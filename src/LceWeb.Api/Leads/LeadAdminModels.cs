namespace LceWeb.Api.Leads;

public sealed record LeadAdminRow(Guid Id, DateTimeOffset CreatedUtc, string Name, int? Score, string Language, string Campaign, string Status, string EmailStatus, string Interests);
public sealed record LeadAdminPage(IReadOnlyList<LeadAdminRow> Rows, string? Continuation);
public sealed record LeadAdminDetail(LeadSubmission Submission, string Status, string EmailStatus, string Revision, string? LastUpdatedBy, DateTimeOffset? LastUpdatedUtc);
public static class LeadFollowUp
{
    public static readonly string[] States = ["new", "contacted", "appointment", "proposal", "won", "lost"];
    public static bool IsValid(string value) => States.Contains(value, StringComparer.Ordinal);
}

public sealed record LeadAdminFilter(int? MinimumScore, string? Interest, string? Product, string? Language)
{
    public static readonly string[] Interests = ["proveedores", "costos", "logistica", "coordinacion", "compliance", "nom", "mve", "materialidad", "tecnologia"];
    public static readonly string[] Products = ["technology", "machinery", "telecom", "furniture", "skincare", "spares", "electronics", "personal-care", "electrical", "industrial-plastic", "plastic", "appliances", "other"];
    public static readonly string[] Languages = ["es", "en", "fr", "pt"];
    public bool IsValid => MinimumScore is null or >= 0 and <= 17 &&
        (string.IsNullOrEmpty(Interest) || Interests.Contains(Interest, StringComparer.Ordinal)) &&
        (string.IsNullOrEmpty(Product) || Products.Contains(Product, StringComparer.Ordinal)) &&
        (string.IsNullOrEmpty(Language) || Languages.Contains(Language, StringComparer.Ordinal));
    public static string InterestField(string value) => "Interest_" + value.Replace("-", "_");
    public static string ProductField(string value) => "Product_" + value.Replace("-", "_");
}
