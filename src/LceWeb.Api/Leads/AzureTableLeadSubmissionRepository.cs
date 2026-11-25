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
    private const int MaxEmailErrorLength = 2048;
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
            ["PrivacyNoticeVersion"] = submission.PrivacyNoticeVersion ?? string.Empty,
            ["PrivacyAcceptedUtc"] = submission.PrivacyAcceptedUtc ?? submission.CreatedUtc,
            ["DefinitionVersion"] = submission.DefinitionVersion,
            ["Language"] = submission.Language,
            ["Name"] = submission.Contact.Name,
            ["Email"] = submission.Contact.Email,
            ["CallingCode"] = submission.Contact.Phone?.CallingCode,
            ["PhoneNumber"] = submission.Contact.Phone?.Number,
            ["UtmSource"] = submission.Acquisition.UtmSource,
            ["UtmMedium"] = submission.Acquisition.UtmMedium,
            ["UtmCampaign"] = submission.Acquisition.UtmCampaign,
            ["PageUrl"] = submission.Acquisition.PageUrl,
            ["EmailStatus"] = LeadEmailStatus.Pending.ToString(),
            ["FollowUpStatus"] = "new",
            ["EmailStatusUpdatedUtc"] = submission.CreatedUtc,
            ["EmailOperationId"] = string.Empty,
            ["EmailError"] = string.Empty,
            ["CommercialScore"] = submission.Scores.TryGetValue("commercial", out var commercialScore) ? commercialScore : null,
            ["InterestTags"] = string.Join(",", submission.InterestTags),
            ["SubmissionJson"] = submissionJson
        };

        // Fixed, allowlisted boolean fields support server-side filtering without substring scans.
        foreach (var tag in submission.InterestTags.Where(LeadAdminFilter.Interests.Contains))
            entity[LeadAdminFilter.InterestField(tag)] = true;
        foreach (var product in submission.Answers.Where(a => a.QuestionId == "products").SelectMany(a => a.AnswerIds).Where(LeadAdminFilter.Products.Contains).Distinct())
            entity[LeadAdminFilter.ProductField(product)] = true;

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

        if (!response.HasValue || response.Value is null)
        {
            return null;
        }

        return Parse(response.Value, diagnosticId, submissionId);
    }

    private LeadSubmission Parse(TableEntity entity, Guid diagnosticId, Guid submissionId)
    {
        var submissionJson = entity.GetString("SubmissionJson");
        if (string.IsNullOrWhiteSpace(submissionJson))
        {
            throw new InvalidDataException(
                $"Lead submission entity {entity.PartitionKey}/{entity.RowKey} does not contain SubmissionJson.");
        }

        var submission = JsonSerializer.Deserialize<LeadSubmission>(submissionJson, _serializerOptions)
            ?? throw new InvalidDataException(
                $"Lead submission entity {entity.PartitionKey}/{entity.RowKey} contains invalid JSON.");

        if (submission.DiagnosticId != diagnosticId || submission.Id != submissionId)
        {
            throw new InvalidDataException(
                $"Lead submission entity {entity.PartitionKey}/{entity.RowKey} does not match its stored identifiers.");
        }

        return submission;
    }

    public async ValueTask<LeadAdminPage> ListAdminAsync(Guid diagnosticId, LeadAdminFilter filter, string? continuation, CancellationToken cancellationToken = default)
    {
        var partition = diagnosticId.ToString("D");
        if (!filter.IsValid) throw new ArgumentException("Invalid filter.");
        var query = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        if (filter.MinimumScore.HasValue) query += " and " + TableClient.CreateQueryFilter($"CommercialScore ge {filter.MinimumScore.Value}");
        if (!string.IsNullOrEmpty(filter.Interest)) query += " and " + LeadAdminFilter.InterestField(filter.Interest) + " eq true";
        if (!string.IsNullOrEmpty(filter.Product)) query += " and " + LeadAdminFilter.ProductField(filter.Product) + " eq true";
        if (!string.IsNullOrEmpty(filter.Language)) query += " and " + TableClient.CreateQueryFilter($"Language eq {filter.Language}");
        var fields = new[] { "CreatedUtc", "Name", "CommercialScore", "Language", "UtmCampaign", "FollowUpStatus", "EmailStatus", "InterestTags" };
        await foreach (var page in tableClient.QueryAsync<TableEntity>(query, 50, fields, cancellationToken).AsPages(continuation, 50))
        {
            var rows = page.Values.Select(entity => new LeadAdminRow(
                Guid.Parse(entity.RowKey), entity.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.MinValue,
                entity.GetString("Name") ?? "", entity.GetInt32("CommercialScore"),
                entity.GetString("Language") ?? "es", entity.GetString("UtmCampaign") ?? "",
                entity.GetString("FollowUpStatus") ?? "new", entity.GetString("EmailStatus") ?? "",
                entity.GetString("InterestTags") ?? "")).OrderByDescending(x => x.CreatedUtc).ToArray();
            return new LeadAdminPage(rows, page.ContinuationToken);
        }
        return new LeadAdminPage([], null);
    }

    public async ValueTask<LeadAdminDetail?> GetAdminAsync(Guid diagnosticId, Guid submissionId, CancellationToken cancellationToken = default)
    {
        var result = await tableClient.GetEntityIfExistsAsync<TableEntity>(diagnosticId.ToString("D"), submissionId.ToString("D"), cancellationToken: cancellationToken);
        if (!result.HasValue || result.Value is null) return null;
        var entity = result.Value;
        return new LeadAdminDetail(Parse(entity, diagnosticId, submissionId), entity.GetString("FollowUpStatus") ?? "new",
            entity.GetString("EmailStatus") ?? "", entity.ETag.ToString(), entity.GetString("FollowUpActor"), entity.GetDateTimeOffset("FollowUpUpdatedUtc"));
    }

    public async ValueTask<bool> UpdateFollowUpAsync(Guid diagnosticId, Guid submissionId, string status, string revision, string actor, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!LeadFollowUp.IsValid(status) || string.IsNullOrWhiteSpace(revision) || revision == "*") return false;
        var result = await tableClient.GetEntityIfExistsAsync<TableEntity>(diagnosticId.ToString("D"), submissionId.ToString("D"), cancellationToken: cancellationToken);
        if (!result.HasValue || result.Value is null || result.Value.ETag.ToString() != revision) return false;
        var entity = new TableEntity(diagnosticId.ToString("D"), submissionId.ToString("D"))
        {
            ["FollowUpStatus"] = status, ["FollowUpActor"] = actor, ["FollowUpUpdatedUtc"] = now
        };
        try
        {
            await tableClient.UpdateEntityAsync(entity, result.Value.ETag, TableUpdateMode.Merge, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { return false; }
    }

    public async ValueTask UpdateEmailDeliveryAsync(
        Guid diagnosticId,
        Guid submissionId,
        LeadEmailDeliveryState delivery,
        CancellationToken cancellationToken = default)
    {
        var error = delivery.Error ?? string.Empty;
        if (error.Length > MaxEmailErrorLength)
        {
            error = error[..MaxEmailErrorLength];
        }

        var entity = new TableEntity(
            diagnosticId.ToString("D"),
            submissionId.ToString("D"))
        {
            ["EmailStatus"] = delivery.Status.ToString(),
            ["EmailStatusUpdatedUtc"] = delivery.UpdatedUtc,
            ["EmailOperationId"] = delivery.ProviderOperationId ?? string.Empty,
            ["EmailError"] = error
        };

        await tableClient.UpdateEntityAsync(
            entity,
            ETag.All,
            TableUpdateMode.Merge,
            cancellationToken);
    }
}
