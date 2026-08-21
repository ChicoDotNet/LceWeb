using System.Text.Json;
using Azure.Data.Tables;

namespace LceWeb.Api.Diagnostics;

public sealed class AzureTableDiagnosticDefinitionRepository(TableClient tableClient)
    : IDiagnosticDefinitionRepository
{
    private readonly JsonSerializerOptions _serializerOptions = DiagnosticJson.CreateOptions();

    public async ValueTask<DiagnosticDefinition?> GetLatestAsync(
        Guid diagnosticId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = diagnosticId.ToString("D");
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
        DiagnosticDefinition? latest = null;

        await foreach (var entity in tableClient.QueryAsync<TableEntity>(
                           filter: filter,
                           cancellationToken: cancellationToken))
        {
            var definitionJson = entity.GetString("DefinitionJson");
            if (string.IsNullOrWhiteSpace(definitionJson))
            {
                throw new InvalidDataException(
                    $"Diagnostic table entity {entity.PartitionKey}/{entity.RowKey} does not contain DefinitionJson.");
            }

            var definition = JsonSerializer.Deserialize<DiagnosticDefinition>(definitionJson, _serializerOptions)
                ?? throw new InvalidDataException(
                    $"Diagnostic table entity {entity.PartitionKey}/{entity.RowKey} contains invalid JSON.");

            var errors = DiagnosticDefinitionValidator.Validate(definition);
            if (errors.Count > 0)
            {
                throw new InvalidDataException(
                    $"Diagnostic table entity {entity.PartitionKey}/{entity.RowKey} is invalid:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
            }

            if (definition.Id != diagnosticId)
            {
                throw new InvalidDataException(
                    $"Diagnostic table entity {entity.PartitionKey}/{entity.RowKey} contains definition id {definition.Id:D}, expected {diagnosticId:D}.");
            }

            var storedVersion = entity.GetInt32("Version");
            if (storedVersion is not null && storedVersion.Value != definition.Version)
            {
                throw new InvalidDataException(
                    $"Diagnostic table entity {entity.PartitionKey}/{entity.RowKey} has Version={storedVersion.Value}, but the JSON contains version {definition.Version}.");
            }

            if (latest is null || definition.Version > latest.Version)
            {
                latest = definition;
            }
        }

        return latest;
    }
}
