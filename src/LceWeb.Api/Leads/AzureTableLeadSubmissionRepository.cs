using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using LceWeb.Api.Diagnostics;

namespace LceWeb.Api.Leads;

public sealed class AzureTableLeadSubmissionRepository(TableClient tableClient)
    : ILeadSubmissionRepository
{
    private const int MaxSubmissionJsonBytes = 61440;
    private readonly JsonSerializerOptions _serializerOptions = DiagnosticJson.CreateOptions();

    public async ValueTask StoreAsync(
        LeadSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var submissionJson = JsonSerializer.Serialize(submission, _serializerOptions);
        var byteCount = Encoding.Unicode.GetByteCount(submissionJson);
        if (byteCount > MaxSubmissionJsonBytes)
        {
            throw new InvalidDataException(
                $"Lead submission {submission.Id:D} is {byteCount} UTF-16 bytes. SubmissionJson must remain below 60 KiB.");
        }

        var entity = new TableEntity(
            submission.DiagnosticId.ToString("D"),
            submission.Id.ToString("D"))
        {
            ["CreatedUtc"] = submission.CreatedUtc,
            ["DefinitionVersion"] = submission.DefinitionVersion,
            ["Name"] = submission.Contact.Name,
            ["Email"] = submission.Contact.Email,
            ["CallingCode"] = submission.Contact.Phone?.CallingCode,
            ["PhoneNumber"] = submission.Contact.Phone?.Number,
            ["UtmSource"] = submission.Acquisition.UtmSource,
            ["UtmMedium"] = submission.Acquisition.UtmMedium,
            ["UtmCampaign"] = submission.Acquisition.UtmCampaign,
            ["PageUrl"] = submission.Acquisition.PageUrl,
            ["EmailStatus"] = "Pending",
            ["SubmissionJson"] = submissionJson
        };

        try
        {
            await tableClient.AddEntityAsync(entity, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status409Conflict)
        {
            throw new InvalidOperationException(
                $"Lead submission {submission.Id:D} already exists.",
                exception);
        }
    }

    public async ValueTask<LeadSubmission?> GetAsync(
        Guid diagnosticId,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(
            diagnosticId.ToString("D"),
            submissionId.ToString("D"),
            cancellationToken: cancellationToken);

        if (!response.HasValue)
        {
            return null;
        }

        var submissionJson = response.Value.GetString("SubmissionJson");
        if (string.IsNullOrWhiteSpace(submissionJson))
        {
            throw new InvalidDataException(
                $"Lead submission entity {response.Value.PartitionKey}/{response.Value.RowKey} does not contain SubmissionJson.");
        }

        var submission = JsonSerializer.Deserialize<LeadSubmission>(submissionJson, _serializerOptions)
            ?? throw new InvalidDataException(
                $"Lead submission entity {response.Value.PartitionKey}/{response.Value.RowKey} contains invalid JSON.");

        if (submission.DiagnosticId != diagnosticId || submission.Id != submissionId)
        {
            throw new InvalidDataException(
                $"Lead submission entity {response.Value.PartitionKey}/{response.Value.RowKey} does not match its stored identifiers.");
        }

        return submission;
    }
}
