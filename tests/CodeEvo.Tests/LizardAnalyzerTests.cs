using CodeEvo.Adapters;
using Xunit;

namespace CodeEvo.Tests;

public class LizardAnalyzerTests
{
    [Fact]
    public void ParseCsvOutput_SingleFunction_ReturnsSingleFileResult()
    {
        var dirPath = "/repo";
        var csv = "4,2,12,1,4,\"foo@1-4@/repo/Foo.cs\",\"/repo/Foo.cs\",\"foo\",\"foo( x )\",1,4";
        var result = LizardAnalyzer.ParseCsvOutput(csv, dirPath);

        Assert.Single(result);
        var key = result.Keys.First();
        Assert.Contains("Foo.cs", key);
        Assert.Equal(2.0, result[key].AvgCyclomaticComplexity);
        Assert.Equal(0, result[key].SmellsHigh);
        Assert.Equal(0, result[key].SmellsMedium);
        Assert.Equal(0, result[key].SmellsLow);
    }

    [Fact]
    public void ParseCsvOutput_MultipleFunctionsSameFile_AveragesCcn()
    {
        var dirPath = "/repo";
        var csv = string.Join("\n",
            "5,3,20,0,5,\"A@1-5@/repo/Foo.cs\",\"/repo/Foo.cs\",\"A\",\"A()\",1,5",
            "5,7,20,0,5,\"B@6-10@/repo/Foo.cs\",\"/repo/Foo.cs\",\"B\",\"B()\",6,10");
        var result = LizardAnalyzer.ParseCsvOutput(csv, dirPath);

        Assert.Single(result);
        var entry = result.Values.First();
        Assert.Equal(5.0, entry.AvgCyclomaticComplexity);
    }

    [Fact]
    public void ParseCsvOutput_MultipleFiles_ReturnsEntryPerFile()
    {
        var dirPath = "/repo";
        var csv = string.Join("\n",
            "4,2,12,1,4,\"A@1-4@/repo/Foo.cs\",\"/repo/Foo.cs\",\"A\",\"A()\",1,4",
            "4,4,12,1,4,\"B@1-4@/repo/Bar.cs\",\"/repo/Bar.cs\",\"B\",\"B()\",1,4");
        var result = LizardAnalyzer.ParseCsvOutput(csv, dirPath);

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(21, 1, 0, 0)]
    [InlineData(18, 0, 1, 0)]
    [InlineData(12, 0, 0, 1)]
    [InlineData(5,  0, 0, 0)]
    public void ParseCsvOutput_SmellThresholds_ClassifiedCorrectly(
        double ccn, int expectedHigh, int expectedMedium, int expectedLow)
    {
        var dirPath = "/repo";
        var csv = $"5,{ccn},20,0,5,\"A@1-5@/repo/Foo.cs\",\"/repo/Foo.cs\",\"A\",\"A()\",1,5";
        var result = LizardAnalyzer.ParseCsvOutput(csv, dirPath);

        var entry = result.Values.First();
        Assert.Equal(expectedHigh, entry.SmellsHigh);
        Assert.Equal(expectedMedium, entry.SmellsMedium);
        Assert.Equal(expectedLow, entry.SmellsLow);
    }

    [Fact]
    public void ParseCsvOutput_EmptyOutput_ReturnsEmptyDictionary()
    {
        var result = LizardAnalyzer.ParseCsvOutput("", "/repo");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseCsvOutput_MalformedLines_AreSkipped()
    {
        var dirPath = "/repo";
        var csv = "notanumber,also_bad\n4,2,12,1,4,\"foo@1-4@/repo/Foo.cs\",\"/repo/Foo.cs\",\"foo\",\"foo()\",1,4";
        var result = LizardAnalyzer.ParseCsvOutput(csv, dirPath);

        Assert.Single(result);
    }
}
