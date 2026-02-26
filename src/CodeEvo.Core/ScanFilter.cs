namespace CodeEvo.Core;

public static class ScanFilter
{
    /// <summary>
    /// Directory names that are always excluded from scans (packages, build outputs, VCS metadata, etc.).
    /// </summary>
    public static readonly IReadOnlySet<string> DefaultIgnoredDirectories =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // VCS metadata
            ".git", ".hg", ".svn",
            // Package managers
            "node_modules", "vendor", "packages", ".nuget", "Pods",
            // Build outputs
            "bin", "obj", "out", "dist", "build", "target",
            // Language-specific caches / build dirs
            "__pycache__", ".gradle", ".m2",
            // IDE artifacts
            ".vs", ".idea", ".vscode",
            // Test/coverage outputs
            "coverage", ".nyc_output",
            // Framework outputs
            ".next", "DerivedData", ".dart_tool", ".pub-cache",
        };

    /// <summary>Returns true when any ancestor directory segment of <paramref name="filePath"/>
    /// (relative to <paramref name="rootPath"/>) is in <see cref="DefaultIgnoredDirectories"/>.</summary>
    public static bool IsPathIgnored(string filePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath);
        var parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        // All parts except the last (file name) are directory segments.
        return parts.Length > 1 && parts[..^1].Any(p => DefaultIgnoredDirectories.Contains(p));
    }

    /// <summary>Returns true when <paramref name="includePatterns"/> is null/empty,
    /// or the file name matches at least one glob pattern.</summary>
    public static bool MatchesFilter(string filePath, string[]? includePatterns)
    {
        if (includePatterns is null || includePatterns.Length == 0)
            return true;
        var fileName = Path.GetFileName(filePath);
        return includePatterns.Any(pattern => MatchGlob(fileName, pattern));
    }

    /// <summary>Simple glob match supporting leading-wildcard (*.ext), trailing-wildcard (prefix*),
    /// full-wildcard (*), and exact (case-insensitive) comparisons.</summary>
    private static bool MatchGlob(string fileName, string pattern)
    {
        if (pattern == "*") return true;

        // *.ext  or  *suffix
        if (pattern.StartsWith('*') && !pattern[1..].Contains('*'))
            return fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);

        // prefix*
        if (pattern.EndsWith('*') && !pattern[..^1].Contains('*'))
            return fileName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);

        // exact name match
        return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
