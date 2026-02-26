using CodeEvo.Core.Models;
using LibGit2Sharp;

namespace CodeEvo.Core;

public class GitTraversal
{
    public IEnumerable<CommitInfo> GetAllCommits(string repoPath)
    {
        using var repo = new Repository(repoPath);
        var filter = new CommitFilter { SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            var parents = commit.Parents.Select(p => p.Sha).ToList();
            yield return new CommitInfo(commit.Sha, commit.Author.When, parents);
        }
    }

    public IEnumerable<CommitInfo> GetCheckpointCommits(string repoPath)
    {
        using var repo = new Repository(repoPath);
        var taggedShas = repo.Tags
            .Select(t => t.Target.Sha)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filter = new CommitFilter { SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            bool isTagged = taggedShas.Contains(commit.Sha);
            bool isMerge = commit.Parents.Count() > 1;
            if (isTagged || isMerge)
            {
                var parents = commit.Parents.Select(p => p.Sha).ToList();
                yield return new CommitInfo(commit.Sha, commit.Author.When, parents);
            }
        }
    }

    /// <summary>
    /// Returns every file path that exists in the repository tree at
    /// <paramref name="commitHash"/>, recursively, excluding paths whose
    /// directory segments match <see cref="ScanFilter.DefaultIgnoredDirectories"/>.
    /// </summary>
    public IEnumerable<string> GetAllFilesAtCommit(string repoPath, string commitHash)
    {
        using var repo = new Repository(repoPath);
        var commit = repo.Lookup<Commit>(commitHash);
        if (commit == null)
            yield break;

        foreach (var path in WalkTree(commit.Tree, string.Empty))
            yield return path;
    }

    private static IEnumerable<string> WalkTree(Tree tree, string prefix)
    {
        foreach (var entry in tree)
        {
            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                // Skip ignored directory segments
                if (ScanFilter.DefaultIgnoredDirectories.Contains(entry.Name))
                    continue;
                // Git tree paths always use '/' as the separator regardless of OS.
                // Using '/' explicitly (not Path.Combine) preserves git-convention paths
                // that LibGit2Sharp's Tree indexer requires for blob lookups.
                var subPrefix = prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
                foreach (var child in WalkTree((Tree)entry.Target, subPrefix))
                    yield return child;
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                yield return prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
            }
        }
    }

    public static bool IsValidRepo(string path)
    {
        try
        {
            var discovered = Repository.Discover(path);
            return discovered is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the human-readable repo name and the origin remote URL (empty string if none).
    /// The name is derived from the remote URL when available, otherwise from the directory name.
    /// </summary>
    public static (string Name, string RemoteUrl) GetRepoInfo(string path)
    {
        using var repo = new Repository(path);
        var remoteUrl = repo.Network.Remotes["origin"]?.Url ?? string.Empty;
        string name;
        if (remoteUrl.Length > 0)
        {
            // Normalise both SSH (git@host:owner/repo.git) and HTTPS (https://host/owner/repo.git) URLs
            // to extract the "owner/repo" segment:
            //   1. Strip common scheme/user prefixes.
            //   2. If a colon appears before the first slash (SSH syntax), strip up to the colon.
            //      Otherwise, strip up to and including the first slash (HTTPS host part).
            //   3. Remove a trailing ".git" suffix.
            name = remoteUrl
                .Replace("git@", string.Empty)
                .Replace("https://", string.Empty)
                .Replace("http://", string.Empty);
            var colonIdx = name.IndexOf(':');
            var slashIdx = name.IndexOf('/');
            if (colonIdx >= 0 && (slashIdx < 0 || colonIdx < slashIdx))
                name = name[(colonIdx + 1)..];
            else if (slashIdx >= 0)
                name = name[(slashIdx + 1)..];
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
        }
        else
        {
            name = Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
        }
        return (name, remoteUrl);
    }

    public IEnumerable<string> GetChangedFiles(string repoPath, string commitHash)
    {
        using var repo = new Repository(repoPath);
        var commit = repo.Lookup<Commit>(commitHash);
        if (commit == null)
            yield break;

        if (!commit.Parents.Any())
        {
            // Initial commit: compare against empty tree
            var emptyTree = new EmptyTreeTreeEntry();
            foreach (var entry in commit.Tree)
                yield return entry.Path;
            yield break;
        }

        var parent = commit.Parents.First();
        var diff = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
        foreach (var change in diff)
            yield return change.Path;
    }
}

// Sentinel class to represent the concept of an empty tree comparison
file sealed class EmptyTreeTreeEntry { }
