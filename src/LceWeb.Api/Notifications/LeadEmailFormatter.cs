using System.Net;
using System.Text;
using LceWeb.Api.Diagnostics;
using LceWeb.Api.Leads;

namespace LceWeb.Api.Notifications;

public static class LeadEmailFormatter
{
    public static LeadEmailMessage Format(
        DiagnosticDefinition definition,
        LeadSubmission submission,
        LeadEmailOptions options)
    {
        var subject = $"{options.SubjectPrefix}: {definition.Name} — {submission.Contact.Name}";
        var plainText = BuildPlainText(definition, submission);
        var html = BuildHtml(definition, submission);

        return new LeadEmailMessage
        {
            Subject = subject,
            PlainText = plainText,
            Html = html
        };
    }

    private static string BuildPlainText(DiagnosticDefinition definition, LeadSubmission submission)
    {
        var builder = new StringBuilder();
        builder.AppendLine("NUEVO PROSPECTO");
        builder.AppendLine();
        builder.AppendLine($"Diagnóstico: {definition.Name}");
        builder.AppendLine($"Versión: {definition.Version}");
        builder.AppendLine($"Lead ID: {submission.Id:D}");
        builder.AppendLine($"Fecha UTC: {submission.CreatedUtc:O}");
        builder.AppendLine();
        builder.AppendLine("CONTACTO");
        builder.AppendLine($"Nombre: {submission.Contact.Name}");
        builder.AppendLine($"Correo: {submission.Contact.Email}");
        builder.AppendLine($"Teléfono: {FormatPhone(submission.Contact.Phone)}");

        AppendAcquisitionPlain(builder, submission.Acquisition);

        builder.AppendLine();
        builder.AppendLine("AUTODIAGNÓSTICO");
        AppendAnswersPlain(builder, definition, submission.Answers);

        builder.AppendLine();
        builder.AppendLine("RESULTADOS");
        AppendResultsPlain(builder, definition, submission);

        return builder.ToString().TrimEnd();
    }

    private static string BuildHtml(DiagnosticDefinition definition, LeadSubmission submission)
    {
        var builder = new StringBuilder();
        builder.Append("<!doctype html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937;line-height:1.5\">");
        builder.Append("<div style=\"max-width:760px;margin:0 auto\">");
        builder.Append("<h1 style=\"font-size:24px\">Nuevo prospecto</h1>");
        builder.Append($"<p><strong>Diagnóstico:</strong> {Encode(definition.Name)}<br>");
        builder.Append($"<strong>Versión:</strong> {definition.Version}<br>");
        builder.Append($"<strong>Lead ID:</strong> {submission.Id:D}<br>");
        builder.Append($"<strong>Fecha UTC:</strong> {Encode(submission.CreatedUtc.ToString("O"))}</p>");

        builder.Append("<h2 style=\"font-size:20px\">Contacto</h2><table style=\"border-collapse:collapse;width:100%\">");
        AppendHtmlRow(builder, "Nombre", submission.Contact.Name);
        AppendHtmlRow(builder, "Correo", submission.Contact.Email);
        AppendHtmlRow(builder, "Teléfono", FormatPhone(submission.Contact.Phone));
        builder.Append("</table>");

        AppendAcquisitionHtml(builder, submission.Acquisition);

        builder.Append("<h2 style=\"font-size:20px\">Autodiagnóstico</h2>");
        AppendAnswersHtml(builder, definition, submission.Answers);

        builder.Append("<h2 style=\"font-size:20px\">Resultados</h2>");
        AppendResultsHtml(builder, definition, submission);

        builder.Append("</div></body></html>");
        return builder.ToString();
    }

    private static void AppendAnswersPlain(
        StringBuilder builder,
        DiagnosticDefinition definition,
        IReadOnlyList<LeadAnswer> answers)
    {
        var answersByQuestion = answers.ToDictionary(answer => answer.QuestionId, StringComparer.Ordinal);

        foreach (var step in definition.Steps.OrderBy(step => step.Order))
        {
            var answeredQuestions = step.Questions
                .OrderBy(question => question.Order)
                .Where(question => answersByQuestion.ContainsKey(question.Id))
                .ToArray();

            if (answeredQuestions.Length == 0)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine(step.Title.ToUpperInvariant());
            foreach (var question in answeredQuestions)
            {
                var answer = answersByQuestion[question.Id];
                builder.AppendLine($"{question.Prompt}: {FormatAnswer(question, answer)}");
            }
        }
    }

    private static void AppendAnswersHtml(
        StringBuilder builder,
        DiagnosticDefinition definition,
        IReadOnlyList<LeadAnswer> answers)
    {
        var answersByQuestion = answers.ToDictionary(answer => answer.QuestionId, StringComparer.Ordinal);

        foreach (var step in definition.Steps.OrderBy(step => step.Order))
        {
            var answeredQuestions = step.Questions
                .OrderBy(question => question.Order)
                .Where(question => answersByQuestion.ContainsKey(question.Id))
                .ToArray();

            if (answeredQuestions.Length == 0)
            {
                continue;
            }

            builder.Append($"<h3 style=\"font-size:16px;margin-bottom:8px\">{Encode(step.Title)}</h3>");
            builder.Append("<table style=\"border-collapse:collapse;width:100%;margin-bottom:20px\">");
            foreach (var question in answeredQuestions)
            {
                AppendHtmlRow(builder, question.Prompt, FormatAnswer(question, answersByQuestion[question.Id]));
            }
            builder.Append("</table>");
        }
    }

    private static string FormatAnswer(DiagnosticQuestion question, LeadAnswer answer)
    {
        if (question.Type == DiagnosticQuestionType.Text)
        {
            return string.IsNullOrWhiteSpace(answer.Text) ? "—" : answer.Text;
        }

        var options = question.Options.ToDictionary(option => option.Id, StringComparer.Ordinal);
        var labels = answer.AnswerIds
            .Select(answerId => options.TryGetValue(answerId, out var option) ? option.Label : answerId)
            .ToArray();

        return labels.Length == 0 ? "—" : string.Join(", ", labels);
    }

    private static void AppendResultsPlain(
        StringBuilder builder,
        DiagnosticDefinition definition,
        LeadSubmission submission)
    {
        var dimensions = definition.Dimensions.ToDictionary(dimension => dimension.Id, StringComparer.Ordinal);
        foreach (var score in submission.Scores)
        {
            var label = dimensions.TryGetValue(score.Key, out var dimension) ? dimension.Label : score.Key;
            builder.AppendLine($"{label}: {score.Value}");
        }

        foreach (var result in submission.Results)
        {
            builder.AppendLine($"{result.Label}: {result.Summary}");
        }
    }

    private static void AppendResultsHtml(
        StringBuilder builder,
        DiagnosticDefinition definition,
        LeadSubmission submission)
    {
        builder.Append("<table style=\"border-collapse:collapse;width:100%;margin-bottom:20px\">");
        var dimensions = definition.Dimensions.ToDictionary(dimension => dimension.Id, StringComparer.Ordinal);
        foreach (var score in submission.Scores)
        {
            var label = dimensions.TryGetValue(score.Key, out var dimension) ? dimension.Label : score.Key;
            AppendHtmlRow(builder, label, score.Value.ToString());
        }
        builder.Append("</table>");

        foreach (var result in submission.Results)
        {
            builder.Append("<div style=\"border:1px solid #d1d5db;border-radius:8px;padding:12px;margin:8px 0\">");
            builder.Append($"<strong>{Encode(result.Label)}</strong><br>{Encode(result.Summary)}");
            builder.Append("</div>");
        }
    }

    private static void AppendAcquisitionPlain(StringBuilder builder, LeadAcquisition acquisition)
    {
        if (!HasAcquisition(acquisition))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("ADQUISICIÓN");
        AppendPlainField(builder, "UTM source", acquisition.UtmSource);
        AppendPlainField(builder, "UTM medium", acquisition.UtmMedium);
        AppendPlainField(builder, "UTM campaign", acquisition.UtmCampaign);
        AppendPlainField(builder, "UTM term", acquisition.UtmTerm);
        AppendPlainField(builder, "UTM content", acquisition.UtmContent);
        AppendPlainField(builder, "Referrer", acquisition.Referrer);
        AppendPlainField(builder, "Página", acquisition.PageUrl);
    }

    private static void AppendAcquisitionHtml(StringBuilder builder, LeadAcquisition acquisition)
    {
        if (!HasAcquisition(acquisition))
        {
            return;
        }

        builder.Append("<h2 style=\"font-size:20px\">Adquisición</h2><table style=\"border-collapse:collapse;width:100%\">");
        AppendHtmlRowIfPresent(builder, "UTM source", acquisition.UtmSource);
        AppendHtmlRowIfPresent(builder, "UTM medium", acquisition.UtmMedium);
        AppendHtmlRowIfPresent(builder, "UTM campaign", acquisition.UtmCampaign);
        AppendHtmlRowIfPresent(builder, "UTM term", acquisition.UtmTerm);
        AppendHtmlRowIfPresent(builder, "UTM content", acquisition.UtmContent);
        AppendHtmlRowIfPresent(builder, "Referrer", acquisition.Referrer);
        AppendHtmlRowIfPresent(builder, "Página", acquisition.PageUrl);
        builder.Append("</table>");
    }

    private static bool HasAcquisition(LeadAcquisition acquisition) =>
        !string.IsNullOrWhiteSpace(acquisition.UtmSource) ||
        !string.IsNullOrWhiteSpace(acquisition.UtmMedium) ||
        !string.IsNullOrWhiteSpace(acquisition.UtmCampaign) ||
        !string.IsNullOrWhiteSpace(acquisition.UtmTerm) ||
        !string.IsNullOrWhiteSpace(acquisition.UtmContent) ||
        !string.IsNullOrWhiteSpace(acquisition.Referrer) ||
        !string.IsNullOrWhiteSpace(acquisition.PageUrl);

    private static string FormatPhone(LeadPhone? phone) =>
        phone is null ? "—" : $"{phone.CallingCode} {phone.Number}";

    private static void AppendPlainField(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"{label}: {value}");
        }
    }

    private static void AppendHtmlRowIfPresent(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            AppendHtmlRow(builder, label, value);
        }
    }

    private static void AppendHtmlRow(StringBuilder builder, string label, string value)
    {
        builder.Append("<tr>");
        builder.Append($"<th style=\"text-align:left;vertical-align:top;padding:6px 12px 6px 0;width:30%\">{Encode(label)}</th>");
        builder.Append($"<td style=\"padding:6px 0\">{Encode(value)}</td>");
        builder.Append("</tr>");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
