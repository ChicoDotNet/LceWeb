namespace LceWeb.Api.Notifications;

public sealed class LeadEmailOptions
{
    public const string SectionName = "Email";
    public const string NoneProvider = "None";
    public const string AzureCommunicationServicesProvider = "AzureCommunicationServices";

    public string Provider { get; init; } = NoneProvider;
    public string? Endpoint { get; init; }
    public string? ConnectionString { get; init; }
    public string? SenderAddress { get; init; }
    public string SubjectPrefix { get; init; } = "Nuevo prospecto LCE";
    public IReadOnlyList<string> Recipients { get; init; } = [];
}
