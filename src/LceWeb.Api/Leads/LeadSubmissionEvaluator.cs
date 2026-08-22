using System.Net.Mail;
using System.Text.RegularExpressions;
using LceWeb.Api.Diagnostics;

namespace LceWeb.Api.Leads;

public static partial class LeadSubmissionEvaluator
{
    private const int MaxNameLength = 200;
    private const int MaxEmailLength = 320;
    private const int MaxAcquisitionValueLength = 256;
    private const int MaxUrlLength = 2048;

    public static LeadSubmissionEvaluation Evaluate(
        DiagnosticDefinition definition,
        LeadSubmissionRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var contact = NormalizeContact(request.Contact, errors);
        var acquisition = NormalizeAcquisition(request.Acquisition, errors);

        var allQuestions = definition.Steps
            .SelectMany(step => step.Questions)
            .ToDictionary(question => question.Id, StringComparer.Ordinal);

        var answersByQuestion = new Dictionary<string, LeadAnswerRequest>(StringComparer.Ordinal);
        foreach (var answer in request.Answers)
        {
            if (string.IsNullOrWhiteSpace(answer.QuestionId))
            {
                AddError(errors, "answers", "Cada respuesta debe incluir questionId.");
                continue;
            }

            if (!answersByQuestion.TryAdd(answer.QuestionId, answer))
            {
                AddError(errors, $"answers.{answer.QuestionId}", "La pregunta fue enviada más de una vez.");
            }

            if (!allQuestions.ContainsKey(answer.QuestionId))
            {
                AddError(errors, $"answers.{answer.QuestionId}", "La pregunta no existe en esta versión del diagnóstico.");
            }
        }

        var visibleQuestions = GetVisibleQuestions(definition, answersByQuestion);
        var visibleQuestionIds = visibleQuestions
            .Select(question => question.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var submittedQuestionId in answersByQuestion.Keys)
        {
            if (allQuestions.ContainsKey(submittedQuestionId) && !visibleQuestionIds.Contains(submittedQuestionId))
            {
                AddError(errors, $"answers.{submittedQuestionId}", "La pregunta no es visible para la ruta de respuestas enviada.");
            }
        }

        var normalizedAnswers = new List<LeadAnswer>();
        foreach (var question in visibleQuestions)
        {
            answersByQuestion.TryGetValue(question.Id, out var submittedAnswer);
            var normalized = ValidateAndNormalizeAnswer(question, submittedAnswer, errors);
            if (normalized is not null)
            {
                normalizedAnswers.Add(normalized);
            }
        }

        var scores = ComputeScores(definition, visibleQuestions, normalizedAnswers);
        var results = ResolveResults(definition, scores);

        return new LeadSubmissionEvaluation
        {
            Errors = errors.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal),
            Contact = contact,
            Answers = normalizedAnswers,
            Acquisition = acquisition,
            Scores = scores,
            Results = results
        };
    }

    private static LeadContact? NormalizeContact(
        LeadContactRequest contact,
        IDictionary<string, List<string>> errors)
    {
        var name = contact.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            AddError(errors, "contact.name", "El nombre es obligatorio.");
        }
        else if (name.Length > MaxNameLength)
        {
            AddError(errors, "contact.name", $"El nombre no puede exceder {MaxNameLength} caracteres.");
        }

        var email = contact.Email?.Trim() ?? string.Empty;
        if (email.Length == 0)
        {
            AddError(errors, "contact.email", "El correo electrónico es obligatorio.");
        }
        else if (email.Length > MaxEmailLength ||
                 !MailAddress.TryCreate(email, out var parsedEmail) ||
                 !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, "contact.email", "El correo electrónico no es válido.");
        }

        LeadPhone? phone = null;
        if (contact.Phone is not null)
        {
            var callingCode = contact.Phone.CallingCode?.Trim() ?? string.Empty;
            var number = PhoneFormattingCharacters().Replace(contact.Phone.Number?.Trim() ?? string.Empty, string.Empty);

            if (!CallingCode().IsMatch(callingCode))
            {
                AddError(errors, "contact.phone.callingCode", "La clave LD debe usar formato internacional, por ejemplo +52.");
            }

            if (!PhoneDigits().IsMatch(number))
            {
                AddError(errors, "contact.phone.number", "El teléfono debe contener entre 7 y 15 dígitos.");
            }

            if (CallingCode().IsMatch(callingCode) && PhoneDigits().IsMatch(number))
            {
                phone = new LeadPhone
                {
                    CallingCode = callingCode,
                    Number = number
                };
            }
        }

        if (errors.Keys.Any(key => key.StartsWith("contact.", StringComparison.Ordinal)))
        {
            return null;
        }

        return new LeadContact
        {
            Name = name,
            Email = email,
            Phone = phone
        };
    }

    private static LeadAcquisition NormalizeAcquisition(
        LeadAcquisitionRequest? acquisition,
        IDictionary<string, List<string>> errors)
    {
        acquisition ??= new LeadAcquisitionRequest();

        return new LeadAcquisition
        {
            UtmSource = NormalizeText(acquisition.UtmSource, "acquisition.utmSource", MaxAcquisitionValueLength, errors),
            UtmMedium = NormalizeText(acquisition.UtmMedium, "acquisition.utmMedium", MaxAcquisitionValueLength, errors),
            UtmCampaign = NormalizeText(acquisition.UtmCampaign, "acquisition.utmCampaign", MaxAcquisitionValueLength, errors),
            UtmTerm = NormalizeText(acquisition.UtmTerm, "acquisition.utmTerm", MaxAcquisitionValueLength, errors),
            UtmContent = NormalizeText(acquisition.UtmContent, "acquisition.utmContent", MaxAcquisitionValueLength, errors),
            Referrer = NormalizeUrl(acquisition.Referrer, "acquisition.referrer", errors),
            PageUrl = NormalizeUrl(acquisition.PageUrl, "acquisition.pageUrl", errors)
        };
    }

    private static string? NormalizeText(
        string? value,
        string key,
        int maxLength,
        IDictionary<string, List<string>> errors)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            AddError(errors, key, $"El valor no puede exceder {maxLength} caracteres.");
            return null;
        }

        return normalized;
    }

    private static string? NormalizeUrl(
        string? value,
        string key,
        IDictionary<string, List<string>> errors)
    {
        var normalized = NormalizeText(value, key, MaxUrlLength, errors);
        if (normalized is null)
        {
            return null;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            AddError(errors, key, "La URL debe ser absoluta y usar http o https.");
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static IReadOnlyList<DiagnosticQuestion> GetVisibleQuestions(
        DiagnosticDefinition definition,
        IReadOnlyDictionary<string, LeadAnswerRequest> answers)
    {
        return definition.Steps
            .OrderBy(step => step.Order)
            .Where(step => ConditionMatches(step.VisibleWhen, answers))
            .SelectMany(step => step.Questions
                .OrderBy(question => question.Order)
                .Where(question => ConditionMatches(question.VisibleWhen, answers)))
            .ToArray();
    }

    private static bool ConditionMatches(
        DiagnosticCondition? condition,
        IReadOnlyDictionary<string, LeadAnswerRequest> answers)
    {
        if (condition is null)
        {
            return true;
        }

        answers.TryGetValue(condition.QuestionId, out var sourceAnswer);
        var selected = sourceAnswer?.AnswerIds ?? [];
        var hasAny = condition.Values.Any(selected.Contains);

        return condition.Operator switch
        {
            DiagnosticConditionOperator.AnyOf => hasAny,
            DiagnosticConditionOperator.NoneOf => !hasAny,
            _ => false
        };
    }

    private static LeadAnswer? ValidateAndNormalizeAnswer(
        DiagnosticQuestion question,
        LeadAnswerRequest? answer,
        IDictionary<string, List<string>> errors)
    {
        if (question.Type == DiagnosticQuestionType.Text)
        {
            var text = answer?.Text?.Trim() ?? string.Empty;
            if (question.Required && text.Length == 0)
            {
                AddError(errors, $"answers.{question.Id}", "Esta pregunta es obligatoria.");
            }

            if (question.MaxLength is not null && text.Length > question.MaxLength.Value)
            {
                AddError(errors, $"answers.{question.Id}", $"La respuesta no puede exceder {question.MaxLength.Value} caracteres.");
            }

            if (answer is not null && answer.AnswerIds.Count > 0)
            {
                AddError(errors, $"answers.{question.Id}", "Una pregunta de texto no acepta answerIds.");
            }

            return text.Length == 0
                ? null
                : new LeadAnswer { QuestionId = question.Id, Text = text };
        }

        if (!string.IsNullOrWhiteSpace(answer?.Text))
        {
            AddError(errors, $"answers.{question.Id}", "Esta pregunta no acepta una respuesta de texto.");
        }

        var selected = answer?.AnswerIds ?? [];
        if (selected.Count != selected.Distinct(StringComparer.Ordinal).Count())
        {
            AddError(errors, $"answers.{question.Id}", "La misma opción no puede enviarse más de una vez.");
        }

        var validOptions = question.Options
            .Select(option => option.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (selected.Any(value => !validOptions.Contains(value)))
        {
            AddError(errors, $"answers.{question.Id}", "La respuesta contiene una opción no válida.");
        }

        switch (question.Type)
        {
            case DiagnosticQuestionType.SingleChoice:
                if (question.Required && selected.Count != 1)
                {
                    AddError(errors, $"answers.{question.Id}", "Selecciona una opción.");
                }
                else if (selected.Count > 1)
                {
                    AddError(errors, $"answers.{question.Id}", "Selecciona sólo una opción.");
                }
                break;

            case DiagnosticQuestionType.MultipleChoice:
                var minimum = question.MinSelections ?? (question.Required ? 1 : 0);
                var maximum = question.MaxSelections ?? int.MaxValue;
                if (selected.Count < minimum)
                {
                    AddError(errors, $"answers.{question.Id}", $"Selecciona al menos {minimum} opción(es).");
                }
                if (selected.Count > maximum)
                {
                    AddError(errors, $"answers.{question.Id}", $"Selecciona como máximo {maximum} opción(es).");
                }
                break;
        }

        return selected.Count == 0
            ? null
            : new LeadAnswer
            {
                QuestionId = question.Id,
                AnswerIds = selected.ToArray()
            };
    }

    private static IReadOnlyDictionary<string, int> ComputeScores(
        DiagnosticDefinition definition,
        IReadOnlyList<DiagnosticQuestion> visibleQuestions,
        IReadOnlyList<LeadAnswer> answers)
    {
        var scores = definition.Dimensions
            .ToDictionary(dimension => dimension.Id, _ => 0, StringComparer.Ordinal);
        var answersByQuestion = answers.ToDictionary(answer => answer.QuestionId, StringComparer.Ordinal);

        foreach (var question in visibleQuestions.Where(question => question.Type != DiagnosticQuestionType.Text))
        {
            if (!answersByQuestion.TryGetValue(question.Id, out var answer))
            {
                continue;
            }

            var selected = answer.AnswerIds.ToHashSet(StringComparer.Ordinal);
            foreach (var option in question.Options.Where(option => selected.Contains(option.Id)))
            {
                foreach (var score in option.Scores)
                {
                    scores[score.Key] = scores.GetValueOrDefault(score.Key) + score.Value;
                }
            }
        }

        return scores;
    }

    private static IReadOnlyList<LeadResult> ResolveResults(
        DiagnosticDefinition definition,
        IReadOnlyDictionary<string, int> scores)
    {
        return definition.Results
            .Where(result =>
            {
                var score = scores.GetValueOrDefault(result.DimensionId);
                return score >= result.MinScore &&
                       (result.MaxScore is null || score <= result.MaxScore.Value);
            })
            .Select(result => new LeadResult
            {
                Id = result.Id,
                DimensionId = result.DimensionId,
                Label = result.Label,
                Summary = result.Summary
            })
            .ToArray();
    }

    private static void AddError(
        IDictionary<string, List<string>> errors,
        string key,
        string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    [GeneratedRegex(@"^\+[1-9]\d{0,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CallingCode();

    [GeneratedRegex(@"^\d{7,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneDigits();

    [GeneratedRegex(@"[\s\-().]", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneFormattingCharacters();
}
