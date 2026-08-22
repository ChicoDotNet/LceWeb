using Azure.Data.Tables;
using Azure.Identity;
using LceWeb.Api.Diagnostics;

namespace LceWeb.Api.Leads;

public static class LeadRepositoryRegistration
{
    public static IServiceCollection AddLeadSubmissionRepository(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(LeadStorageOptions.SectionName);
        var diagnosticSection = configuration.GetSection(DiagnosticStorageOptions.SectionName);
        var options = new LeadStorageOptions
        {
            Provider = section["Provider"]
                ?? diagnosticSection["Provider"]
                ?? LeadStorageOptions.MemoryProvider,
            TableName = section["TableName"] ?? "LeadSubmissions",
            TableEndpoint = section["TableEndpoint"] ?? diagnosticSection["TableEndpoint"],
            ConnectionString = section["ConnectionString"] ?? diagnosticSection["ConnectionString"]
        };

        services.AddSingleton(options);

        if (string.Equals(
                options.Provider,
                LeadStorageOptions.MemoryProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ILeadSubmissionRepository, InMemoryLeadSubmissionRepository>();
            return services;
        }

        if (!string.Equals(
                options.Provider,
                LeadStorageOptions.AzureTableProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported lead storage provider '{options.Provider}'. Expected '{LeadStorageOptions.MemoryProvider}' or '{LeadStorageOptions.AzureTableProvider}'.");
        }

        services.AddSingleton<ILeadSubmissionRepository>(_ =>
            new AzureTableLeadSubmissionRepository(CreateTableClient(options)));
        return services;
    }

    private static TableClient CreateTableClient(LeadStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new TableClient(options.ConnectionString, options.TableName);
        }

        if (!Uri.TryCreate(options.TableEndpoint, UriKind.Absolute, out var tableEndpoint))
        {
            throw new InvalidOperationException(
                $"{LeadStorageOptions.SectionName}:TableEndpoint must be an absolute URI when Provider is '{LeadStorageOptions.AzureTableProvider}'.");
        }

        return new TableClient(tableEndpoint, options.TableName, new DefaultAzureCredential());
    }
}
