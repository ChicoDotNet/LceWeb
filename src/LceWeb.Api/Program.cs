using System.Threading.RateLimiting;
using System.Text.Json;
using LceWeb.Api.Attachments;
using LceWeb.Api.Admin;
using Microsoft.AspNetCore.Http.Features;
using LceWeb.Api.Diagnostics;
using LceWeb.Api.Leads;
using LceWeb.Api.Notifications;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 128 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options => DiagnosticJson.Configure(options.SerializerOptions));
builder.Services.AddDiagnosticDefinitionRepository(builder.Configuration, builder.Environment);
builder.Services.AddLeadSubmissionRepository(builder.Configuration);
builder.Services.AddLeadNotifications(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PrivateAttachmentStore>();
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

var admin = AdminConfiguration.Register(builder.Services, builder.Configuration);
var app = builder.Build();

var leadStorage = app.Services.GetRequiredService<LeadStorageOptions>();
// Production is fail-closed: Azure Tables, both operator switches, and a published
// approval version are required. Development keeps isolated CI fixtures available.
var privacyNoticeVersion = app.Environment.IsDevelopment()
    ? "development-test"
    : builder.Configuration["Leads:PrivacyNoticeVersion"]?.Trim();
var publicLeadCaptureEnabled = app.Environment.IsDevelopment() ||
    (string.Equals(
        leadStorage.Provider,
        LeadStorageOptions.AzureTableProvider,
        StringComparison.OrdinalIgnoreCase) &&
     builder.Configuration.GetValue<bool>("Leads:PublicCaptureEnabled") &&
     builder.Configuration.GetValue<bool>("Leads:PrivacyNoticeApproved") &&
     !string.IsNullOrWhiteSpace(privacyNoticeVersion));

var attachmentStore = app.Services.GetRequiredService<PrivateAttachmentStore>();
var attachmentsEnabled = publicLeadCaptureEnabled && admin.Enabled && attachmentStore.IsConfigured && attachmentStore.IsCleanReadConfigured &&
    builder.Configuration.GetValue<bool>("Attachments:PublicUploadEnabled") &&
    builder.Configuration.GetValue<bool>("Attachments:MalwareScanningConfigured") &&
    string.Equals(builder.Configuration["Attachments:PrivacyNoticeVersion"], privacyNoticeVersion, StringComparison.Ordinal);

// Back office is deliberately unavailable unless Entra and durable storage are configured.
if (admin.Enabled && !string.Equals(leadStorage.Provider, LeadStorageOptions.AzureTableProvider, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Admin requires durable Azure Table storage.");
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin") || context.Request.Path.StartsWithSegments("/signin-oidc"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        if (!admin.Enabled) { context.Response.StatusCode = 404; return; }
    }
    await next();
});
if (admin.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/public-capabilities", () => Results.Ok(new
    {
        leadCaptureEnabled = publicLeadCaptureEnabled,
        attachmentsEnabled,
        privacyNoticeVersion = publicLeadCaptureEnabled ? privacyNoticeVersion : null
    }))
    .WithName("GetPublicCapabilities");

app.MapGet(
    "/api/diagnostics/{diagnosticId}",
    async (string diagnosticId, int? version, IDiagnosticDefinitionRepository repository, CancellationToken cancellationToken) =>
    {
        if (!Guid.TryParse(diagnosticId, out var id) || id == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid diagnostic id",
                detail: "diagnosticId must be a non-empty GUID.");
        }

        var definition = version.HasValue
            ? await repository.GetVersionAsync(id, version.Value, cancellationToken)
            : await repository.GetLatestAsync(id, cancellationToken);
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

        // The public commercial questionnaire must not expose internal weighting or bands.
        return Results.Ok(definition.Version >= 2 ? CommercialDiagnostic.PublicDefinition(definition) : definition);
    })
    .WithName("GetDiagnosticDefinition");

app.MapPost(
        "/api/leads",
        async (
            HttpRequest httpRequest,
            IDiagnosticDefinitionRepository diagnosticRepository,
            ILeadSubmissionRepository leadRepository,
            ILeadNotificationSender notificationSender,
            TimeProvider timeProvider,
            IHostApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (!publicLeadCaptureEnabled)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Public lead capture is not enabled",
                    detail: "Contacta a LCE por correo o teléfono mientras habilitamos el registro seguro.");
            }

            LeadSubmissionRequest? request;
            IFormFileCollection? files = null;
            try
            {
                if (httpRequest.HasFormContentType)
                {
                    if (!attachmentsEnabled || httpRequest.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
                        return Results.Problem(statusCode: 415, title: "Attachment upload is unavailable.");
                    var limit = httpRequest.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                    if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = PrivateAttachmentStore.MaxRequestBytes;
                    var form = await httpRequest.ReadFormAsync(new FormOptions
                    {
                        MultipartBodyLengthLimit = PrivateAttachmentStore.MaxFileBytes,
                        ValueLengthLimit = 128 * 1024,
                        ValueCountLimit = 1
                    }, cancellationToken);
                    if (form.Count != 1 || form["payload"].Count != 1 || form["payload"].ToString().Length > 128 * 1024)
                        return Results.BadRequest(new { title = "Invalid attachment payload." });
                    request = JsonSerializer.Deserialize<LeadSubmissionRequest>(form["payload"].ToString(), DiagnosticJson.CreateOptions());
                    files = form.Files;
                }
                else if (httpRequest.HasJsonContentType())
                    request = await httpRequest.ReadFromJsonAsync<LeadSubmissionRequest>(DiagnosticJson.CreateOptions(), cancellationToken);
                else return Results.Problem(statusCode: 415, title: "Unsupported content type.");
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or BadHttpRequestException)
            {
                return Results.BadRequest(new { title = "Invalid request or attachment size." });
            }
            if (request is null || request.Contact is null || request.Answers is null)
                return Results.BadRequest(new { title = "Invalid lead submission." });

            // The browser checkbox is not evidence on its own. Require the
            // exact published notice revision and persist a server-side timestamp.
            if (!request.PrivacyAccepted ||
                !string.Equals(request.PrivacyNoticeVersion, privacyNoticeVersion, StringComparison.Ordinal))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["privacyAccepted"] = ["Debes aceptar el aviso de privacidad vigente para enviar la solicitud."],
                    ["privacyNoticeVersion"] = ["La versión del aviso no coincide con la versión publicada. Recarga la página."]
                });
            }

            if (!string.IsNullOrWhiteSpace(request.Website))
            {
                return Results.Accepted(value: new { accepted = true });
            }

            var requestErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (request.DiagnosticId == Guid.Empty)
            {
                requestErrors["diagnosticId"] = ["diagnosticId must be a non-empty GUID."];
            }

            if (request.Language is not null && request.Language is not ("es" or "en" or "fr" or "pt"))
                requestErrors["language"] = ["Idioma no admitido."];

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

            var acceptedUtc = timeProvider.GetUtcNow();
            var submissionId = Guid.NewGuid();
            IReadOnlyList<LeadAttachment> attachments = [];
            var scores = evaluation.Scores;
            var bands = evaluation.Results;
            if (files is { Count: > 0 })
            {
                if (definition.Version != 2 || !evaluation.Answers.Any(a => a.QuestionId == "technical" && a.AnswerIds.Contains("available")))
                    return Results.BadRequest(new { title = "Attachments require technical information." });
                try
                {
                    attachments = await attachmentStore.StoreAsync(submissionId, files, cancellationToken);
                }
                catch (InvalidDataException)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["attachments"] = ["Adjunta hasta tres archivos PDF, JPG o PNG de hasta 10 MB cada uno."] });
                }
                var adjusted = new Dictionary<string, int>(scores, StringComparer.Ordinal);
                adjusted["commercial"] = Math.Min(17, adjusted.GetValueOrDefault("commercial") + 1);
                scores = adjusted;
                bands = definition.Results.Where(r => r.DimensionId == "commercial" && adjusted["commercial"] >= r.MinScore && (r.MaxScore is null || adjusted["commercial"] <= r.MaxScore))
                    .Select(r => new LeadResult { Id = r.Id, DimensionId = r.DimensionId, Label = r.Label, Summary = r.Summary }).ToArray();
            }
            var submission = new LeadSubmission
            {
                Id = submissionId,
                Attachments = attachments,
                Language = request.Language ?? "es",
                CreatedUtc = acceptedUtc,
                PrivacyNoticeVersion = privacyNoticeVersion,
                PrivacyAcceptedUtc = acceptedUtc,
                DiagnosticId = definition.Id,
                DefinitionVersion = definition.Version,
                Contact = evaluation.Contact!,
                Answers = evaluation.Answers,
                Acquisition = evaluation.Acquisition,
                Scores = scores,
                Results = bands,
                InterestTags = CommercialDiagnostic.InterestTags(evaluation.Answers, evaluation.Acquisition)
            };

            // If persistence fails after uploading, keep the private blobs for reconciliation.
            // A timeout may mean Table Storage committed successfully; deleting could destroy a valid lead's evidence.
            await leadRepository.StoreAsync(submission, cancellationToken);

            var logger = loggerFactory.CreateLogger("LeadNotifications");
            LeadNotificationResult notificationResult;
            try
            {
                notificationResult = await notificationSender.SendAsync(
                    definition,
                    submission,
                    applicationLifetime.ApplicationStopping);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Unexpected email notification failure for lead {LeadId}.",
                    submission.Id);
                notificationResult = LeadNotificationResult.Failed("Unexpected email notification failure.");
            }

            var emailStatus = notificationResult.Status switch
            {
                LeadNotificationStatus.Disabled => LeadEmailStatus.Disabled,
                LeadNotificationStatus.Sent => LeadEmailStatus.Sent,
                LeadNotificationStatus.Failed => LeadEmailStatus.Failed,
                _ => LeadEmailStatus.Failed
            };

            try
            {
                await leadRepository.UpdateEmailDeliveryAsync(
                    submission.DiagnosticId,
                    submission.Id,
                    new LeadEmailDeliveryState
                    {
                        Status = emailStatus,
                        UpdatedUtc = timeProvider.GetUtcNow(),
                        ProviderOperationId = notificationResult.ProviderOperationId,
                        Error = notificationResult.Error
                    },
                    applicationLifetime.ApplicationStopping);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Lead {LeadId} was stored, but its email delivery status could not be updated.",
                    submission.Id);
            }

            if (notificationResult.Status == LeadNotificationStatus.Failed)
            {
                logger.LogWarning(
                    "Lead {LeadId} was stored but email notification failed: {EmailError}",
                    submission.Id,
                    notificationResult.Error);
            }

            return Results.Json(
                new LeadSubmissionResponse
                {
                    SubmissionId = submission.Id,
                    CreatedUtc = submission.CreatedUtc,
                    DiagnosticId = submission.DiagnosticId,
                    DefinitionVersion = submission.DefinitionVersion,
                    Scores = definition.Version >= 2 ? new Dictionary<string, int>() : submission.Scores,
                    Results = definition.Version >= 2 ? [] : submission.Results
                },
                statusCode: StatusCodes.Status201Created);
        })
    .RequireRateLimiting("lead-submissions")
    .WithName("CreateLeadSubmission");

app.MapAdmin(admin);
app.Run();

public partial class Program;
