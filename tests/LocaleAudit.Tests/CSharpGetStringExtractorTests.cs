using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

public class CSharpGetStringExtractorTests
{
    [Fact]
    public void Extract_finds_direct_GetString_string_literal()
    {
        var cs = """
            public class Foo
            {
                public string Bar() => _localization.GetString("Nav_Dashboard");
            }
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Select(r => r.Key).Should().BeEquivalentTo("Nav_Dashboard");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Extract_ignores_empty_string_key()
    {
        var cs = """
            var a = _l.GetString("");
            var b = _l.GetString("Real_Key");
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Select(r => r.Key).Should().BeEquivalentTo("Real_Key");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Extract_finds_multiple_distinct_keys_in_one_file()
    {
        var cs = """
            public class Foo
            {
                public void Run()
                {
                    var a = _localization.GetString("Nav_Dashboard");
                    var b = _localization.GetString("Nav_Chat");
                    var c = localizationService.GetString("Nav_Settings");
                }
            }
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Select(r => r.Key).Should().BeEquivalentTo("Nav_Dashboard", "Nav_Chat", "Nav_Settings");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Extract_finds_GetString_with_format_args()
    {
        var cs = """
            var msg = _localization.GetString("Search_ResultCount", count);
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Select(r => r.Key).Should().BeEquivalentTo("Search_ResultCount");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Extract_finds_GetString_with_method_call_arg()
    {
        var cs = """
            var a = _l.GetString("Nav_Count", items.Count());
            var b = _l.GetString("Nav_Max", Math.Max(a, b));
            var c = _l.GetString("Nav_Plain", GetCount());
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Select(r => r.Key).Should().BeEquivalentTo("Nav_Count", "Nav_Max", "Nav_Plain");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Extract_ignores_non_literal_args()
    {
        var cs = """
            var a = _localization.GetString(dynamicKey);
            var b = _localization.GetString(GetKey());
            var c = _localization.GetString(someVar + "Suffix");
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Extract_ignores_single_line_commented_out_calls()
    {
        var cs = """
            public class Foo
            {
                public void Run()
                {
                    // var old = _localization.GetString("Legacy_Key");
                    var n = _localization.GetString("Active_Key");
                }
            }
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Select(r => r.Key).Should().BeEquivalentTo("Active_Key");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ExtractAll_recurses_subdirectories()
    {
        var tmpRoot = Directory.CreateTempSubdirectory("cs-audit-test").FullName;
        try
        {
            var sub = Path.Combine(tmpRoot, "Services");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(tmpRoot, "Root.cs"),
                "class R { void M() => _l.GetString(\"K_Root\"); }");
            File.WriteAllText(Path.Combine(sub, "Nested.cs"),
                "class N { void M() => _l.GetString(\"K_Nested\"); }");

            var result = CSharpGetStringExtractor.ExtractAll(tmpRoot);

            result.Select(r => r.Key).Should().Contain(new[] { "K_Root", "K_Nested" });
        }
        finally
        {
            Directory.Delete(tmpRoot, recursive: true);
        }
    }

    [Fact]
    public void Extract_does_not_pick_up_unrelated_string_literals()
    {
        var cs = """
            var label = "Nav_Dashboard"; // this is NOT a GetString call
            var r = _localization.GetString("Nav_Real");
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Select(r => r.Key).Should().BeEquivalentTo("Nav_Real");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Extract_handles_url_literals_on_separate_line_from_GetString()
    {
        // Documents the known limitation: URL-in-string and GetString on SEPARATE lines are safe.
        // Same-line `var url = "https://..."; _l.GetString("K")` would drop the K — see LIMITATION
        // comment in CSharpGetStringExtractor.SingleLineCommentRegex.
        var cs = """
            public class Foo
            {
                private const string ApiUrl = "https://api.example.com";
                public string Bar() => _l.GetString("Nav_Bar");
            }
            """;
        var tmp = WriteTempCs(cs);
        try
        {
            var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

            result.Select(r => r.Key).Should().BeEquivalentTo("Nav_Bar");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static string WriteTempCs(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cs-audit-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, content);
        return path;
    }
}
