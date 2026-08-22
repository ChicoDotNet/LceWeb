namespace LceWeb.Api.Diagnostics;

public interface IDiagnosticDefinitionRepository
{
    ValueTask<DiagnosticDefinition?> GetLatestAsync(
        Guid diagnosticId,
        CancellationToken cancellationToken = default);

    ValueTask<DiagnosticDefinition?> GetVersionAsync(
        Guid diagnosticId,
        int version,
        CancellationToken cancellationToken = default);
}
