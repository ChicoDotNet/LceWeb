using System.Net.Mail;
using Azure.Communication.Email;
using Azure.Identity;

namespace LceWeb.Api.Notifications;

public static class LeadNotificationRegistration
{
    public static IServiceCollection AddLeadNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(LeadEmailOptions.SectionName);
        var recipients = ParseRecipients(section["Recipients"]);
        var options = new LeadEmailOptions
        {
            Provider = section["Provider"] ?? LeadEmailOptions.NoneProvider,
            Endpoint = section["Endpoint"],
            ConnectionString = section["ConnectionString"],
            SenderAddress = section["SenderAddress"],
            SenderDisplayName = section["SenderDisplayName"] ?? "LCE Comercial",
            SubjectPrefix = section["SubjectPrefix"] ?? "Nuevo prospecto LCE",
            Recipients = recipients
        };

        services.AddSingleton(options);

        if (string.Equals(options.Provider, LeadEmailOptions.NoneProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ILeadNotificationSender, DisabledLeadNotificationSender>();
            return services;
        }

        if (!string.Equals(
                options.Provider,
                LeadEmailOptions.AzureCommunicationServicesProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported email provider '{options.Provider}'. Expected '{LeadEmailOptions.NoneProvider}' or '{LeadEmailOptions.AzureCommunicationServicesProvider}'.");
        }

        ValidateAzureCommunicationServicesOptions(options);
        services.AddSingleton(_ => CreateEmailClient(options));
        services.AddSingleton<ILeadNotificationSender, AzureCommunicationServicesLeadNotificationSender>();
        return services;
    }

    private static EmailClient CreateEmailClient(LeadEmailOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new EmailClient(options.ConnectionString);
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                $"{LeadEmailOptions.SectionName}:Endpoint must be an absolute URI when using Azure Communication Services without a connection string.");
        }

        return new EmailClient(endpoint, new DefaultAzureCredential());
    }

    private static void ValidateAzureCommunicationServicesOptions(LeadEmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString) &&
            !Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Configure {LeadEmailOptions.SectionName}:Endpoint for Managed Identity authentication, or ConnectionString only for local/emergency use.");
        }

        if (string.IsNullOrWhiteSpace(options.SenderAddress) ||
            !MailAddress.TryCreate(options.SenderAddress, out _))
        {
            throw new InvalidOperationException(
                $"{LeadEmailOptions.SectionName}:SenderAddress must be a valid MailFrom address from a domain connected to Azure Communication Services.");
        }

        if (options.Recipients.Count == 0)
        {
            throw new InvalidOperationException(
                $"{LeadEmailOptions.SectionName}:Recipients must contain at least one email address.");
        }
    }

    private static IReadOnlyList<string> ParseRecipients(string? rawRecipients)
    {
        if (string.IsNullOrWhiteSpace(rawRecipients))
        {
            return [];
        }

        var recipients = rawRecipients
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var recipient in recipients)
        {
            if (!MailAddress.TryCreate(recipient, out _))
            {
                throw new InvalidOperationException(
                    $"{LeadEmailOptions.SectionName}:Recipients contains invalid email address '{recipient}'.");
            }
        }

        return recipients;
    }
}
