using LceWeb.Api.Diagnostics;
using LceWeb.Api.Leads;

namespace LceWeb.Api.Notifications;

public sealed class DisabledLeadNotificationSender : ILeadNotificationSender
{
    public ValueTask<LeadNotificationResult> SendAsync(
        DiagnosticDefinition definition,
        LeadSubmission submission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(LeadNotificationResult.Disabled());
    }
}
