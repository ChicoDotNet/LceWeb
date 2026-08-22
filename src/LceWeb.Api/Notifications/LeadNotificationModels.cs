namespace LceWeb.Api.Notifications;

public enum LeadNotificationStatus
{
    Disabled,
    Sent,
    Failed
}

public sealed record LeadNotificationResult
{
    public LeadNotificationStatus Status { get; init; }
    public string? ProviderOperationId { get; init; }
    public string? Error { get; init; }

    public static LeadNotificationResult Disabled() => new()
    {
        Status = LeadNotificationStatus.Disabled
    };

    public static LeadNotificationResult Sent(string providerOperationId) => new()
    {
        Status = LeadNotificationStatus.Sent,
        ProviderOperationId = providerOperationId
    };

    public static LeadNotificationResult Failed(string error, string? providerOperationId = null) => new()
    {
        Status = LeadNotificationStatus.Failed,
        ProviderOperationId = providerOperationId,
        Error = error
    };
}

public sealed record LeadEmailMessage
{
    public required string Subject { get; init; }
    public required string PlainText { get; init; }
    public required string Html { get; init; }
}
