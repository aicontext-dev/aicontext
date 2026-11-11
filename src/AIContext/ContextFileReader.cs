namespace AIContext;

// Reads of the files in .aicontext/ can fail for ordinary reasons: another program holds a lock, the account has no
// permission. Those failures are reported as diagnostics. Only IOException and UnauthorizedAccessException are
// expected here; anything else is a bug and propagates.
internal static class ContextFileReader
{
    // The text of the file, or null after an error diagnostic (code, location) was added.
    public static string? TryReadAllText(string path, string code, string location, List<Diagnostic> diagnostics)
    {
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
