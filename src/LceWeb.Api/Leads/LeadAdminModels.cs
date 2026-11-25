namespace LceWeb.Api.Leads;

public sealed record LeadAdminRow(Guid Id, DateTimeOffset CreatedUtc, string Name, int? Score, string Language, string Campaign, string Status, string EmailStatus, string Interests);
public sealed record LeadAdminPage(IReadOnlyList<LeadAdminRow> Rows, string? Continuation);
public sealed record LeadAdminDetail(LeadSubmission Submission, string Status, string EmailStatus, string Revision, string? LastUpdatedBy, DateTimeOffset? LastUpdatedUtc);
public static class LeadFollowUp
{
    public static readonly string[] States = ["new", "contacted", "appointment", "proposal", "won", "lost"];
    public static bool IsValid(string value) => States.Contains(value, StringComparer.Ordinal);
}
