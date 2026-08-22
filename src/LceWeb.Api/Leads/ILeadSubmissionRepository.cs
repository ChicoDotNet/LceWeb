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

    ValueTask UpdateEmailDeliveryAsync(
        Guid diagnosticId,
        Guid submissionId,
        LeadEmailDeliveryState delivery,
        CancellationToken cancellationToken = default);
}
