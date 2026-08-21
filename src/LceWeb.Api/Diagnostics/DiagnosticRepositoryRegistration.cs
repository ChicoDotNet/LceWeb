using Azure.Data.Tables;
using Azure.Identity;

namespace LceWeb.Api.Diagnostics;

public static class DiagnosticRepositoryRegistration
{
    public static IServiceCollection AddDiagnosticDefinitionRepository(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(DiagnosticStorageOptions.SectionName);
        var options = new DiagnosticStorageOptions
        {
            Provider = section["Provider"] ?? DiagnosticStorageOptions.MemoryProvider,
            TableName = section["TableName"] ?? "DiagnosticDefinitions",
            TableEndpoint = section["TableEndpoint"],
            ConnectionString = section["ConnectionString"]
        };

        services.AddSingleton(options);

        if (string.Equals(
                options.Provider,
                DiagnosticStorageOptions.MemoryProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            var diagnosticDirectory = Path.Combine(environment.ContentRootPath, "diagnostics");
            var diagnosticDefinitions = DiagnosticDefinitionLoader.LoadDirectory(diagnosticDirectory);
            services.AddSingleton<IDiagnosticDefinitionRepository>(
                new InMemoryDiagnosticDefinitionRepository(diagnosticDefinitions));
            return services;
        }

        if (!string.Equals(
                options.Provider,
                DiagnosticStorageOptions.AzureTableProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported diagnostic storage provider '{options.Provider}'. Expected '{DiagnosticStorageOptions.MemoryProvider}' or '{DiagnosticStorageOptions.AzureTableProvider}'.");
        }

        services.AddSingleton(_ => CreateTableClient(options));
        services.AddSingleton<IDiagnosticDefinitionRepository, AzureTableDiagnosticDefinitionRepository>();
        return services;
    }

    private static TableClient CreateTableClient(DiagnosticStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new TableClient(options.ConnectionString, options.TableName);
        }

        if (!Uri.TryCreate(options.TableEndpoint, UriKind.Absolute, out var tableEndpoint))
        {
            throw new InvalidOperationException(
                $"{DiagnosticStorageOptions.SectionName}:TableEndpoint must be an absolute URI when Provider is '{DiagnosticStorageOptions.AzureTableProvider}'.");
        }

        return new TableClient(tableEndpoint, options.TableName, new DefaultAzureCredential());
    }
}
