using System.IO;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

public class XamlUidExtractorTests
{
    [Fact]
    public void Extract_finds_single_uid_with_text_property()
    {
        var xaml = """<Button x:Uid="MyButton" />""";
        var tmp = WriteTempXaml(xaml);

        var result = XamlUidExtractor.ExtractFromFile(tmp);

        result.Should().ContainSingle().Which.Uid.Should().Be("MyButton");
        File.Delete(tmp);
    }

    [Fact]
    public void Extract_handles_multiple_uids_in_one_file()
    {
        var xaml = """
            <StackPanel>
                <Button x:Uid="BtnOk" />
                <Button x:Uid="BtnCancel" />
                <TextBlock x:Uid="LblStatus" />
            </StackPanel>
            """;
        var tmp = WriteTempXaml(xaml);

        var result = XamlUidExtractor.ExtractFromFile(tmp);

        result.Select(u => u.Uid).Should().BeEquivalentTo("BtnOk", "BtnCancel", "LblStatus");
        File.Delete(tmp);
    }

    [Fact]
    public void ExtractAll_recurses_subdirectories()
    {
        var tmpRoot = Directory.CreateTempSubdirectory("locale-audit-test").FullName;
        var sub = Path.Combine(tmpRoot, "Views");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(tmpRoot, "Root.xaml"), """<Button x:Uid="Root" />""");
        File.WriteAllText(Path.Combine(sub, "Nested.xaml"), """<Button x:Uid="Nested" />""");

        var result = XamlUidExtractor.ExtractAll(tmpRoot);

        result.Select(u => u.Uid).Should().Contain(new[] { "Root", "Nested" });
        Directory.Delete(tmpRoot, recursive: true);
    }

    [Fact]
    public void Extract_ignores_commented_out_uids()
    {
        var xaml = """
            <StackPanel>
                <!-- <Button x:Uid="OldButton" /> -->
                <Button x:Uid="NewButton" />
            </StackPanel>
            """;
        var tmp = WriteTempXaml(xaml);

        var result = XamlUidExtractor.ExtractFromFile(tmp);

        result.Select(u => u.Uid).Should().BeEquivalentTo("NewButton");
        File.Delete(tmp);
    }

    [Fact]
    public void Extract_records_source_file_path()
    {
        var xaml = """<Button x:Uid="MyButton" />""";
        var tmp = WriteTempXaml(xaml);

        var result = XamlUidExtractor.ExtractFromFile(tmp);

        result.Single().SourceFile.Should().EndWith(Path.GetFileName(tmp));
        File.Delete(tmp);
    }

    private static string WriteTempXaml(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"locale-audit-{Guid.NewGuid():N}.xaml");
        var wrapped = $"""
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                {content}
            </Page>
            """;
        File.WriteAllText(path, wrapped);
        return path;
    }
}
