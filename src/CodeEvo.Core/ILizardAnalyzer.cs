namespace CodeEvo.Core;

public record LizardFileResult(
    double AvgCyclomaticComplexity,
    int SmellsHigh,
    int SmellsMedium,
    int SmellsLow);

public interface ILizardAnalyzer
{
    IReadOnlyDictionary<string, LizardFileResult> AnalyzeDirectory(string dirPath);
}
