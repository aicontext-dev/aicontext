namespace AIContext;

// The one definition of a line break for everything that reads, hashes or writes text: LF, CRLF and a lone CR are the
// same break, and nothing else is (U+0085, U+2028 and U+2029 are ordinary characters in a Markdown file). Text is
// normalized once, before it is parsed, hashed, scanned or written, so the parsed text, the source hash, the line
// numbers of findings and the exported files are the same whichever line endings a source file has (SPEC 6.2).
internal static class LineEndings
{
    public static string Normalize(string value) =>
        value.Contains('\r', StringComparison.Ordinal)
            ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : value;
}
