namespace LceWeb.Api.Diagnostics;

public static class DiagnosticDefinitionValidator
{
    public static IReadOnlyList<string> Validate(DiagnosticDefinition definition)
    {
        var errors = new List<string>();

        if (definition.Id == Guid.Empty)
        {
            errors.Add("id must be a non-empty GUID.");
        }

        if (definition.Version < 1)
        {
            errors.Add("version must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add("name is required.");
        }

        if (definition.Steps.Count == 0)
        {
            errors.Add("at least one step is required.");
        }

        var dimensionIds = definition.Dimensions.Select(dimension => dimension.Id).ToHashSet(StringComparer.Ordinal);
        if (dimensionIds.Count != definition.Dimensions.Count)
        {
            errors.Add("dimension ids must be unique.");
        }

        var stepIds = new HashSet<string>(StringComparer.Ordinal);
        var questions = new Dictionary<string, DiagnosticQuestion>(StringComparer.Ordinal);

        foreach (var step in definition.Steps)
        {
            if (!stepIds.Add(step.Id))
            {
                errors.Add($"duplicate step id '{step.Id}'.");
            }

            if (step.Questions.Count == 0)
            {
                errors.Add($"step '{step.Id}' must contain at least one question.");
            }

            foreach (var question in step.Questions)
            {
                if (!questions.TryAdd(question.Id, question))
                {
                    errors.Add($"duplicate question id '{question.Id}'.");
                }

                ValidateQuestion(question, dimensionIds, errors);
            }
        }

        foreach (var step in definition.Steps)
        {
            ValidateCondition(step.VisibleWhen, $"step '{step.Id}'", questions, errors);

            foreach (var question in step.Questions)
            {
                ValidateCondition(question.VisibleWhen, $"question '{question.Id}'", questions, errors);
            }
        }

        foreach (var result in definition.Results)
        {
            if (!dimensionIds.Contains(result.DimensionId))
            {
                errors.Add($"result '{result.Id}' references unknown dimension '{result.DimensionId}'.");
            }

            if (result.MaxScore is not null && result.MaxScore < result.MinScore)
            {
                errors.Add($"result '{result.Id}' has maxScore lower than minScore.");
            }
        }

        return errors;
    }

    private static void ValidateQuestion(
        DiagnosticQuestion question,
        IReadOnlySet<string> dimensionIds,
        ICollection<string> errors)
    {
        var optionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var option in question.Options)
        {
            if (!optionIds.Add(option.Id))
            {
                errors.Add($"question '{question.Id}' has duplicate option id '{option.Id}'.");
            }

            foreach (var dimensionId in option.Scores.Keys)
            {
                if (!dimensionIds.Contains(dimensionId))
                {
                    errors.Add($"option '{question.Id}/{option.Id}' scores unknown dimension '{dimensionId}'.");
                }
            }
        }

        switch (question.Type)
        {
            case DiagnosticQuestionType.SingleChoice:
                if (question.Options.Count == 0)
                {
                    errors.Add($"single-choice question '{question.Id}' requires options.");
                }
                break;

            case DiagnosticQuestionType.MultipleChoice:
                if (question.Options.Count == 0)
                {
                    errors.Add($"multiple-choice question '{question.Id}' requires options.");
                }

                if (question.MinSelections is not null && question.MaxSelections is not null &&
                    question.MinSelections > question.MaxSelections)
                {
                    errors.Add($"question '{question.Id}' has minSelections greater than maxSelections.");
                }
                break;

            case DiagnosticQuestionType.Text:
                if (question.Options.Count != 0)
                {
                    errors.Add($"text question '{question.Id}' cannot define answer options.");
                }
                break;

            default:
                errors.Add($"question '{question.Id}' has an unsupported type.");
                break;
        }
    }

    private static void ValidateCondition(
        DiagnosticCondition? condition,
        string owner,
        IReadOnlyDictionary<string, DiagnosticQuestion> questions,
        ICollection<string> errors)
    {
        if (condition is null)
        {
            return;
        }

        if (!questions.TryGetValue(condition.QuestionId, out var sourceQuestion))
        {
            errors.Add($"{owner} references unknown condition question '{condition.QuestionId}'.");
            return;
        }

        if (condition.Values.Count == 0)
        {
            errors.Add($"{owner} condition must contain at least one value.");
            return;
        }

        if (sourceQuestion.Type == DiagnosticQuestionType.Text)
        {
            return;
        }

        var optionIds = sourceQuestion.Options.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var value in condition.Values)
        {
            if (!optionIds.Contains(value))
            {
                errors.Add($"{owner} condition value '{value}' is not an option of question '{condition.QuestionId}'.");
            }
        }
    }
}
