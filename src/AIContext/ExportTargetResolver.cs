using System.Text.Json;

namespace AIContext;

public sealed record ExportTargetResolutionResult(
    List<string> Targets,
    string? Error);

public static class ExportTargetResolver
{
    // Returns exports.defaultTarget when it is set to a string; callers validate the value.
    public static string? ResolveDefaultTarget(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("exports", out var exports) &&
            exports.ValueKind == JsonValueKind.Object &&
            exports.TryGetProperty("defaultTarget", out var defaultTarget) &&
            defaultTarget.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(defaultTarget.GetString()))
        {
            return defaultTarget.GetString();
        }

        return null;
    }

    public static ExportTargetResolutionResult ResolveTargetsForAll(
        JsonElement root,
        IReadOnlyList<string> supportedTargets,
        ISet<string> allowedTargets)
    {
        if (!root.TryGetProperty("exports", out var exports) || exports.ValueKind != JsonValueKind.Object)
        {
            return new ExportTargetResolutionResult(new List<string>(supportedTargets), null);
        }

        if (!exports.TryGetProperty("targets", out var targetsElement))
        {
            return new ExportTargetResolutionResult(new List<string>(supportedTargets), null);
        }

        if (targetsElement.ValueKind != JsonValueKind.Array)
        {
            return new ExportTargetResolutionResult([], "exports.targets must be an array.");
        }

        var configuredTargets = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in targetsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return new ExportTargetResolutionResult([], "exports.targets must contain only strings.");
            }

            var target = item.GetString() ?? string.Empty;
            if (!allowedTargets.Contains(target))
            {
                return new ExportTargetResolutionResult([], $"exports.targets contains unsupported target '{target}'.");
            }

            if (!seen.Add(target))
            {
                return new ExportTargetResolutionResult([], $"exports.targets contains duplicate target '{target}'.");
            }

            configuredTargets.Add(target);
        }

        if (configuredTargets.Count == 0)
        {
            return new ExportTargetResolutionResult(new List<string>(supportedTargets), null);
        }

        return new ExportTargetResolutionResult(configuredTargets, null);
    }
}
