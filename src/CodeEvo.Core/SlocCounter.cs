namespace CodeEvo.Core;

public static class SlocCounter
{
    public static int CountSloc(string[] lines, string language)
    {
        bool inBlockComment = false;
        int count = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (inBlockComment)
            {
                if (line.Contains("*/"))
                    inBlockComment = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Block comment start
            if (UsesCStyleComments(language) && line.StartsWith("/*"))
            {
                if (!line.Contains("*/"))
                    inBlockComment = true;
                continue;
            }

            // Single-line comment
            if (UsesCStyleComments(language) && line.StartsWith("//"))
                continue;

            if (language == "Python" && line.StartsWith("#"))
                continue;

            count++;
        }

        return count;
    }

    private static bool UsesCStyleComments(string language) =>
        language is "C" or "Cpp" or "Java" or "CSharp" or "TypeScript" or "JavaScript" or "Rust";
}
