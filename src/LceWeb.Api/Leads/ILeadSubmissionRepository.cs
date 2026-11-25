namespace LceWeb.Api.Leads;

public interface ILeadSubmissionRepository
{
    ValueTask StoreAsync(
        LeadSubmission submission,
        CancellationToken cancellationToken = default);

    ValueTask<LeadSubmission?> GetAsync(
        Guid diagnosticId,
        Guid submissionId,
        CancellationToken cancellationToken = default);

    ValueTask<LeadAdminPage> ListAdminAsync(Guid diagnosticId, LeadAdminFilter filter, string? continuation, CancellationToken cancellationToken = default);
    ValueTask<LeadAdminDetail?> GetAdminAsync(Guid diagnosticId, Guid submissionId, CancellationToken cancellationToken = default);
    ValueTask<bool> UpdateFollowUpAsync(Guid diagnosticId, Guid submissionId, string status, string revision, string actor, DateTimeOffset now, CancellationToken cancellationToken = default);

    ValueTask UpdateEmailDeliveryAsync(
        Guid diagnosticId,
        Guid submissionId,
        LeadEmailDeliveryState delivery,
        CancellationToken cancellationToken = default);
}
