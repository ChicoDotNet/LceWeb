using System.Collections.Concurrent;

namespace LceWeb.Api.Leads;

public sealed class InMemoryLeadSubmissionRepository : ILeadSubmissionRepository
{
    private readonly ConcurrentDictionary<(Guid DiagnosticId, Guid SubmissionId), LeadSubmission> _submissions = new();
    private readonly ConcurrentDictionary<(Guid DiagnosticId, Guid SubmissionId), LeadEmailDeliveryState> _emailDeliveries = new();

    private readonly ConcurrentDictionary<(Guid, Guid), (string Status, string Revision, string? Actor, DateTimeOffset? UpdatedUtc)> _followUp = new();

    public ValueTask StoreAsync(
        LeadSubmission submission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (submission.DiagnosticId, submission.Id);
        if (!_submissions.TryAdd(key, submission))
        {
            throw new InvalidOperationException($"Lead submission {submission.Id:D} already exists.");
        }

        _emailDeliveries[key] = new LeadEmailDeliveryState
        {
            Status = LeadEmailStatus.Pending,
            UpdatedUtc = submission.CreatedUtc
        };

        return ValueTask.CompletedTask;
    }

    public ValueTask<LeadSubmission?> GetAsync(
        Guid diagnosticId,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _submissions.TryGetValue((diagnosticId, submissionId), out var submission);
        return ValueTask.FromResult(submission);
    }

    public ValueTask<LeadAdminPage> ListAdminAsync(Guid diagnosticId, LeadAdminFilter filter, string? continuation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var offset = int.TryParse(continuation, out var n) && n >= 0 ? n : 0;
        if (!filter.IsValid) throw new ArgumentException("Invalid filter.");
        var ordered = _submissions.Values.Where(x => x.DiagnosticId == diagnosticId &&
            (!filter.MinimumScore.HasValue || (x.Scores.TryGetValue("commercial", out var value) && value >= filter.MinimumScore.Value)) &&
            (string.IsNullOrEmpty(filter.Interest) || x.InterestTags.Contains(filter.Interest)) &&
            (string.IsNullOrEmpty(filter.Product) || x.Answers.Any(a => a.QuestionId == "products" && a.AnswerIds.Contains(filter.Product))) &&
            (string.IsNullOrEmpty(filter.Language) || x.Language == filter.Language))
            .OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Skip(offset).Take(51).ToArray();
        var rows = ordered.Take(50).Select(x =>
        {
            var key = (x.DiagnosticId, x.Id);
            var state = _followUp.GetOrAdd(key, ("new", "0", null, null));
            return new LeadAdminRow(x.Id, x.CreatedUtc, x.Contact.Name, x.Scores.TryGetValue("commercial", out var score) ? score : null, x.Language, x.Acquisition.UtmCampaign ?? "", state.Status, _emailDeliveries.GetValueOrDefault(key)?.Status.ToString() ?? "", string.Join(",", x.InterestTags));
        }).ToArray();
        return ValueTask.FromResult(new LeadAdminPage(rows, ordered.Length > 50 ? (offset + 50).ToString() : null));
    }

    public ValueTask<LeadAdminDetail?> GetAdminAsync(Guid diagnosticId, Guid submissionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (diagnosticId, submissionId);
        if (!_submissions.TryGetValue(key, out var submission)) return ValueTask.FromResult<LeadAdminDetail?>(null);
        var state = _followUp.GetOrAdd(key, ("new", "0", null, null));
        return ValueTask.FromResult<LeadAdminDetail?>(new(submission, state.Status, _emailDeliveries.GetValueOrDefault(key)?.Status.ToString() ?? "", state.Revision, state.Actor, state.UpdatedUtc));
    }

    public ValueTask<bool> UpdateFollowUpAsync(Guid diagnosticId, Guid submissionId, string status, string revision, string actor, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (diagnosticId, submissionId);
        if (!_submissions.ContainsKey(key) || !LeadFollowUp.IsValid(status)) return ValueTask.FromResult(false);
        var old = _followUp.GetOrAdd(key, ("new", "0", null, null));
        return ValueTask.FromResult(old.Revision == revision && _followUp.TryUpdate(key, (status, Guid.NewGuid().ToString("N"), actor, now), old));
    }

    public ValueTask UpdateEmailDeliveryAsync(
        Guid diagnosticId,
        Guid submissionId,
        LeadEmailDeliveryState delivery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (diagnosticId, submissionId);
        if (!_submissions.ContainsKey(key))
        {
            throw new KeyNotFoundException($"Lead submission {submissionId:D} does not exist.");
        }

        _emailDeliveries[key] = delivery;
        return ValueTask.CompletedTask;
    }
}
