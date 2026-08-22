using System.Collections.Concurrent;

namespace LceWeb.Api.Leads;

public sealed class InMemoryLeadSubmissionRepository : ILeadSubmissionRepository
{
    private readonly ConcurrentDictionary<(Guid DiagnosticId, Guid SubmissionId), LeadSubmission> _submissions = new();

    public ValueTask StoreAsync(
        LeadSubmission submission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_submissions.TryAdd((submission.DiagnosticId, submission.Id), submission))
        {
            throw new InvalidOperationException($"Lead submission {submission.Id:D} already exists.");
        }

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
}
