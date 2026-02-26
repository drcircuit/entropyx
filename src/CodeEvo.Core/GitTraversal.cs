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
