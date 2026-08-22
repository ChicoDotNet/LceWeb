using System.Text.Json;

namespace LceWeb.Api.Diagnostics;

public static class DiagnosticDefinitionLoader
{
    public static IReadOnlyCollection<DiagnosticDefinition> LoadDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Diagnostic definition directory was not found: {directoryPath}");
        }

        var serializerOptions = DiagnosticJson.CreateOptions();
        var definitions = new List<DiagnosticDefinition>();

        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            var json = File.ReadAllText(filePath);
            var definition = JsonSerializer.Deserialize<DiagnosticDefinition>(json, serializerOptions)
                ?? throw new InvalidDataException($"Diagnostic definition is empty or invalid JSON: {filePath}");

            var errors = DiagnosticDefinitionValidator.Validate(definition);
            if (errors.Count > 0)
            {
                throw new InvalidDataException(
                    $"Diagnostic definition {filePath} is invalid:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
            }

            definitions.Add(definition);
        }

        if (definitions.Count == 0)
        {
            throw new InvalidDataException($"No diagnostic definition JSON files were found in: {directoryPath}");
        }

        return definitions;
    }
}
