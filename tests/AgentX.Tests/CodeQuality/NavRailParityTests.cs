using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards parity across the navigation rail and the surfaces that mirror it.
/// <para>
/// The rail is the app's table of contents, and three things had quietly drifted from
/// it: two pairs of items shared an icon glyph (Weekly Digest and Analytics both wore the
/// area chart, Backup and Restore wore the Knowledge Vault's library), one item sat
/// between two separators with no group placard above it, and the Ctrl+K palette carried
/// its own hardcoded list of nine pages while the rail had twenty-nine. Each of these is a
/// fact about MainWindow.xaml that can be checked without launching the app.
/// </para>
/// </summary>
public sealed class NavRailParityTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex PageMapEntry = new(
        @"\[""(?<tag>[A-Za-z]+)""\]\s*=\s*typeof\(Views\.",
        RegexOptions.Compiled);

    private static readonly Regex NavItemMapEntry = new(
        @"\[""(?<tag>[A-Za-z]+)""\]\s*=\s*Nav[A-Za-z]+",
        RegexOptions.Compiled);

    [Fact]
    public void NoTwoRailItems_ShareAnIconGlyph()
    {
        var items = RailItems().ToList();
        items.Should().HaveCountGreaterThan(20, "the scan must find the rail, otherwise this guard passes forever");

        var shared = items
            .Where(item => item.Glyph is not null)
            .GroupBy(item => item.Glyph)
            .Where(group => group.Count() > 1)
            .Select(group => $"U+{(int)group.Key![0]:X4} shared by {string.Join(", ", group.Select(i => i.Name))}")
            .ToList();

        shared.Should().BeEmpty(
            "icons survive on the rail for navigation only, and an icon that points at two " +
            "destinations no longer navigates. Shared glyphs:\n  " + string.Join("\n  ", shared));
    }

    [Fact]
    public void EveryRailItem_SitsUnderAGroupPlacard()
    {
        var menu = MenuItemsElement();
        var walked = 0;
        var orphans = new List<string>();
        var seenHeader = false;

        foreach (var element in menu.Elements())
        {
            var name = element.Name.LocalName;
            if (name == "NavigationViewItemHeader")
            {
                seenHeader = true;
            }
            else if (name == "NavigationViewItem")
            {
                walked++;
                if (!seenHeader)
                {
                    orphans.Add(element.Attribute(Xaml + "Name")?.Value ?? "(unnamed)");
                }
            }
        }

        AssertTheWalkReachedTheWholeRail(menu, walked);

        orphans.Should().BeEmpty(
            "DESIGN.md gives every rail group an Archivo stencil placard; an item with no " +
            "placard above it belongs to no group. Orphans:\n  " + string.Join("\n  ", orphans));
    }

    [Fact]
    public void NoRailGroup_IsHeaderless_BetweenSeparators()
    {
        var menu = MenuItemsElement();
        var walked = 0;
        var lastWasSeparator = false;
        var offenders = new List<string>();

        foreach (var element in menu.Elements())
        {
            var name = element.Name.LocalName;
            if (name == "NavigationViewItemSeparator")
            {
                lastWasSeparator = true;
                continue;
            }

            if (name == "NavigationViewItem")
            {
                walked++;
                if (lastWasSeparator)
                {
                    offenders.Add(element.Attribute(Xaml + "Name")?.Value ?? "(unnamed)");
                }
            }

            lastWasSeparator = false;
        }

        AssertTheWalkReachedTheWholeRail(menu, walked);

        offenders.Should().BeEmpty(
            "a separator opens a new group, and a group opens with its placard, never with " +
            "an item. Items directly after a separator:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryRailItem_IsLocalized_AndMappedToAPage()
    {
        var items = RailItems().ToList();
        var codeBehind = File.ReadAllText(MainWindowCodeBehindPath());
        var pageMap = PageMapEntry.Matches(codeBehind).Select(m => m.Groups["tag"].Value).ToHashSet();
        var navItemMap = NavItemMapEntry.Matches(codeBehind).Select(m => m.Groups["tag"].Value).ToHashSet();

        pageMap.Should().NotBeEmpty("the PageMap scan must find entries");
        navItemMap.Should().NotBeEmpty("the BuildNavItemMap scan must find entries");

        var problems = new List<string>();
        foreach (var item in items)
        {
            if (item.Uid is null) problems.Add($"{item.Name}: no x:Uid (unlocalized rail label)");
            if (item.Tag is null) problems.Add($"{item.Name}: no Tag");
            else
            {
                if (!pageMap.Contains(item.Tag)) problems.Add($"{item.Name}: Tag '{item.Tag}' has no PageMap entry");
                if (!navItemMap.Contains(item.Tag)) problems.Add($"{item.Name}: Tag '{item.Tag}' has no BuildNavItemMap entry");
            }
        }

        problems.Should().BeEmpty(
            "a rail item must resolve to a page and to a localized label. Problems:\n  " +
            string.Join("\n  ", problems));
    }

    [Fact]
    public void ThePalette_DerivesItsPagesFromTheRail_NotFromALiteralList()
    {
        // Comments are stripped: a tag quoted in a doc comment is prose, not a registration.
        var palette = Regex.Replace(
            File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "Controls", "CommandPalette.xaml.cs")),
            @"//[^\n]*", string.Empty);
        var shell = File.ReadAllText(MainWindowCodeBehindPath());

        var tags = RailItems().Select(item => item.Tag).Where(tag => tag is not null).ToList();
        tags.Should().NotBeEmpty();

        var literalTags = tags
            .Where(tag => Regex.IsMatch(palette, $@"""{Regex.Escape(tag!)}"""))
            .ToList();

        literalTags.Should().BeEmpty(
            "the palette used to carry nine of the rail's twenty-nine pages as literals and " +
            "drifted; it now receives every page from the rail through Configure(). " +
            "Literal page tags found in CommandPalette.xaml.cs:\n  " + string.Join("\n  ", literalTags));

        shell.Should().Contain("CommandPalette.Configure(",
            "MainWindow must register the rail's pages with the palette");
        shell.Should().Contain("NavView.MenuItems",
            "the registration must walk the rail, not a second list");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Placards and separators are ordering facts, so the two guards that read them walk the
    /// rail's direct children rather than its descendants. That walk used to carry no evidence
    /// it had reached anything: pointed at an empty element, both guards reported no offenders
    /// and passed. This fails instead, on either of the two ways the walk can go blind.
    /// </summary>
    private static void AssertTheWalkReachedTheWholeRail(XElement menu, int walked)
    {
        var all = menu.Descendants(Presentation + "NavigationViewItem").Count();

        walked.Should().BeGreaterThan(20,
            "the walk must actually reach the rail, otherwise this guard reports no offenders " +
            "and passes forever");

        walked.Should().Be(all,
            $"the walk reads direct children only, so it saw {walked} of the {all} rail items " +
            "under MenuItems. An item nested inside another is invisible to the placard and " +
            "separator rules. Either lift it back to the top level, or extend this walk and " +
            "decide there what a placard means for a sub-item.");
    }

    private sealed record RailItem(string Name, string? Tag, string? Uid, string? Glyph);

    private static IEnumerable<RailItem> RailItems()
    {
        var document = XDocument.Load(MainWindowXamlPath());
        var navigationView = document.Descendants(Presentation + "NavigationView").First();

        return navigationView
            .Descendants(Presentation + "NavigationViewItem")
            .Select(item => new RailItem(
                item.Attribute(Xaml + "Name")?.Value ?? "(unnamed)",
                item.Attribute("Tag")?.Value,
                item.Attribute(Xaml + "Uid")?.Value,
                item.Descendants(Presentation + "FontIcon").FirstOrDefault()?.Attribute("Glyph")?.Value));
    }

    private static XElement MenuItemsElement() =>
        XDocument.Load(MainWindowXamlPath())
            .Descendants(Presentation + "NavigationView.MenuItems")
            .First();

    private static string MainWindowXamlPath() =>
        Path.Combine(ResolveSourceRoot(), "AgentX.App", "MainWindow.xaml");

    private static string MainWindowCodeBehindPath() =>
        Path.Combine(ResolveSourceRoot(), "AgentX.App", "MainWindow.xaml.cs");

    private static string ResolveSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "AgentX.App")) &&
                Directory.Exists(Path.Combine(candidate, "AgentX.Core")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the src directory from " + AppContext.BaseDirectory);
    }
}
