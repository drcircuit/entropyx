namespace CodeEvo.Core.Models;

/// <summary>
/// Classifies a source file by its intended role in the repository.
/// </summary>
public enum CodeKind
{
    /// <summary>
    /// Regular application/library source files owned and maintained by the team.
    /// This is the default classification for all tracked source files.
    /// </summary>
    Production,

    /// <summary>
    /// Build scripts, tooling, CI configuration, and other developer-infrastructure files.
    /// Files are tagged as Utility by matching patterns in the <c>.utilityfiles</c> config file.
    /// </summary>
    Utility
}
