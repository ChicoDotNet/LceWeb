using System.Globalization;
using System.Net;
using System.Text;
using LceWeb.Api.Diagnostics;
using LceWeb.Api.Leads;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LceWeb.Api.Admin;

public static class AdminEndpoints
{
    private static readonly Guid DiagnosticId = Guid.Parse("16f5812b-6a27-44f6-b5b4-557a720a6425");
    public static void MapAdmin(this WebApplication app, AdminConfiguration config)
    {
        if (!config.Enabled) return;
        app.MapGet("/admin/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/admin" }, ["Entra"]));
        app.MapGet("/admin/denied", () => Results.Text("Acceso no autorizado. Solicita un rol de aplicación LCE.", statusCode: 403));
        var group = app.MapGroup("/admin").RequireAuthorization("LceStaff");
        group.MapGet("", async (HttpContext context, ILeadSubmissionRepository repository, IAntiforgery antiforgery, int? minScore, string? next, CancellationToken ct) =>
        {
            if (minScore is < 0 or > 17 || next is { Length: > 4096 }) return Results.BadRequest();
            var result = await repository.ListAdminAsync(DiagnosticId, minScore, next, ct);
            var token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            var body = new StringBuilder("<h1>Prospectos LCE</h1><p>50 registros por página, ordenados por fecha dentro de la página. V1 conserva su historial sin score comercial.</p>");
            body.Append("<form method='get'><label>Score mínimo (0–17) <input type='number' name='minScore' min='0' max='17' value='").Append(minScore?.ToString(CultureInfo.InvariantCulture)).Append("'></label> <button>Filtrar</button> <a href='/admin'>Limpiar</a></form>");
            body.Append("<div class='scroll'><table><thead><tr><th>Fecha UTC</th><th>Prospecto</th><th>Score</th><th>Idioma</th><th>Campaña</th><th>Intereses</th><th>Seguimiento</th><th>Correo</th></tr></thead><tbody>");
            foreach (var row in result.Rows)
                body.Append($"<tr><td>{row.CreatedUtc:yyyy-MM-dd HH:mm}</td><td><a href='/admin/leads/{row.Id:D}'>{E(row.Name)}</a></td><td>{(row.Score?.ToString(CultureInfo.InvariantCulture) ?? "—")}</td><td>{E(row.Language)}</td><td>{E(row.Campaign)}</td><td>{E(row.Interests)}</td><td>{E(Label(row.Status))}</td><td>{E(row.EmailStatus)}</td></tr>");
            body.Append("</tbody></table></div>");
            if (result.Rows.Count == 0) body.Append("<p>No hay resultados en esta página.</p>");
            if (!string.IsNullOrEmpty(result.Continuation))
                body.Append($"<p><a href='/admin?minScore={minScore}&amp;next={Uri.EscapeDataString(result.Continuation)}'>Siguiente página →</a></p>");
            body.Append(SignOut(token));
            return Html(body.ToString());
        });
        group.MapGet("/leads/{id:guid}", async (Guid id, HttpContext context, ILeadSubmissionRepository repository, IDiagnosticDefinitionRepository definitions, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            var detail = await repository.GetAdminAsync(DiagnosticId, id, ct);
            if (detail is null) return Results.NotFound();
            var lead = detail.Submission;
            var definition = await definitions.GetVersionAsync(lead.DiagnosticId, lead.DefinitionVersion, ct);
            var token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            var body = new StringBuilder($"<p><a href='/admin'>← Prospectos</a></p><h1>{E(lead.Contact.Name)}</h1><p>Registrado: {lead.CreatedUtc:yyyy-MM-dd HH:mm} UTC · Versión {lead.DefinitionVersion} · {E(lead.Language)}</p>");
            body.Append($"<dl><dt>Correo</dt><dd>{E(lead.Contact.Email)}</dd><dt>Teléfono</dt><dd>{E(lead.Contact.Phone is null ? null : lead.Contact.Phone.CallingCode + " " + lead.Contact.Phone.Number)}</dd><dt>Intereses</dt><dd>{E(string.Join(", ",lead.InterestTags))}</dd><dt>Campaña</dt><dd>{E(lead.Acquisition.UtmCampaign)}</dd><dt>Consentimiento</dt><dd>{E(lead.PrivacyNoticeVersion)} · {lead.PrivacyAcceptedUtc:O}</dd><dt>Correo de alerta</dt><dd>{E(detail.EmailStatus)}</dd><dt>Score</dt><dd>{E(string.Join(" · ", lead.Scores.Select(kv => kv.Key + ": " + kv.Value)))}</dd></dl>");
            body.Append("<h2>Respuestas</h2><dl>");
            var questions = definition?.Steps.SelectMany(x => x.Questions).ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var answer in lead.Answers)
            {
                var question = questions?.GetValueOrDefault(answer.QuestionId);
                var labels = answer.AnswerIds.Select(x => question?.Options.FirstOrDefault(o => o.Id == x)?.Label ?? x);
                body.Append($"<dt>{E(question?.Prompt ?? answer.QuestionId)}</dt><dd>{E(string.Join(", ",labels))} {E(answer.Text)}</dd>");
            }
            body.Append("</dl><h2>Seguimiento</h2>");
            body.Append($"<p>Estado actual: {E(Label(detail.Status))}. Última actualización: {detail.LastUpdatedUtc:O}.</p><form method='post' action='/admin/leads/{id:D}/status'>{Hidden("__RequestVerificationToken",token)}{Hidden("revision",detail.Revision)}<label>Estado <select name='status'>");
            foreach (var state in LeadFollowUp.States) body.Append($"<option value='{state}'{(state == detail.Status ? " selected" : "")}>{E(Label(state))}</option>");
            body.Append("</select></label> <button>Guardar</button></form>");
            body.Append("<h2>Fichas técnicas</h2>");
            if (lead.Attachments.Count == 0) body.Append("<p>Sin adjuntos.</p>");
            else
            {
                body.Append("<p>Archivos en cuarentena. La descarga permanece deshabilitada hasta validar un flujo de escaneo confiable.</p><ul>");
                foreach (var item in lead.Attachments) body.Append($"<li>{E(item.FileName)} · {item.Length} bytes</li>");
                body.Append("</ul>");
            }
            body.Append(SignOut(token));
            return Html(body.ToString());
        });
        group.MapPost("/leads/{id:guid}/status", async (Guid id, HttpContext context, IAntiforgery antiforgery, ILeadSubmissionRepository repository, TimeProvider clock, CancellationToken ct) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            var form = await context.Request.ReadFormAsync(ct);
            var status = form["status"].ToString();
            var revision = form["revision"].ToString();
            var actor = context.User.FindFirst("oid")?.Value;
            if (!Guid.TryParse(actor, out _) || !LeadFollowUp.IsValid(status) || revision.Length > 256) return Results.BadRequest();
            var updated = await repository.UpdateFollowUpAsync(DiagnosticId, id, status, revision, actor!, clock.GetUtcNow(), ct);
            return updated ? Results.Redirect($"/admin/leads/{id:D}") : Results.Conflict("El registro cambió. Recarga antes de guardar.");
        });
        group.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        });
    }

    private static string Label(string status) => status switch
    {
        "new" => "Nuevo", "contacted" => "Contactado", "appointment" => "Cita", "proposal" => "Propuesta", "won" => "Ganado", "lost" => "No concretado", _ => status
    };
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Hidden(string name,string value) => $"<input type='hidden' name='{E(name)}' value='{E(value)}'>";
    private static string SignOut(string token) => $"<form method='post' action='/admin/logout'>{Hidden("__RequestVerificationToken",token)}<button>Cerrar sesión local</button></form>";
    private static IResult Html(string body) => Results.Content("<!doctype html><html lang='es-MX'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><meta name='robots' content='noindex,nofollow'><title>LCE · Back office</title><style>body{font:16px/1.6 system-ui;color:#163b50;margin:32px;max-width:1200px}a{color:#1269a8}h1{line-height:1.2}form{margin:16px 0}input,select,button{font:inherit;padding:6px 10px}table{border-collapse:collapse;width:100%}th,td{text-align:left;vertical-align:top;padding:9px;border-bottom:1px solid #d7e2ea}th{background:#163b50;color:white}.scroll{overflow:auto}dt{font-weight:700}dd{margin:0 0 12px;overflow-wrap:anywhere}</style></head><body><header><strong>LCE | Uso interno</strong></header><main>" + body + "</main></body></html>", "text/html; charset=utf-8");
}
