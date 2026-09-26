using System.Globalization;
using System.Xml.Linq;
using AgentX.App.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Helpers;

/// <summary>
/// Caption buttons (SH25): they used to be hardcoded near-white, which nearly vanished on
/// Day Shift silver. They are now painted from theme tokens, so these tests pin down
/// that every token exists as a solid color in both shifts, that each shift's glyphs
/// read against its own chassis, that HighContrast is left to the system, and that
/// ChromeService does not drift back to literal colors.
/// </summary>
public sealed class CaptionButtonPaletteTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void For_HighContrast_LeavesTheButtonsToTheSystem()
    {
        CaptionButtonPalette.For(highContrast: true).Should().BeNull(
            "HighContrast is exempt from the hardware skin and stays system-bound");
        CaptionButtonPalette.For(highContrast: false).Should().BeSameAs(CaptionButtonPalette.Hardware);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Light")]
    public void EveryToken_IsASolidColorBrushInTheShift(string shift)
    {
        var brushes = SolidBrushes(shift);

        foreach (var key in TokenKeys())
        {
            brushes.Should().ContainKey(key,
                $"ChromeService reads SolidColorBrush.Color for '{key}'; a missing or non-solid " +
                $"entry in the {shift} ThemeDictionary silently falls back to the system color");
        }
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Light")]
    public void CaptionGlyphs_ReadAgainstTheShiftChassis(string shift)
    {
        var brushes = SolidBrushes(shift);
        var chassis = brushes["WindowBackgroundBrush"];
        var tokens = CaptionButtonPalette.Hardware;

        foreach (var key in new[] { tokens.Foreground, tokens.HoverForeground, tokens.InactiveForeground })
        {
            var contrast = ContrastRatio(Composite(brushes[key], chassis), chassis);
            contrast.Should().BeGreaterThanOrEqualTo(3.0,
                $"{key} is a caption glyph on the {shift} chassis and must stay legible " +
                "(WCAG 1.4.11 non-text contrast); the old white-on-silver glyphs measured about 1.6:1");
        }
    }

    [Fact]
    public void ChromeService_PaintsCaptionsFromTokens_NotLiterals()
    {
        var source = File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "Services", "ChromeService.cs"));

        source.Should().NotContain("Color.FromArgb(", "caption colors come from CaptionButtonPalette tokens");
        source.Should().NotContain("Colors.White", "caption colors come from CaptionButtonPalette tokens");
        source.Should().Contain("CaptionButtonPalette.For(");
        source.Should().Contain("ActualThemeChanged", "the captions must be repainted on a shift change");
    }

    private static IEnumerable<string> TokenKeys()
    {
        var tokens = CaptionButtonPalette.Hardware;
        return new[]
        {
            tokens.Foreground, tokens.HoverForeground, tokens.PressedForeground,
            tokens.InactiveForeground, tokens.HoverBackground, tokens.PressedBackground,
        };
    }

    /// <summary>x:Key to ARGB for every SolidColorBrush with a literal color in one ThemeDictionary.</summary>
    private static Dictionary<string, uint> SolidBrushes(string shift)
    {
        var colorsXaml = Path.Combine(ResolveSourceRoot(), "AgentX.App", "Styles", "Colors.xaml");
        var dictionary = XDocument.Load(colorsXaml)
            .Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            .Elements()
            .Single(e => (string?)e.Attribute(XamlNamespace + "Key") == shift);

        return dictionary.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .Select(e => (Key: (string?)e.Attribute(XamlNamespace + "Key"), Color: (string?)e.Attribute("Color")))
            .Where(e => e.Key is not null && e.Color is not null && e.Color.StartsWith('#'))
            .ToDictionary(e => e.Key!, e => ParseArgb(e.Color!));
    }

    private static uint ParseArgb(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length == 6)
        {
            digits = "FF" + digits;
        }

        return uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    /// <summary>Alpha-composites a possibly translucent color over an opaque background.</summary>
    private static uint Composite(uint foreground, uint background)
    {
        var alpha = ((foreground >> 24) & 0xFF) / 255.0;

        uint Channel(int shift)
        {
            var fg = (foreground >> shift) & 0xFF;
            var bg = (background >> shift) & 0xFF;
            return (uint)Math.Round(fg * alpha + bg * (1 - alpha));
        }

        return 0xFF000000 | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    private static double ContrastRatio(uint a, uint b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double RelativeLuminance(uint argb)
    {
        static double Linear(uint channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear((argb >> 16) & 0xFF)
             + 0.7152 * Linear((argb >> 8) & 0xFF)
             + 0.0722 * Linear(argb & 0xFF);
    }

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

        throw new DirectoryNotFoundException("Could not locate the src directory from " + AppContext.BaseDirectory);
    }
}
