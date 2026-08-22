namespace LceWeb.Api.Diagnostics;

public sealed class InMemoryDiagnosticDefinitionRepository : IDiagnosticDefinitionRepository
{
    private readonly IReadOnlyDictionary<Guid, DiagnosticDefinition> _latestById;
    private readonly IReadOnlyDictionary<(Guid DiagnosticId, int Version), DiagnosticDefinition> _byVersion;

    public InMemoryDiagnosticDefinitionRepository(IEnumerable<DiagnosticDefinition> definitions)
    {
        var materialized = definitions.ToArray();

        _latestById = materialized
            .GroupBy(definition => definition.Id)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(definition => definition.Version).First());

        _byVersion = materialized.ToDictionary(
            definition => (definition.Id, definition.Version));
    }

    public ValueTask<DiagnosticDefinition?> GetLatestAsync(
        Guid diagnosticId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _latestById.TryGetValue(diagnosticId, out var definition);
        return ValueTask.FromResult(definition);
    }

    public ValueTask<DiagnosticDefinition?> GetVersionAsync(
        Guid diagnosticId,
        int version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _byVersion.TryGetValue((diagnosticId, version), out var definition);
        return ValueTask.FromResult(definition);
    }
}
