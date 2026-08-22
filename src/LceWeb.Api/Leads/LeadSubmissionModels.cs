using LceWeb.Api.Diagnostics;

namespace LceWeb.Api.Leads;

public sealed record LeadSubmissionRequest
{
    public Guid DiagnosticId { get; init; }
    public int DefinitionVersion { get; init; }
    public required LeadContactRequest Contact { get; init; }
    public IReadOnlyList<LeadAnswerRequest> Answers { get; init; } = [];
    public LeadAcquisitionRequest? Acquisition { get; init; }

    // Honeypot. Legitimate clients must leave this empty.
    public string? Website { get; init; }
}

public sealed record LeadContactRequest
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public LeadPhoneRequest? Phone { get; init; }
}

public sealed record LeadPhoneRequest
{
    public required string CallingCode { get; init; }
    public required string Number { get; init; }
}

public sealed record LeadAnswerRequest
{
    public required string QuestionId { get; init; }
    public IReadOnlyList<string> AnswerIds { get; init; } = [];
    public string? Text { get; init; }
}

public sealed record LeadAcquisitionRequest
{
    public string? UtmSource { get; init; }
    public string? UtmMedium { get; init; }
    public string? UtmCampaign { get; init; }
    public string? UtmTerm { get; init; }
    public string? UtmContent { get; init; }
    public string? Referrer { get; init; }
    public string? PageUrl { get; init; }
}

public sealed record LeadContact
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public LeadPhone? Phone { get; init; }
}

public sealed record LeadPhone
{
    public required string CallingCode { get; init; }
    public required string Number { get; init; }
}

public sealed record LeadAnswer
{
    public required string QuestionId { get; init; }
    public IReadOnlyList<string> AnswerIds { get; init; } = [];
    public string? Text { get; init; }
}

public sealed record LeadAcquisition
{
    public string? UtmSource { get; init; }
    public string? UtmMedium { get; init; }
    public string? UtmCampaign { get; init; }
    public string? UtmTerm { get; init; }
    public string? UtmContent { get; init; }
    public string? Referrer { get; init; }
    public string? PageUrl { get; init; }
}

public sealed record LeadResult
{
    public required string Id { get; init; }
    public required string DimensionId { get; init; }
    public required string Label { get; init; }
    public required string Summary { get; init; }
}

public sealed record LeadSubmission
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public Guid DiagnosticId { get; init; }
    public int DefinitionVersion { get; init; }
    public required LeadContact Contact { get; init; }
    public IReadOnlyList<LeadAnswer> Answers { get; init; } = [];
    public required LeadAcquisition Acquisition { get; init; }
    public IReadOnlyDictionary<string, int> Scores { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<LeadResult> Results { get; init; } = [];
}

public sealed record LeadSubmissionResponse
{
    public Guid SubmissionId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public Guid DiagnosticId { get; init; }
    public int DefinitionVersion { get; init; }
    public IReadOnlyDictionary<string, int> Scores { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<LeadResult> Results { get; init; } = [];
}

public sealed record LeadSubmissionEvaluation
{
    public required IReadOnlyDictionary<string, string[]> Errors { get; init; }
    public LeadContact? Contact { get; init; }
    public IReadOnlyList<LeadAnswer> Answers { get; init; } = [];
    public LeadAcquisition Acquisition { get; init; } = new();
    public IReadOnlyDictionary<string, int> Scores { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<LeadResult> Results { get; init; } = [];

    public bool IsValid => Errors.Count == 0;
}
