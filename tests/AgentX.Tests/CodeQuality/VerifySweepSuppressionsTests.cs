using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the disposition record for the repository verification sweep.
/// <para>
/// The sweep reports every file it cannot prove reachable. Most of those reports are
/// real; a minority are limits of the scanner rather than defects, and those are
/// dismissed by listing the path in <c>.claude/verify-ignore</c>. A dismissal is a
/// judgement call, so the record of it has to survive: it must reach a fresh checkout,
/// it must say why, and its reasoning must still be true.
/// </para>
/// <para>
/// It did not survive. <c>.gitignore</c> excluded the whole <c>.claude/</c> directory,
/// so the file holding every dismissal was never committed. Anyone cloning the repo saw
/// the full finding list with no record of which ones had already been judged, or on
/// what grounds, and the only way to tell a fresh finding from a settled one was to
/// re-derive all of them by hand.
/// </para>
/// </summary>
public sealed class VerifySweepSuppressionsTests
{
    [Fact]
    public void SuppressionFile_IsCommitted()
    {
        var root = ResolveRepoRoot();
        var relative = ".claude/verify-ignore";

        File.Exists(Path.Combine(root, ".claude", "verify-ignore")).Should().BeTrue(
            "the sweep reads its dismissals from this path");

        var tracked = Git(root, $"ls-files -- {relative}");

        tracked.Trim().Should().Be(
            relative,
            "a dismissal that only exists on one machine is not a record. Every finding "
            + "the sweep no longer reports has to be traceable to a committed reason, or a "
            + "fresh checkout cannot tell a settled finding from a new one.");
    }

    [Fact]
    public void EverySuppression_StatesAReason()
    {
        var entries = ReadSuppressions();

        entries.Should().NotBeEmpty("the parser must find the suppression list, or this guard is vacuous");

        var unexplained = entries
            .Where(e => e.Reason.Length == 0)
            .Select(e => $"line {e.LineNumber}: {e.Pattern}")
            .ToList();

        unexplained.Should().BeEmpty(
            "each dismissal needs a comment above it saying why the finding is not a defect. "
            + "Unexplained:\n  " + string.Join("\n  ", unexplained));
    }

    [Fact]
    public void EverySuppression_StillPointsAtSomethingThatExists()
    {
        var root = ResolveRepoRoot();
        var entries = ReadSuppressions();

        var stale = entries
            .Where(e => !PathExists(root, e.Pattern))
            .Select(e => $"line {e.LineNumber}: {e.Pattern}")
            .ToList();

        stale.Should().BeEmpty(
            "a suppression whose target is gone is a blindfold: it silences nothing today and "
            + "will silently swallow a real finding the moment a new file lands on that path. "
            + "Stale:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void SuppressionsClaimingXamlUsage_AreActuallyUsedInXaml()
    {
        var root = ResolveRepoRoot();
        var xaml = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .ToList();

        xaml.Should().NotBeEmpty("the XAML scan must find markup, or this guard is vacuous");

        var claims = ReadSuppressions()
            .Where(e => e.Section.StartsWith(XamlOnlySection, StringComparison.Ordinal))
            .ToList();

        claims.Should().NotBeEmpty(
            "the XAML-only group is the largest block of dismissals; if the parser stops "
            + "finding it, this guard has quietly stopped checking anything");

        var unproven = claims
            .Where(e =>
            {
                var typeName = Path.GetFileName(e.Pattern)
                    .Replace(".xaml.cs", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace(".cs", string.Empty, StringComparison.OrdinalIgnoreCase);
                return !xaml.Any(text => Regex.IsMatch(text, $@"\b{Regex.Escape(typeName)}\b"));
            })
            .Select(e => $"line {e.LineNumber}: {e.Pattern}")
            .ToList();

        unproven.Should().BeEmpty(
            "these are dismissed on the grounds that the sweep does not index XAML, so the "
            + "type must actually appear in XAML. If it does not, the file is an orphan and the "
            + "dismissal was wrong. Unproven:\n  " + string.Join("\n  ", unproven));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Heading of the group whose members are dismissed purely because the sweep does not
    /// index markup. Every entry under it makes a checkable claim.
    /// </summary>
    private const string XamlOnlySection = "XAML-only references";

    private sealed record Suppression(int LineNumber, string Pattern, string Reason, string Section);

    /// <summary>
    /// Parses the suppression list into (pattern, preceding comment block, enclosing
    /// section) triples. A blank line ends a comment block, so a pattern separated from the
    /// nearest comment by a gap counts as unexplained. A <c># --- Heading ---</c> line opens
    /// a section that runs until the next such line.
    /// </summary>
    private static List<Suppression> ReadSuppressions()
    {
        var path = Path.Combine(ResolveRepoRoot(), ".claude", "verify-ignore");
        var lines = File.ReadAllLines(path);
        var entries = new List<Suppression>();
        var comment = new List<string>();
        var section = string.Empty;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.Length == 0)
            {
                // A blank line closes the group as well as the comment. Without that, a
                // bare path appended to the end of the file would silently inherit the
                // heading of whatever group happened to come last, and this guard would
                // accept a dismissal nobody had written a reason for.
                comment.Clear();
                section = string.Empty;
                continue;
            }

            if (line.StartsWith('#'))
            {
                var body = line.TrimStart('#').Trim();
                var heading = Regex.Match(body, @"^-{2,}\s*(.+?)\s*-{2,}$");
                if (heading.Success)
                {
                    section = heading.Groups[1].Value;
                    continue;
                }

                comment.Add(body);
                continue;
            }

            // A per-entry comment is the reason when one is present; otherwise the entry
            // inherits the heading of the group it was filed under. An entry with neither
            // is a bare path with nothing said about it.
            var note = string.Join(" ", comment);
            entries.Add(new Suppression(
                i + 1, line, note.Length > 0 ? note : section, section));
        }

        return entries;
    }

    private static bool PathExists(string root, string pattern)
    {
        var native = pattern.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.Combine(root, native);
        return File.Exists(full) || Directory.Exists(full);
    }

    /// <summary>
    /// Runs git and returns stdout. Anything that stops git from answering (not installed,
    /// not a repository, a non-zero exit) fails the test with what actually happened, rather
    /// than letting an empty string masquerade as "the file is untracked".
    /// </summary>
    private static string Git(string root, string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not run 'git {arguments}' in '{root}'. This guard verifies what is "
                + "committed, so it cannot fall back to a filesystem check.", ex);
        }

        using (process)
        {
            process.Should().NotBeNull("git must be available to verify what is committed");

            // Drain stdout before waiting: a full pipe buffer would deadlock the wait.
            var output = process!.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000).Should().BeTrue("git should answer well within 30s");
            process.ExitCode.Should().Be(0, $"'git {arguments}' failed: {error}");
            return output;
        }
    }

    private static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            // .git is a directory in a clone and a file holding "gitdir: ..." in a worktree.
            // Requiring the directory made these four guards throw in every worktree, which is
            // where a clean-tree build gets checked, so the one place they most needed to run
            // was the one place they could not.
            var git = Path.Combine(directory.FullName, ".git");
            if ((Directory.Exists(git) || File.Exists(git)) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
