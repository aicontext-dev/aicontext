using AIContext;
using Xunit;

namespace AIContext.Tests;

// The source hash is part of every generated header, and CI compares it (`export --check`). Sources with LF endings
// have to keep their hash when line-ending handling changes; only the hash of sources with CR endings may change
// (SPEC 6.2). The expected value was computed before the shared line-ending helper existed (step 10) and is not to be
// updated to make a test pass: a different value means every project's generated files change.
public sealed class SourceHashContractTests
{
    private const string LfHash = "d9db6eddddad72c1";

    private static ExportContext LfContext() => new(
        "sample-project",
        "# Project: sample-project\n\nShort summary.\n\n**Tech stack:** .NET\n",
        "Layers.\n\n- Core\n- CLI\n",
        "### D1. First\n**Decision:** One.\n",
        null,
        "- Never do X.\n",
        "Term: meaning.\n",
        [
            new RuleDocument("ui", "UI conventions", ["src/ui/**", "**/*.tsx"], "Use shared components.\n\nSecond paragraph."),
            new RuleDocument("api", "API design", [], "Return JSON envelopes."),
            new RuleDocument("style", null, [], "Format code.")
        ]);

    [Fact]
    public void ComputeSourceHash_SourcesWithLfEndings_KeepTheirHash()
    {
        Assert.Equal(LfHash, ContextExporter.ComputeSourceHash(LfContext()));
    }
}
