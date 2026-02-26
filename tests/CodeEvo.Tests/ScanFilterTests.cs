using CodeEvo.Core;
using Xunit;

namespace CodeEvo.Tests;

public class ScanFilterTests
{
    // ── IsPathIgnored ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("node_modules/lodash/index.js")]
    [InlineData("bin/Release/net10.0/app.dll")]
    [InlineData("obj/Debug/Foo.cs")]
    [InlineData("dist/bundle.js")]
    [InlineData("build/output.js")]
    [InlineData("target/release/app")]
    [InlineData(".git/COMMIT_EDITMSG")]
    [InlineData(".vs/solution.suo")]
    [InlineData("src/node_modules/foo.js")]        // nested ignored dir
    [InlineData("deep/sub/bin/app.exe")]           // ignored dir deep in tree
    public void IsPathIgnored_FileInIgnoredDirectory_ReturnsTrue(string relativeSubPath)
    {
        var root = Path.GetTempPath();
        var fullPath = Path.Combine(root, relativeSubPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(ScanFilter.IsPathIgnored(fullPath, root));
    }

    [Theory]
    [InlineData("src/Foo.cs")]
    [InlineData("README.md")]
    [InlineData("index.ts")]
    [InlineData("lib/utils.py")]
    [InlineData("binaries/data.bin")]   // "binaries" is NOT in the ignore list
    [InlineData("builder/main.go")]     // "builder" is NOT in the ignore list
    public void IsPathIgnored_FileNotInIgnoredDirectory_ReturnsFalse(string relativeSubPath)
    {
        var root = Path.GetTempPath();
        var fullPath = Path.Combine(root, relativeSubPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.False(ScanFilter.IsPathIgnored(fullPath, root));
    }

    [Fact]
    public void IsPathIgnored_FileDirectlyInRoot_ReturnsFalse()
    {
        var root = Path.GetTempPath();
        var fullPath = Path.Combine(root, "Program.cs");
        Assert.False(ScanFilter.IsPathIgnored(fullPath, root));
    }

    // ── MatchesFilter ────────────────────────────────────────────────────────────

    [Fact]
    public void MatchesFilter_NullPatterns_ReturnsTrue()
        => Assert.True(ScanFilter.MatchesFilter("Foo.cs", null));

    [Fact]
    public void MatchesFilter_EmptyPatterns_ReturnsTrue()
        => Assert.True(ScanFilter.MatchesFilter("Foo.cs", []));

    [Theory]
    [InlineData("Foo.cs", "*.cs")]
    [InlineData("index.ts", "*.ts")]
    [InlineData("Component.tsx", "*.tsx")]
    [InlineData("main.cpp", "*.cpp")]
    public void MatchesFilter_MatchingExtensionPattern_ReturnsTrue(string fileName, string pattern)
        => Assert.True(ScanFilter.MatchesFilter(fileName, [pattern]));

    [Theory]
    [InlineData("Foo.cs", "*.ts")]
    [InlineData("index.js", "*.ts")]
    [InlineData("README.md", "*.cs")]
    public void MatchesFilter_NonMatchingExtensionPattern_ReturnsFalse(string fileName, string pattern)
        => Assert.False(ScanFilter.MatchesFilter(fileName, [pattern]));

    [Fact]
    public void MatchesFilter_MultiplePatterns_MatchesAny()
        => Assert.True(ScanFilter.MatchesFilter("index.ts", ["*.cs", "*.ts"]));

    [Fact]
    public void MatchesFilter_WildcardStar_MatchesAll()
        => Assert.True(ScanFilter.MatchesFilter("anything.xyz", ["*"]));

    [Theory]
    [InlineData("Makefile", "Makefile")]
    [InlineData("makefile", "Makefile")]    // case-insensitive
    [InlineData("MAKEFILE", "Makefile")]
    public void MatchesFilter_ExactNamePattern_MatchesCaseInsensitive(string fileName, string pattern)
        => Assert.True(ScanFilter.MatchesFilter(fileName, [pattern]));

    [Fact]
    public void MatchesFilter_ExactNamePattern_DoesNotMatchDifferentFile()
        => Assert.False(ScanFilter.MatchesFilter("Foo.cs", ["Makefile"]));

    // ── MatchGlob (internal, tested via reflection-style coverage) ──────────────

    [Theory]
    [InlineData("Test.cs", "Test*", true)]     // trailing wildcard
    [InlineData("Testing.cs", "Test*", true)]
    [InlineData("Other.cs", "Test*", false)]
    public void MatchesFilter_TrailingWildcard_Works(string fileName, string pattern, bool expected)
        => Assert.Equal(expected, ScanFilter.MatchesFilter(fileName, [pattern]));

    // ── LoadExIgnorePatterns ─────────────────────────────────────────────────────

    [Fact]
    public void LoadExIgnorePatterns_MissingFile_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try { Assert.Empty(ScanFilter.LoadExIgnorePatterns(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadExIgnorePatterns_ReadsPatterns_SkipsCommentsAndBlankLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, ".exignore"), [
                "# this is a comment",
                "*.log",
                "",
                "generated",
                "  # indented comment  ",
                "*.min.js",
            ]);
            var patterns = ScanFilter.LoadExIgnorePatterns(dir);
            Assert.Equal(new string[] { "*.log", "generated", "*.min.js" }, patterns);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── IsExIgnored ──────────────────────────────────────────────────────────────

    [Fact]
    public void IsExIgnored_EmptyPatterns_ReturnsFalse()
    {
        var root = Path.GetTempPath();
        Assert.False(ScanFilter.IsExIgnored(Path.Combine(root, "src", "Foo.cs"), root, []));
    }

    [Theory]
    [InlineData("src/Foo.log", "*.log")]          // filename extension match
    [InlineData("generated/Foo.cs", "generated")] // directory segment match
    [InlineData("src/generated/Bar.cs", "generated")] // nested directory segment
    [InlineData("src/Foo.min.js", "*.min.js")]    // multi-dot extension match
    [InlineData("editor_assets/shaderlab.ico.ico", ".ico")]   // extension-only pattern
    [InlineData("editor_assets/fonts/Hack-Regular.ttf", ".ttf")] // extension-only pattern
    [InlineData("CMakeLists.txt", ".txt")]         // extension-only pattern
    [InlineData("third_party/Crinkler.exe", "third_party")] // directory prefix pattern
    [InlineData(".gitignore", ".gitignore")]       // dot-file: suffix match (.gitignore ends with .gitignore)
    public void IsExIgnored_MatchingPattern_ReturnsTrue(string relPath, string pattern)
    {
        var root = Path.GetTempPath();
        var fullPath = Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(ScanFilter.IsExIgnored(fullPath, root, [pattern]));
    }

    [Theory]
    [InlineData("src/Foo.cs", "*.log")]
    [InlineData("src/Foo.cs", "generated")]
    public void IsExIgnored_NonMatchingPattern_ReturnsFalse(string relPath, string pattern)
    {
        var root = Path.GetTempPath();
        var fullPath = Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.False(ScanFilter.IsExIgnored(fullPath, root, [pattern]));
    }

    [Fact]
    public void IsExIgnored_RelativePath_WorksWithoutRoot()
    {
        // Files from git traversal arrive as relative paths
        var root = "/repo";
        Assert.True(ScanFilter.IsExIgnored("src/generated/Foo.cs", root, ["generated"]));
    }

    // ── LoadUtilityPatterns ──────────────────────────────────────────────────────

    [Fact]
    public void LoadUtilityPatterns_MissingFile_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try { Assert.Empty(ScanFilter.LoadUtilityPatterns(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadUtilityPatterns_ReadsPatterns_SkipsCommentsAndBlankLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, ".utilityfiles"), [
                "# build scripts",
                "*.sh",
                "",
                "scripts",
                "  # another comment  ",
                "*.ps1",
            ]);
            var patterns = ScanFilter.LoadUtilityPatterns(dir);
            Assert.Equal(new string[] { "*.sh", "scripts", "*.ps1" }, patterns);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── ClassifyCodeKind ─────────────────────────────────────────────────────────

    [Fact]
    public void ClassifyCodeKind_EmptyPatterns_ReturnsProduction()
    {
        var root = Path.GetTempPath();
        var kind = ScanFilter.ClassifyCodeKind("src/Foo.cs", root, []);
        Assert.Equal(CodeEvo.Core.Models.CodeKind.Production, kind);
    }

    [Theory]
    [InlineData("scripts/deploy.sh", "*.sh")]
    [InlineData("tools/build.py", "tools")]
    [InlineData("scripts/ci.ps1", "scripts")]
    public void ClassifyCodeKind_MatchingUtilityPattern_ReturnsUtility(string relPath, string pattern)
    {
        var root = Path.GetTempPath();
        var kind = ScanFilter.ClassifyCodeKind(relPath, root, [pattern]);
        Assert.Equal(CodeEvo.Core.Models.CodeKind.Utility, kind);
    }

    [Theory]
    [InlineData("src/Foo.cs", "*.sh")]
    [InlineData("src/Bar.py", "tools")]
    public void ClassifyCodeKind_NonMatchingPattern_ReturnsProduction(string relPath, string pattern)
    {
        var root = Path.GetTempPath();
        var kind = ScanFilter.ClassifyCodeKind(relPath, root, [pattern]);
        Assert.Equal(CodeEvo.Core.Models.CodeKind.Production, kind);
    }
}
