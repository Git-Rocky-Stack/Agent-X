#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
migrate-themeresource.py — Tier 5a / Tier 5b migration tool.

Rewrites {StaticResource <Key>} → {ThemeResource <Key>} for theme-sensitive
brush keys only. Spacing, radius, padding, breakpoints, shadow geometry,
and the raw Color tokens stay as StaticResource (they are theme-invariant
and StaticResource is faster).

Why the surgical approach:
  • {ThemeResource} re-resolves when the active theme changes; {StaticResource}
    resolves once at load time and never updates. To make the Light and
    HighContrast palettes actually take effect at runtime, every consumer of
    a brush key must use ThemeResource.
  • But the Color tokens (Black, Red500, Surface1, etc.) are SHARED across
    themes and don't change — switching their consumers to ThemeResource
    would do nothing useful and would cost a runtime lookup per resolve.
  • Same for spacing/radius/padding — pure layout, no theme variance.

Usage:
  python scripts/migrate-themeresource.py --files <file1.xaml> <file2.xaml> ...
  python scripts/migrate-themeresource.py --dir src/AgentX.App/Styles
  python scripts/migrate-themeresource.py --tier5a    # PoC slice (default)
  python scripts/migrate-themeresource.py --tier5b    # full migration sweep
  python scripts/migrate-themeresource.py --dry-run   # report-only

Idempotent: re-runs on already-migrated files report zero changes.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path
from typing import Iterable

# Theme-sensitive brush keys — these MUST use ThemeResource so they
# re-resolve when the user toggles Dark/Light/HighContrast. Sourced from
# the ThemeDictionaries blocks in src/AgentX.App/Styles/Colors.xaml.
THEMED_BRUSH_KEYS = frozenset({
    # Window & Page Backgrounds
    "WindowBackgroundBrush",
    "PageBackgroundBrush",
    "BackgroundSecondaryBrush",
    # Ambient / "Gradient" legacy keys (now solid)
    "WindowGradientBrush",
    "SidebarGradientBrush",
    "HeaderGradientBrush",
    # Card surfaces
    "CardBrush",
    "CardElevatedBrush",
    "CardHighBrush",
    "CardHoverBrush",
    "CardPressedBrush",
    "CardGradientBrush",
    "CardGradientVerticalBrush",
    "CardGradientSubtleBrush",
    "AccentCardGradientBrush",
    "AccentGlowGradientBrush",
    # Borders
    "BorderSubtleBrush",
    "BorderMediumBrush",
    "BorderStrongBrush",
    "BorderFocusBrush",
    "BorderGradientBrush",
    "BorderAccentGradientBrush",
    # Text
    "TextPrimaryBrush",
    "TextSecondaryBrush",
    "TextTertiaryBrush",
    "TextDisabledBrush",
    "TextInverseBrush",
    "TextAccentBrush",
    "TextLinkBrush",
    # Accent
    "AccentPrimaryBrush",
    "AccentHoverBrush",
    "AccentPressedBrush",
    "AccentSubtleBrush",
    "AccentGlowBrush",
    "AccentGradientBrush",
    "AccentGradientHoverBrush",
    # Status
    "SuccessBrush",
    "WarningBrush",
    "ErrorBrush",
    "InfoBrush",
    "OnlineBrush",
    "OfflineBrush",
    "SuccessSubtleBrush",
    "WarningSubtleBrush",
    "ErrorSubtleBrush",
    "InfoSubtleBrush",
    # Data visualization
    "GraphTagBrush",
    "GraphTagSubtleBrush",
    "ChartSeriesBrush",
    "ChartSeriesSubtleBrush",
    # Inputs
    "InputBackgroundBrush",
    "InputBorderBrush",
    "InputFocusBorderBrush",
    "InputPlaceholderBrush",
    # Scrollbars
    "ScrollbarTrackBrush",
    "ScrollbarThumbBrush",
    "ScrollbarThumbHoverBrush",
    # Overlays / shadows
    "OverlayBrush",
    "OverlayLightBrush",
})

# Tier 5a PoC slice — the highest-leverage files that, once migrated, prove
# theme switching works end-to-end across the most-viewed screens. Shared
# style dictionaries cover ~80% of pages by transitive reference; the named
# pages cover the remaining anchor surfaces.
TIER_5A_FILES = [
    # Shared style dictionaries (referenced by virtually every page)
    "src/AgentX.App/Styles/Typography.xaml",
    "src/AgentX.App/Styles/Controls.xaml",
    "src/AgentX.App/Styles/Navigation.xaml",
    "src/AgentX.App/Styles/Chat.xaml",
    "src/AgentX.App/Styles/Documents.xaml",
    "src/AgentX.App/Styles/UserGuideSections.xaml",
    "src/AgentX.App/Styles/UserGuideSections.Core.xaml",
    "src/AgentX.App/Styles/UserGuideSections.Features.xaml",
    "src/AgentX.App/Styles/UserGuideSections.Advanced.xaml",
    "src/AgentX.App/Styles/UserGuideSections.PowerUser.xaml",
    "src/AgentX.App/Styles/UserGuideSections.Research.xaml",
    "src/AgentX.App/Styles/UserGuideSections.Automation.xaml",
    "src/AgentX.App/Styles/UserGuideSections.Tutorials.xaml",
    "src/AgentX.App/Styles/UserGuideSections.Extended.xaml",
    # Root shell + the 5 anchor pages where users spend most time
    "src/AgentX.App/MainWindow.xaml",
    "src/AgentX.App/Views/SettingsPage.xaml",        # theme picker lives here
    "src/AgentX.App/Views/ChatPage.xaml",            # primary workflow
    "src/AgentX.App/Views/DashboardPage.xaml",       # landing
    "src/AgentX.App/Views/UserGuidePage.xaml",       # high-traffic reference
]

# Regex matches both `{StaticResource KeyName}` and `{StaticResource
# KeyName }` (trailing whitespace). The brush key is captured for whitelist
# filtering. XAML allows whitespace around the markup-extension argument.
_PATTERN = re.compile(r"\{StaticResource\s+(\w+)\s*\}")


def migrate_text(text: str, themed_keys: frozenset[str]) -> tuple[str, int]:
    """Return (new_text, change_count) for one file's content."""
    changes = 0

    def replace(match: re.Match[str]) -> str:
        nonlocal changes
        key = match.group(1)
        if key in themed_keys:
            changes += 1
            return "{ThemeResource " + key + "}"
        return match.group(0)

    new_text = _PATTERN.sub(replace, text)
    return new_text, changes


def migrate_file(path: Path, themed_keys: frozenset[str], dry_run: bool) -> int:
    """Migrate one file in place. Returns count of {StaticResource} → {ThemeResource} swaps."""
    if not path.exists():
        print(f"[SKIP] {path} — not found")
        return 0

    # Detect newline style so we preserve it. Most XAML in this repo is CRLF
    # but we don't want a migration sweep to flip line endings under us.
    raw_bytes = path.read_bytes()
    if b"\r\n" in raw_bytes:
        newline = "\r\n"
    else:
        newline = "\n"

    text = raw_bytes.decode("utf-8")
    new_text, changes = migrate_text(text, themed_keys)

    if changes == 0:
        print(f"[OK  ] {path} — no themed-brush StaticResource references")
        return 0

    if dry_run:
        print(f"[DRY ] {path} — would migrate {changes} reference(s)")
        return changes

    # Re-normalize newlines to the file's existing convention. We round-trip
    # via universal-decoded text so re.sub does not introduce stray '\r'.
    out_bytes = new_text.encode("utf-8")
    if newline == "\r\n":
        # Reconvert any LF that escaped from the regex back to CRLF.
        out_bytes = out_bytes.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    path.write_bytes(out_bytes)
    print(f"[MIG ] {path} — migrated {changes} reference(s)")
    return changes


def gather_files(args: argparse.Namespace, repo_root: Path) -> Iterable[Path]:
    if args.files:
        for f in args.files:
            yield Path(f) if Path(f).is_absolute() else (repo_root / f)
    elif args.dir:
        root = Path(args.dir) if Path(args.dir).is_absolute() else (repo_root / args.dir)
        yield from root.rglob("*.xaml")
    elif args.tier5b:
        # Full sweep — every XAML under src/AgentX.App, excluding build
        # artifacts (obj/, bin/) which can contain copies of WinUI SDK
        # XAML payloads after a debug build.
        app_root = repo_root / "src" / "AgentX.App"
        for p in app_root.rglob("*.xaml"):
            if "obj" in p.parts or "bin" in p.parts:
                continue
            yield p
    else:
        # Default = Tier 5a slice
        for rel in TIER_5A_FILES:
            yield repo_root / rel


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    g = parser.add_mutually_exclusive_group()
    g.add_argument("--files", nargs="+", help="Specific XAML files to migrate.")
    g.add_argument("--dir", help="Directory to recursively migrate.")
    g.add_argument("--tier5a", action="store_true", default=True,
                   help="Migrate the Tier 5a PoC slice (default).")
    g.add_argument("--tier5b", action="store_true",
                   help="Migrate every XAML under src/AgentX.App (full sweep).")
    parser.add_argument("--dry-run", action="store_true",
                        help="Report what would change, write nothing.")
    args = parser.parse_args()

    # Repo root resolves relative paths against the directory containing
    # scripts/, not the user's PWD — so the script works from anywhere.
    repo_root = Path(__file__).resolve().parent.parent

    files = list(gather_files(args, repo_root))
    if not files:
        print("No files matched.")
        return 1

    total_changes = 0
    files_changed = 0
    for f in files:
        n = migrate_file(f, THEMED_BRUSH_KEYS, args.dry_run)
        total_changes += n
        if n > 0:
            files_changed += 1

    print()
    print(f"Summary: {total_changes} reference(s) migrated across {files_changed} file(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
