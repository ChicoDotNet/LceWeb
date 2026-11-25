namespace LceWeb.Api.Leads;

public sealed record LeadAttachment
{
    public required string BlobName { get; init; }
    public required string FileName { get; init; }
    public required string MediaType { get; init; }
    public long Length { get; init; }
    public required string Sha256 { get; init; }
    // No file may be served without a trusted clean scan and an authorized user.
    public string ScanStatus { get; init; } = "Pending";
}
