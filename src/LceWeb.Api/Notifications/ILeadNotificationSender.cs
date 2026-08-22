using LceWeb.Api.Diagnostics;
using LceWeb.Api.Leads;

namespace LceWeb.Api.Notifications;

public interface ILeadNotificationSender
{
    ValueTask<LeadNotificationResult> SendAsync(
        DiagnosticDefinition definition,
        LeadSubmission submission,
        CancellationToken cancellationToken = default);
}
