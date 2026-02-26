namespace CodeEvo.Core;

public static class LanguageDetector
{
    public static string Detect(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".java" => "Java",
            ".cs" => "CSharp",
            ".c" or ".h" => "C",
            ".cpp" or ".cc" or ".cxx" or ".hpp" => "Cpp",
            ".ts" or ".tsx" => "TypeScript",
            ".js" or ".jsx" or ".mjs" or ".cjs" => "JavaScript",
            ".rs" => "Rust",
            ".py" => "Python",
            _ => string.Empty
        };
    }
}
