namespace LceWeb.Api.Diagnostics;

public sealed class DiagnosticStorageOptions
{
    public const string SectionName = "Diagnostics:Storage";
    public const string MemoryProvider = "Memory";
    public const string AzureTableProvider = "AzureTable";

    public string Provider { get; init; } = MemoryProvider;
    public string TableName { get; init; } = "DiagnosticDefinitions";
    public string? TableEndpoint { get; init; }
    public string? ConnectionString { get; init; }
}
