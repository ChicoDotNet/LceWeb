using Azure;
using Azure.Communication.Email;
using LceWeb.Api.Diagnostics;
using LceWeb.Api.Leads;

namespace LceWeb.Api.Notifications;

public sealed class AzureCommunicationServicesLeadNotificationSender(
    EmailClient emailClient,
    LeadEmailOptions options) : ILeadNotificationSender
{
    public async ValueTask<LeadNotificationResult> SendAsync(
        DiagnosticDefinition definition,
        LeadSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var formatted = LeadEmailFormatter.Format(definition, submission, options);
        var content = new EmailContent(formatted.Subject)
        {
            PlainText = formatted.PlainText,
            Html = formatted.Html
        };

        var recipients = new EmailRecipients(
            options.Recipients.Select(address => new EmailAddress(address)));
        var message = new EmailMessage(options.SenderAddress!, recipients, content);

        try
        {
            var operation = await emailClient.SendAsync(
                WaitUntil.Completed,
                message,
                cancellationToken);

            return LeadNotificationResult.Sent(operation.Id);
        }
        catch (RequestFailedException exception)
        {
            var error = string.IsNullOrWhiteSpace(exception.ErrorCode)
                ? exception.Message
                : $"{exception.ErrorCode}: {exception.Message}";
            return LeadNotificationResult.Failed(error);
        }
    }
}
