using System.Threading.RateLimiting;
using LceWeb.Api.Diagnostics;
using LceWeb.Api.Leads;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 128 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options => DiagnosticJson.Configure(options.SerializerOptions));
builder.Services.AddDiagnosticDefinitionRepository(builder.Configuration, builder.Environment);
builder.Services.AddLeadSubmissionRepository(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("lead-submissions", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

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

app.MapPost(
        "/api/leads",
        async (
            LeadSubmissionRequest request,
            IDiagnosticDefinitionRepository diagnosticRepository,
            ILeadSubmissionRepository leadRepository,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(request.Website))
            {
                return Results.Accepted(value: new { accepted = true });
            }

            var requestErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (request.DiagnosticId == Guid.Empty)
            {
                requestErrors["diagnosticId"] = ["diagnosticId must be a non-empty GUID."];
            }

            if (request.DefinitionVersion < 1)
            {
                requestErrors["definitionVersion"] = ["definitionVersion must be at least 1."];
            }

            if (requestErrors.Count > 0)
            {
                return Results.ValidationProblem(requestErrors);
            }

            var definition = await diagnosticRepository.GetVersionAsync(
                request.DiagnosticId,
                request.DefinitionVersion,
                cancellationToken);
            if (definition is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Diagnostic version not found",
                    detail: "The submitted diagnosticId and definitionVersion do not identify a stored definition.");
            }

            if (!definition.IsActive)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status410Gone,
                    title: "Diagnostic is inactive");
            }

            var evaluation = LeadSubmissionEvaluator.Evaluate(definition, request);
            if (!evaluation.IsValid)
            {
                return Results.ValidationProblem(
                    evaluation.Errors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            }

            var submission = new LeadSubmission
            {
                Id = Guid.NewGuid(),
                CreatedUtc = timeProvider.GetUtcNow(),
                DiagnosticId = definition.Id,
                DefinitionVersion = definition.Version,
                Contact = evaluation.Contact!,
                Answers = evaluation.Answers,
                Acquisition = evaluation.Acquisition,
                Scores = evaluation.Scores,
                Results = evaluation.Results
            };

            await leadRepository.StoreAsync(submission, cancellationToken);

            return Results.Json(
                new LeadSubmissionResponse
                {
                    SubmissionId = submission.Id,
                    CreatedUtc = submission.CreatedUtc,
                    DiagnosticId = submission.DiagnosticId,
                    DefinitionVersion = submission.DefinitionVersion,
                    Scores = submission.Scores,
                    Results = submission.Results
                },
                statusCode: StatusCodes.Status201Created);
        })
    .RequireRateLimiting("lead-submissions")
    .WithName("CreateLeadSubmission");

app.Run();

public partial class Program;
