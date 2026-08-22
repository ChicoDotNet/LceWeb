namespace LceWeb.Api.Leads;

public enum LeadEmailStatus
{
    Pending,
    Disabled,
    Sent,
    Failed
}

public sealed record LeadEmailDeliveryState
{
    public LeadEmailStatus Status { get; init; } = LeadEmailStatus.Pending;
    public DateTimeOffset UpdatedUtc { get; init; }
    public string? ProviderOperationId { get; init; }
    public string? Error { get; init; }
}
