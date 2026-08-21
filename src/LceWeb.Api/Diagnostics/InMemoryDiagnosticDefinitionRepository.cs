namespace LceWeb.Api.Diagnostics;

public sealed class InMemoryDiagnosticDefinitionRepository : IDiagnosticDefinitionRepository
{
    private readonly IReadOnlyDictionary<Guid, DiagnosticDefinition> _latestById;

    public InMemoryDiagnosticDefinitionRepository(IEnumerable<DiagnosticDefinition> definitions)
    {
        _latestById = definitions
            .GroupBy(definition => definition.Id)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(definition => definition.Version).First());
    }

    public ValueTask<DiagnosticDefinition?> GetLatestAsync(
        Guid diagnosticId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _latestById.TryGetValue(diagnosticId, out var definition);
        return ValueTask.FromResult(definition);
    }
}
