namespace CodeEvo.Core.Models;

public record FileMetrics(
    string CommitHash,
    string Path,
    string Language,
    int Sloc,
    double CyclomaticComplexity,
    double MaintainabilityIndex,
    int SmellsHigh,
    int SmellsMedium,
    int SmellsLow,
    double CouplingProxy,
    double MaintainabilityProxy);
