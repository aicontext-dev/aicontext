using System.Text.Json;
using Json.Schema;

namespace AIContext;

// Keyword is the JSON Schema keyword that failed (for example "pattern"); empty for an unknown property.
public sealed record SchemaIssue(string Location, string Message, string Keyword = "");

public static class ContextSchema
{
    public const string CanonicalUrl = "https://aicontext.dev/schema/v1/context.schema.json";

    private const string ResourceName = "context.schema.json";

    private static readonly Lazy<JsonSchema> Schema = new(Load);

    public static string ReadText()
    {
        using var stream = typeof(ContextSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<SchemaIssue> Validate(JsonElement instance)
    {
        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        };

        var results = Schema.Value.Evaluate(instance, options);
        var issues = new List<SchemaIssue>();
        if (results.IsValid)
        {
            return issues;
        }

        // Container keywords only repeat failures that are reported again for the exact child location.
        var aggregateKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "properties", "additionalProperties", "items", "patternProperties", "allOf", "anyOf", "oneOf", "$ref"
        };

        foreach (var detail in results.Details ?? [])
        {
            if (detail.Errors is null)
            {
                continue;
            }

            foreach (var error in detail.Errors)
            {
                if (aggregateKeywords.Contains(error.Key))
                {
                    continue;
                }

                var message = error.Key.Length == 0 ? "unknown property is not allowed." : error.Value;
                issues.Add(new SchemaIssue(detail.InstanceLocation.ToString(), message, error.Key));
            }
        }

        if (issues.Count == 0)
        {
            issues.Add(new SchemaIssue(string.Empty, "does not match the schema."));
        }

        return issues
            .OrderBy(x => x.Location, StringComparer.Ordinal)
            .ThenBy(x => x.Message, StringComparer.Ordinal)
            .ToList();
    }

    private static JsonSchema Load() => JsonSchema.FromText(ReadText());
}
