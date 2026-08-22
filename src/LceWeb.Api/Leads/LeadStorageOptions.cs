namespace LceWeb.Api.Leads;

public sealed class LeadStorageOptions
{
    public const string SectionName = "Leads:Storage";
    public const string MemoryProvider = "Memory";
    public const string AzureTableProvider = "AzureTable";

    public string Provider { get; init; } = MemoryProvider;
    public string TableName { get; init; } = "LeadSubmissions";
    public string? TableEndpoint { get; init; }
    public string? ConnectionString { get; init; }
}
