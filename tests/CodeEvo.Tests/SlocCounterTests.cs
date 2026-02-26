using CodeEvo.Core;
using Xunit;

namespace CodeEvo.Tests;

public class SlocCounterTests
{
    [Fact]
    public void CountSloc_EmptyLines_ReturnsZero()
    {
        var lines = new[] { "", "  ", "\t" };
        Assert.Equal(0, SlocCounter.CountSloc(lines, "CSharp"));
    }

    [Fact]
    public void CountSloc_CodeLines_ReturnsCorrectCount()
    {
        var lines = new[] { "int x = 1;", "int y = 2;", "return x + y;" };
        Assert.Equal(3, SlocCounter.CountSloc(lines, "CSharp"));
    }

    [Fact]
    public void CountSloc_SingleLineComments_AreExcluded()
    {
        var lines = new[] { "// this is a comment", "int x = 1;", "// another comment" };
        Assert.Equal(1, SlocCounter.CountSloc(lines, "CSharp"));
    }

    [Fact]
    public void CountSloc_PythonHashComments_AreExcluded()
    {
        var lines = new[] { "# comment", "x = 1", "# another" };
        Assert.Equal(1, SlocCounter.CountSloc(lines, "Python"));
    }

    [Fact]
    public void CountSloc_BlockComments_AreExcluded()
    {
        var lines = new[]
        {
            "/* start",
            "   still in block",
            "*/",
            "int x = 5;"
        };
        Assert.Equal(1, SlocCounter.CountSloc(lines, "CSharp"));
    }

    [Fact]
    public void CountSloc_MixedContent_ReturnsCorrectCount()
    {
        var lines = new[]
        {
            "// header comment",
            "public class Foo {",
            "    /* block",
            "       comment */",
            "    int x = 1;",
            "",
            "    // inline",
            "    return x;",
            "}"
        };
        Assert.Equal(4, SlocCounter.CountSloc(lines, "CSharp"));
    }

    [Fact]
    public void CountSloc_JavaScriptSingleLineComments_AreExcluded()
    {
        var lines = new[] { "// js comment", "const x = 1;", "// another" };
        Assert.Equal(1, SlocCounter.CountSloc(lines, "JavaScript"));
    }

    [Fact]
    public void CountSloc_RustSingleLineComments_AreExcluded()
    {
        var lines = new[] { "// rust comment", "let x = 1;", "// another" };
        Assert.Equal(1, SlocCounter.CountSloc(lines, "Rust"));
    }
}
