namespace CodeEvo.Core;

public static class CouplingCounter
{
    /// <summary>
    /// Counts import/dependency directives in the given lines as a proxy for efferent coupling.
    /// </summary>
    public static int Count(string[] lines, string language)
    {
        int count = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (IsDependencyDirective(line, language))
                count++;
        }
        return count;
    }

    private static bool IsDependencyDirective(string line, string language) =>
        language switch
        {
            "CSharp"     => IsCSharpUsingDirective(line),
            "Java"       => line.StartsWith("import ") && line.EndsWith(";"),
            "TypeScript" or "JavaScript" => line.StartsWith("import ") || line.Contains("require('") || line.Contains("require(\""),
            "Python"     => line.StartsWith("import ") || line.StartsWith("from "),
            "C" or "Cpp" => line.StartsWith("#include"),
            "Rust"       => line.StartsWith("use ") || line.StartsWith("extern crate "),
            _            => false
        };

    /// <summary>
    /// Returns true only for C# using directives (namespace imports), not using statements or declarations.
    /// Handles: "using Ns;", "using static Ns;", "using Alias = Ns;"
    /// Rejects: "using var x = ...", "using (", "using TypeName varName = ..."
    /// </summary>
    private static bool IsCSharpUsingDirective(string line)
    {
        if (!line.StartsWith("using ") || !line.EndsWith(";")) return false;

        var inner = line["using ".Length..^1].Trim();

        if (inner.StartsWith("("))   return false; // using statement
        if (inner.StartsWith("var ")) return false; // using var declaration

        // Strip optional "static " prefix
        if (inner.StartsWith("static ")) inner = inner["static ".Length..];

        // If there is a space in the remainder it must be an alias directive: "Alias = Namespace"
        // Using declarations look like "TypeName varName = ..." where the part after the first
        // space does NOT immediately start with '='.
        var spaceIdx = inner.IndexOf(' ');
        if (spaceIdx >= 0)
        {
            var afterSpace = inner[(spaceIdx + 1)..];
            if (!afterSpace.StartsWith("=")) return false; // "TypeName varName = ..." — declaration
        }

        return true;
    }
}
