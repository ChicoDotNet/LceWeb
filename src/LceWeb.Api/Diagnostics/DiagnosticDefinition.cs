namespace LceWeb.Api.Diagnostics;

public enum DiagnosticQuestionType
{
    SingleChoice,
    MultipleChoice,
    Text
}

public enum DiagnosticConditionOperator
{
    AnyOf,
    NoneOf
}

public sealed record DiagnosticDefinition
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public bool IsActive { get; init; }
    public required string Name { get; init; }
    public required DiagnosticCopy Copy { get; init; }
    public IReadOnlyList<DiagnosticDimension> Dimensions { get; init; } = [];
    public IReadOnlyList<DiagnosticStep> Steps { get; init; } = [];
    public IReadOnlyList<DiagnosticResultBand> Results { get; init; } = [];
}

public sealed record DiagnosticCopy
{
    public required string IntroTitle { get; init; }
    public string? IntroBody { get; init; }
    public required string CompletionTitle { get; init; }
    public string? CompletionBody { get; init; }
}

public sealed record DiagnosticDimension
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}

public sealed record DiagnosticStep
{
    public required string Id { get; init; }
    public int Order { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public DiagnosticCondition? VisibleWhen { get; init; }
    public IReadOnlyList<DiagnosticQuestion> Questions { get; init; } = [];
}

public sealed record DiagnosticQuestion
{
    public required string Id { get; init; }
    public int Order { get; init; }
    public required string Prompt { get; init; }
    public string? HelpText { get; init; }
    public DiagnosticQuestionType Type { get; init; }
    public bool Required { get; init; }
    public int? MinSelections { get; init; }
    public int? MaxSelections { get; init; }
    public int? MaxLength { get; init; }
    public DiagnosticCondition? VisibleWhen { get; init; }
    public IReadOnlyList<DiagnosticAnswerOption> Options { get; init; } = [];
}

public sealed record DiagnosticAnswerOption
{
    public required string Id { get; init; }
    public int Order { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, int> Scores { get; init; } = new Dictionary<string, int>();
}

public sealed record DiagnosticCondition
{
    public required string QuestionId { get; init; }
    public DiagnosticConditionOperator Operator { get; init; }
    public IReadOnlyList<string> Values { get; init; } = [];
}

public sealed record DiagnosticResultBand
{
    public required string Id { get; init; }
    public required string DimensionId { get; init; }
    public int MinScore { get; init; }
    public int? MaxScore { get; init; }
    public required string Label { get; init; }
    public required string Summary { get; init; }
}
