using System.Collections.Concurrent;

namespace LceWeb.Api.Leads;

public sealed class InMemoryLeadSubmissionRepository : ILeadSubmissionRepository
{
    private readonly ConcurrentDictionary<(Guid DiagnosticId, Guid SubmissionId), LeadSubmission> _submissions = new();
    private readonly ConcurrentDictionary<(Guid DiagnosticId, Guid SubmissionId), LeadEmailDeliveryState> _emailDeliveries = new();

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
