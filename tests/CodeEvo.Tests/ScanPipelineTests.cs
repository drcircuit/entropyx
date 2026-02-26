using CodeEvo.Core;
using Xunit;

namespace CodeEvo.Tests;

public class ScanPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public ScanPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private void WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void ScanDirectory_EmptyDirectory_ReturnsEmptyList()
    {
        var pipeline = new ScanPipeline();
        var result = pipeline.ScanDirectory(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public void ScanDirectory_SingleCSharpFile_DetectsLanguageAndCountsSloc()
    {
        WriteFile("Foo.cs", "// comment\npublic class Foo {\n    int x = 1;\n}\n");
        var pipeline = new ScanPipeline();
        var result = pipeline.ScanDirectory(_tempDir);

        Assert.Single(result);
        Assert.Equal("CSharp", result[0].Language);
        Assert.Equal(3, result[0].Sloc); // class line + int line + closing brace; comment excluded
        Assert.Equal("Foo.cs", result[0].Path);
        Assert.Equal(string.Empty, result[0].CommitHash);
    }

    [Fact]
    public void ScanDirectory_MultipleFiles_AllIncluded()
    {
        WriteFile("main.py", "x = 1\ny = 2\n");
        WriteFile("lib.rs", "fn foo() {}\n");
        WriteFile("index.ts", "const x = 1;\n");
        var pipeline = new ScanPipeline();
        var result = pipeline.ScanDirectory(_tempDir);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, f => f.Language == "Python" && f.Sloc == 2);
        Assert.Contains(result, f => f.Language == "Rust" && f.Sloc == 1);
        Assert.Contains(result, f => f.Language == "TypeScript" && f.Sloc == 1);
    }

    [Fact]
    public void ScanDirectory_UnknownExtension_IncludedWithEmptyLanguage()
    {
        WriteFile("README.md", "# Hello\nsome text\n");
        var pipeline = new ScanPipeline();
        var result = pipeline.ScanDirectory(_tempDir);

        Assert.Single(result);
        Assert.Equal(string.Empty, result[0].Language);
    }

    [Fact]
    public void ScanDirectory_NestedFiles_AllFound()
    {
        WriteFile("src/a.cs", "int a = 1;\n");
        WriteFile("src/sub/b.cs", "int b = 2;\n");
        var pipeline = new ScanPipeline();
        var result = pipeline.ScanDirectory(_tempDir);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Equal("CSharp", f.Language));
    }

    [Fact]
    public void ScanDirectory_RelativePaths_AreRelativeToRoot()
    {
        WriteFile("sub/foo.cs", "int x = 1;\n");
        var pipeline = new ScanPipeline();
        var result = pipeline.ScanDirectory(_tempDir);

        // Path should be relative, not absolute
        Assert.DoesNotContain(result, f => Path.IsPathRooted(f.Path));
        Assert.Contains(result, f => f.Path.Contains("foo.cs"));
    }
}
