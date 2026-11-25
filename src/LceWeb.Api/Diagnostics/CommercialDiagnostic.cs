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

    public static IReadOnlyList<string> FollowUpSuggestions(IReadOnlyList<string> interests)
    {
        var suggestions = new List<string>();
        foreach (var tag in interests)
        {
            var suggestion = tag switch
            {
                "proveedores" => "Explorar proveedores, condiciones de compra y alcance de la validación requerida.",
                "costos" => "Revisar componentes del costo integral y alternativas de planeación.",
                "logistica" => "Identificar fechas, rutas y obstáculos de coordinación.",
                "coordinacion" => "Identificar responsables y participantes de la operación.",
                "compliance" => "Explorar requisitos, información disponible y riesgos percibidos.",
                "nom" => "Confirmar producto, uso y documentación técnica; canalizar la revisión especializada.",
                "mve" => "Explorar datos y documentación de valoración; confirmar alcance con el especialista.",
                "materialidad" => "Identificar disponibilidad y congruencia de documentos operativos.",
                "tecnologia" => "Confirmar equipo, uso, componentes y mercados de origen y destino.",
                _ => null
            };
            if (suggestion is not null) suggestions.Add(suggestion);
        }
        if (suggestions.Count == 0) suggestions.Add("Confirmar necesidades, etapa y expectativas durante la primera llamada.");
        return suggestions;
    }

    public static IReadOnlyList<string> InterestTags(IReadOnlyList<LeadAnswer> answers, LeadAcquisition acquisition)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var option in answers.Where(a => a.QuestionId == "priorities").SelectMany(a => a.AnswerIds))
        {
            var tag = option switch { "trust" => "proveedores", "cost" => "costos", "time" => "logistica", "coordination" => "coordinacion", "compliance" => "compliance", _ => null };
            if (tag is not null) tags.Add(tag);
        }
        var campaignTag = acquisition.UtmCampaign?.Trim().ToLowerInvariant() switch
        {
            "mve-2027" => "mve",
            "proveedores-2027" => "proveedores",
            "compliance-2027" => "compliance",
            "hidden-leverage-costos" => "costos",
            "proof-first-nom" => "nom",
            "diagnostic-first-logistica" => "logistica",
            _ => null
        };
        if (campaignTag is not null) tags.Add(campaignTag);
        foreach (var source in new[] { acquisition.PageUrl, acquisition.Referrer })
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) continue;
            var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            if (path.StartsWith("/en/", StringComparison.Ordinal) || path.StartsWith("/fr/", StringComparison.Ordinal) || path.StartsWith("/pt/", StringComparison.Ordinal)) path = path[3..];
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
