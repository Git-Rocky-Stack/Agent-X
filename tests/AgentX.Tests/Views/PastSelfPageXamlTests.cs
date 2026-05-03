using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Views;

public sealed class PastSelfPageXamlTests
{
    [Fact]
    public void PastSelfPage_DeclaresConvertersUsedByStaticResources()
    {
        var xaml = ReadPastSelfPageXaml();

        xaml.Should().Contain("<converters:NullToVisibilityConverter x:Key=\"NullToVisibilityConverter\"");
        xaml.Should().Contain("<converters:InverseBoolConverter x:Key=\"InverseBoolConverter\"");
        xaml.Should().Contain("<converters:BoolToVisibilityConverter x:Key=\"BoolToVisibilityConverter\"");
        xaml.Should().Contain("<converters:TimeAgoConverter x:Key=\"TimeAgoConverter\"");
        xaml.Should().NotContain("StaticResource BooleanToVisibilityConverter");
        xaml.Should().NotContain("StaticResource DateTimeToStringConverter");
    }

    [Fact]
    public void PastSelfSearchButton_IsEnabledWhileIdleAndDisabledWhileLoading()
    {
        var xaml = ReadPastSelfPageXaml();

        xaml.Should().NotContain("IsEnabled=\"{x:Bind ViewModel.IsLoading, Mode=OneWay}\"");
        xaml.Should().Contain("IsEnabled=\"{x:Bind ViewModel.IsLoading, Mode=OneWay, Converter={StaticResource InverseBoolConverter}}\"");
    }

    [Fact]
    public void PastSelfDraftActions_DoNotAdvertiseUnwiredChatPrefill()
    {
        var xaml = ReadPastSelfPageXaml();
        var codeBehind = ReadPastSelfPageCodeBehind();

        xaml.Should().Contain("Content=\"Copy for Chat\"");
        xaml.Should().NotContain("Content=\"Use in Chat\"");
        codeBehind.Should().NotContain("For now");
        codeBehind.Should().NotContain("Navigate to Chat page with the draft pre-populated");
    }

    private static string ReadPastSelfPageXaml()
    {
        return File.ReadAllText(ResolvePastSelfFile("PastSelfPage.xaml"));
    }

    private static string ReadPastSelfPageCodeBehind()
    {
        return File.ReadAllText(ResolvePastSelfFile("PastSelfPage.xaml.cs"));
    }

    private static string ResolvePastSelfFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AgentX.App", "Views", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from test output directory.");
    }
}
