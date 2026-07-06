using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace AgentX.App.Helpers;

/// <summary>
/// Attaches a soft LED glow behind a TextBlock using a Composition drop
/// shadow masked to the text's alpha channel (DESIGN.md: "render LEDs with
/// glow"). The glow visual is hosted on a sibling panel that sits directly
/// behind the text in the same layout cell, so XAML opacity animations on a
/// shared ancestor (lamp blink, strike) carry the glow with them.
///
/// High Contrast: glow is decorative and is not attached when an HC scheme
/// is active - state remains legible from the stencil word itself.
/// </summary>
internal static class CompositionGlow
{
    private sealed class GlowEntry
    {
        public SpriteVisual Visual = null!;
        public DropShadow Shadow = null!;
    }

    private static readonly ConditionalWeakTable<TextBlock, GlowEntry> Entries = new();
    private static readonly AccessibilitySettings Accessibility = new();

    /// <summary>
    /// Hosts a glow for <paramref name="text"/> on <paramref name="glowHost"/>.
    /// The host must occupy the same layout cell as the text (same Grid cell,
    /// same alignment) so the masked shadow lines up with the glyphs.
    /// </summary>
    public static void Attach(TextBlock text, UIElement glowHost, Color color, float blurRadius = 10f, float opacity = 0.85f)
    {
        if (text is null || glowHost is null) return;

        bool highContrast;
        try { highContrast = Accessibility.HighContrast; }
        catch { highContrast = false; }
        if (highContrast) return;

        if (Entries.TryGetValue(text, out var existing))
        {
            existing.Shadow.Color = color;
            return;
        }

        var hostVisual = ElementCompositionPreview.GetElementVisual(glowHost);
        var compositor = hostVisual.Compositor;

        var shadow = compositor.CreateDropShadow();
        shadow.Mask = text.GetAlphaMask();
        shadow.Color = color;
        shadow.BlurRadius = blurRadius;
        shadow.Opacity = opacity;
        shadow.Offset = Vector3.Zero;

        var sprite = compositor.CreateSpriteVisual();
        sprite.Shadow = shadow;
        sprite.Size = new Vector2((float)text.ActualWidth, (float)text.ActualHeight);

        ElementCompositionPreview.SetElementChildVisual(glowHost, sprite);

        var entry = new GlowEntry { Visual = sprite, Shadow = shadow };
        Entries.Add(text, entry);

        text.SizeChanged += (_, _) =>
        {
            if (Entries.TryGetValue(text, out var e))
            {
                e.Visual.Size = new Vector2((float)text.ActualWidth, (float)text.ActualHeight);
                e.Shadow.Mask = text.GetAlphaMask();
            }
        };
    }

    /// <summary>Re-tints an already-attached glow (e.g. phosphor to amber).</summary>
    public static void SetColor(TextBlock text, Color color)
    {
        if (text is not null && Entries.TryGetValue(text, out var entry))
        {
            entry.Shadow.Color = color;
        }
    }
}
