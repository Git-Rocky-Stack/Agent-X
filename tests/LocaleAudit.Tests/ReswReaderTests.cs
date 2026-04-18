using System;
using System.IO;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

public class ReswReaderTests
{
    [Fact]
    public void ReadFile_returns_all_name_value_pairs()
    {
        var resw = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="MyButton.Content" xml:space="preserve">
                <value>Click me</value>
              </data>
              <data name="MyLabel.Text" xml:space="preserve">
                <value>Hello</value>
              </data>
            </root>
            """;
        var tmp = WriteTempResw(resw);
        try
        {
            var result = ReswReader.ReadFile(tmp);

            result.Should().HaveCount(2);
            result["MyButton.Content"].Should().Be("Click me");
            result["MyLabel.Text"].Should().Be("Hello");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ReadFile_handles_empty_resw()
    {
        var resw = """
            <?xml version="1.0" encoding="utf-8"?>
            <root></root>
            """;
        var tmp = WriteTempResw(resw);
        try
        {
            var result = ReswReader.ReadFile(tmp);

            result.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ReadAllLocales_discovers_all_locale_folders()
    {
        var root = Directory.CreateTempSubdirectory("resw-locales-test").FullName;
        try
        {
            WriteResw(root, "en-US", "<data name=\"A.Text\"><value>A</value></data>");
            WriteResw(root, "fr", "<data name=\"A.Text\"><value>A-fr</value></data>");
            WriteResw(root, "ja", "");

            var result = ReswReader.ReadAllLocales(root);

            result.Keys.Should().BeEquivalentTo("en-US", "fr", "ja");
            result["en-US"]["A.Text"].Should().Be("A");
            result["fr"]["A.Text"].Should().Be("A-fr");
            result["ja"].Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WriteTempResw(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"resw-{Guid.NewGuid():N}.resw");
        File.WriteAllText(path, content);
        return path;
    }

    private static void WriteResw(string root, string locale, string entriesXml)
    {
        var dir = Path.Combine(root, locale);
        Directory.CreateDirectory(dir);
        var resw = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <root>{entriesXml}</root>
            """;
        File.WriteAllText(Path.Combine(dir, "Resources.resw"), resw);
    }
}
