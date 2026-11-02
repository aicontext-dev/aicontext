using System.Text.Json;

namespace AIContext;

public enum ExportLayout
{
    // One file per tool containing everything (default).
    Single,
    // Always-on content in one small file; scoped and on-demand content in separate native rule files.
    Split
}

public static class ExportLayoutResolver
{
    // exports.nestedAgents: also write per-directory AGENTS.md for directory-scoped rules (split layout only).
    public static bool ResolveNestedAgents(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("exports", out var exports) &&
        exports.ValueKind == JsonValueKind.Object &&
        exports.TryGetProperty("nestedAgents", out var nested) &&
        nested.ValueKind == JsonValueKind.True;

    public static bool TryParse(string? value, out ExportLayout layout)
    {
        switch (value)
        {
            case "single":
                layout = ExportLayout.Single;
                return true;
            case "split":
                layout = ExportLayout.Split;
                return true;
            default:
                layout = ExportLayout.Single;
                return false;
        }
    }

    // A command-line override wins over exports.layout; the default is single.
    public static (ExportLayout Layout, string? Error) Resolve(JsonElement root, string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return TryParse(overrideValue, out var overridden)
                ? (overridden, null)
                : (ExportLayout.Single, $"Unsupported layout '{overrideValue}'. Supported layouts: single, split.");
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("exports", out var exports) &&
            exports.ValueKind == JsonValueKind.Object &&
            exports.TryGetProperty("layout", out var layoutElement))
        {
            return layoutElement.ValueKind == JsonValueKind.String && TryParse(layoutElement.GetString(), out var configured)
                ? (configured, null)
                : (ExportLayout.Single, "exports.layout must be 'single' or 'split'.");
        }

        return (ExportLayout.Single, null);
    }
}
