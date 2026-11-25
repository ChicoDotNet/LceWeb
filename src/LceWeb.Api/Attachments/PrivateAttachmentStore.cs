using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LceWeb.Api.Leads;
using System.Security.Cryptography;

namespace LceWeb.Api.Attachments;

public sealed class PrivateAttachmentStore
{
    public const long MaxFileBytes = 10L * 1024 * 1024;
    public const long MaxRequestBytes = 32L * 1024 * 1024;
    private readonly BlobContainerClient? _container;

    public PrivateAttachmentStore(IConfiguration configuration)
    {
        var endpoint = configuration["Attachments:ServiceUri"];
        var container = configuration["Attachments:ContainerName"];
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(container))
            _container = new BlobServiceClient(uri, new DefaultAzureCredential()).GetBlobContainerClient(container);
    }

    public bool IsConfigured => _container is not null;

    public async Task<IReadOnlyList<LeadAttachment>> StoreAsync(Guid leadId, IFormFileCollection files, CancellationToken cancellationToken)
    {
        if (_container is null) throw new InvalidOperationException("Attachment storage is not configured.");
        if (files.Count is < 1 or > 3 || files.Any(f => f.Name != "attachments" || f.Length <= 0 || f.Length > MaxFileBytes))
            throw new InvalidDataException("Invalid file count or length.");

        var properties = await _container.GetPropertiesAsync(cancellationToken: cancellationToken);
        if (properties.Value.PublicAccess != PublicAccessType.None)
            throw new InvalidOperationException("Attachment container must be private.");

        var validated = new List<(IFormFile File, LeadAttachment Metadata)>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name.Any(char.IsControl))
                throw new InvalidDataException("Invalid filename.");
            var extension = Path.GetExtension(name).ToLowerInvariant();
            await using var stream = file.OpenReadStream();
            var prefix = new byte[8];
            var count = await stream.ReadAtLeastAsync(prefix, prefix.Length, throwIfEndOfStream: false, cancellationToken: cancellationToken);
            var media = extension switch
            {
                ".pdf" when count >= 5 && prefix.AsSpan(0, 5).SequenceEqual("%PDF-"u8) => "application/pdf",
                ".jpg" or ".jpeg" when count >= 3 && prefix[0] == 0xff && prefix[1] == 0xd8 && prefix[2] == 0xff => "image/jpeg",
                ".png" when count == 8 && prefix.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => "image/png",
                _ => throw new InvalidDataException("Invalid file signature.")
            };
            stream.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            validated.Add((file, new LeadAttachment
            {
                BlobName = $"{leadId:D}/{Guid.NewGuid():N}",
                FileName = name,
                MediaType = media,
                Length = file.Length,
                Sha256 = hash
            }));
        }
        // Only after all files have passed structural checks. Blob keys are server-generated;
        // the quarantine container is private and never mapped to a public download endpoint.
        var uploaded = new List<LeadAttachment>();
        try
        {
            foreach (var item in validated)
            {
                await using var stream = item.File.OpenReadStream();
                await _container.GetBlobClient(item.Metadata.BlobName).UploadAsync(stream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/octet-stream" },
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                }, cancellationToken);
                uploaded.Add(item.Metadata);
            }
            return uploaded;
        }
        catch
        {
            // Do not log filenames or payloads. Cleanup is best effort and remaining blobs
            // are still private; the operator reconciles abandoned lead IDs.
            foreach (var item in uploaded)
            {
                try { await _container.GetBlobClient(item.BlobName).DeleteIfExistsAsync(cancellationToken: CancellationToken.None); }
                catch { /* Reconcile quarantine orphan through the operations runbook. */ }
            }
            throw;
        }
    }
}
