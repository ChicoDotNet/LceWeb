using LceWeb.Api.Leads;

namespace LceWeb.Api.Diagnostics;

public static class CommercialDiagnostic
{
    public static DiagnosticDefinition PublicDefinition(DiagnosticDefinition definition) => definition with
    {
        Dimensions = [],
        Results = [],
        Steps = definition.Steps.Select(step => step with
        {
            Questions = step.Questions.Select(question => question with
            {
                Options = question.Options.Select(option => option with
                {
                    Scores = new Dictionary<string, int>()
                }).ToArray()
            }).ToArray()
        }).ToArray()
    };

    public static IReadOnlyList<string> InterestTags(IReadOnlyList<LeadAnswer> answers, LeadAcquisition acquisition)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var option in answers.Where(a => a.QuestionId == "priorities").SelectMany(a => a.AnswerIds))
        {
            var tag = option switch { "trust" => "proveedores", "cost" => "costos", "time" => "logistica", "coordination" => "coordinacion", "compliance" => "compliance", _ => null };
            if (tag is not null) tags.Add(tag);
        }
        foreach (var source in new[] { acquisition.PageUrl, acquisition.Referrer })
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) continue;
            var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var tag = path switch
            {
                "/l/costos-importacion.html" => "costos",
                "/l/cumplimiento-nom.html" => "nom",
                "/l/diagnostico-logistico.html" => "logistica",
                "/l/manifestacion-de-valor.html" => "mve",
                "/l/proveedores-confiables.html" => "proveedores",
                "/l/compliance-aduanero.html" => "compliance",
                "/l/materialidad-aduanera.html" => "materialidad",
                "/l/importacion-tecnologia-industrial.html" => "tecnologia",
                _ => null
            };
            if (tag is not null) tags.Add(tag);
        }
        return tags.ToArray();
    }
}
