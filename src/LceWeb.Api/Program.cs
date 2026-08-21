using LceWeb.Api.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => DiagnosticJson.Configure(options.SerializerOptions));

var diagnosticDirectory = Path.Combine(builder.Environment.ContentRootPath, "diagnostics");
var diagnosticDefinitions = DiagnosticDefinitionLoader.LoadDirectory(diagnosticDirectory);

builder.Services.AddSingleton<IDiagnosticDefinitionRepository>(
    new InMemoryDiagnosticDefinitionRepository(diagnosticDefinitions));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet(
    "/api/diagnostics/{diagnosticId}",
    async (string diagnosticId, IDiagnosticDefinitionRepository repository, CancellationToken cancellationToken) =>
    {
        if (!Guid.TryParse(diagnosticId, out var id) || id == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid diagnostic id",
                detail: "diagnosticId must be a non-empty GUID.");
        }

        var definition = await repository.GetLatestAsync(id, cancellationToken);
        if (definition is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Diagnostic not found");
        }

        if (!definition.IsActive)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Diagnostic is inactive");
        }

        return Results.Ok(definition);
    })
    .WithName("GetDiagnosticDefinition");

app.Run();

public partial class Program;
