namespace AIContext;

// Reads the files that make up the context of a project (everything under .aicontext/). They are read with the
// permissions of whoever runs the tool, and a repository can contain symbolic links, so a source whose real location
// is outside the project is refused before it is opened, the same way export refuses to write there (D12, D19): its
// content would end up in the generated files, which other tools and people then read. Links that stay inside the
// project work. A hard link cannot be told from a regular file and is read like one.
//
// Reads can also fail for ordinary reasons: another program holds a lock, the account has no permission. Those
// failures are reported as diagnostics. Only IOException and UnauthorizedAccessException are expected here; anything
// else is a bug and propagates.
internal sealed class ContextFileReader
{
    private readonly ExportPathGuard _guard;

    public ContextFileReader(string projectRoot)
    {
        _guard = new ExportPathGuard(projectRoot);
    }

    // The root of the project that owns a .aicontext directory.
    public static string ProjectRootOf(string contextDirectory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contextDirectory));
        return Path.GetDirectoryName(full) ?? full;
    }

    // Null when the real location of the path is inside the project; otherwise why it is refused.
    public PathProblem? FindProblem(string path) => _guard.FindSourceProblem(path);

    // Null when the path may be read; otherwise the error to report (location is project-relative, with '/').
    public Diagnostic? Refuse(string path, string location) =>
        FindProblem(path) is { } problem
            ? Diagnostic.Error(
                DiagnosticCodes.SourceOutsideProject,
                $"{path} {problem.Description}. A source of the project context must be inside the project; it was not read.",
                location)
            : null;

    // The text of the file, or null after an error diagnostic (a refused source, or code for a failed read) was added.
    public string? TryReadAllText(string path, string code, string location, List<Diagnostic> diagnostics)
    {
        if (Refuse(path, location) is { } refused)
        {
            diagnostics.Add(refused);
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic.Error(
                code,
                $"{path} could not be read ({ex.Message}). Check that no other program has it locked and that you have permission to read it.",
                location));
            return null;
        }
    }
}
