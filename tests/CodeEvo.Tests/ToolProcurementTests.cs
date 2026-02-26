using CodeEvo.Adapters;
using Xunit;

namespace CodeEvo.Tests;

public class ToolProcurementTests
{
    [Theory]
    [InlineData("CSharp")]
    [InlineData("Java")]
    [InlineData("Python")]
    [InlineData("TypeScript")]
    [InlineData("JavaScript")]
    [InlineData("C")]
    [InlineData("Cpp")]
    [InlineData("Rust")]
    public void GetRequiredToolsForLanguage_KnownLanguage_IncludesGitClocAndLizard(string language)
    {
        var procurement = new ToolProcurement();
        var tools = procurement.GetRequiredToolsForLanguage(language);
        Assert.Contains("git", tools);
        Assert.Contains("cloc", tools);
        Assert.Contains("lizard", tools);
    }

    [Fact]
    public void GetRequiredToolsForLanguage_UnknownLanguage_IncludesGitAndClocButNotLizard()
    {
        var procurement = new ToolProcurement();
        var tools = procurement.GetRequiredToolsForLanguage("unknown");
        Assert.Contains("git", tools);
        Assert.Contains("cloc", tools);
        Assert.DoesNotContain("lizard", tools);
    }

    [Theory]
    [InlineData("lizard", "linux")]
    [InlineData("lizard", "macos")]
    [InlineData("lizard", "windows")]
    public void GetInstallInstructions_LizardTool_ReturnsPipInstallCommand(string tool, string platform)
    {
        var procurement = new ToolProcurement();
        var result = procurement.GetInstallInstructions(tool, platform);
        Assert.Equal("pip install lizard", result);
    }

    [Theory]
    [InlineData("git", "linux", "sudo apt-get install git")]
    [InlineData("git", "macos", "brew install git")]
    [InlineData("git", "windows", "winget install --id Git.Git")]
    [InlineData("cloc", "linux", "sudo apt-get install cloc")]
    [InlineData("cloc", "macos", "brew install cloc")]
    [InlineData("cloc", "windows", "winget install cloc")]
    public void GetInstallInstructions_KnownTools_ReturnCorrectInstructions(string tool, string platform, string expected)
    {
        var procurement = new ToolProcurement();
        var result = procurement.GetInstallInstructions(tool, platform);
        Assert.Equal(expected, result);
    }
}
