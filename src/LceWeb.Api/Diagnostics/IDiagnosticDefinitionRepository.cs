namespace LceWeb.Api.Diagnostics;

public interface IDiagnosticDefinitionRepository
{
    ValueTask<DiagnosticDefinition?> GetLatestAsync(Guid diagnosticId, CancellationToken cancellationToken = default);
}
