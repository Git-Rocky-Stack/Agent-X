#!/usr/bin/env python3
"""
inject-audit-l10n.py
====================

Adds the localization keys introduced by the code-quality audit's UI wiring to every
locale's Resources.resw, keeping the six locales at full key-set parity.

Input is a JSON map of:

    "<KeyName>": {
        "attrs": ["tip" | "auto" | ".Text" | ".Content" | ".PlaceholderText", ...],
        "en": "...", "de": "...", "es": "...", "fr": "...", "ja": "...", "zh-CN": "..."
    }

The "attrs" shorthands expand to the attribute suffixes this repo already uses:

    tip  -> .ToolTipService.ToolTip
            .[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name
    auto -> .[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name

Entries are appended before </root>. The resw reader indexes by name rather than
position, so appending is correct and keeps the diff readable.

Idempotent: a key already present in a locale is skipped, so re-running is safe.

Usage:
    python scripts/inject-audit-l10n.py <translations.json>
"""

from pathlib import Path
from xml.sax.saxutils import escape
import io
import json
import sys

ROOT = Path(__file__).resolve().parents[1]
STRINGS_DIR = ROOT / "src" / "AgentX.App" / "Strings"
LOCALES = ["de", "en-US", "es", "fr", "ja", "zh-CN"]

TOOLTIP_SUFFIX = ".ToolTipService.ToolTip"
AUTOMATION_SUFFIX = ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"


def expand_suffixes(attrs):
    """Turns the shorthand attribute list into concrete resw name suffixes."""
    suffixes = []
    for attr in attrs:
        if attr == "tip":
            suffixes.extend([TOOLTIP_SUFFIX, AUTOMATION_SUFFIX])
        elif attr == "auto":
            suffixes.append(AUTOMATION_SUFFIX)
        else:
            suffixes.append(attr)
    return suffixes


def inject(locale, translations):
    """Appends every missing entry for one locale. Returns the number added."""
    path = STRINGS_DIR / locale / "Resources.resw"
    text = io.open(path, encoding="utf-8").read()

    # The source strings are keyed "en"; the locale folder is "en-US".
    source_key = "en" if locale == "en-US" else locale

    closing = text.rindex("</root>")
    additions = []

    for key, spec in translations.items():
        value = spec.get(source_key)
        if value is None:
            raise KeyError(f"{key} has no {source_key} translation")

        for suffix in expand_suffixes(spec["attrs"]):
            name = key + suffix
            if f'name="{name}"' in text:
                continue
            additions.append(
                f'  <data name="{name}" xml:space="preserve">'
                f"<value>{escape(value)}</value></data>"
            )

    if not additions:
        return 0

    updated = text[:closing] + "\n".join(additions) + "\n" + text[closing:]
    io.open(path, "w", encoding="utf-8", newline="\n").write(updated)
    return len(additions)


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 1

    translations = json.load(io.open(sys.argv[1], encoding="utf-8"))
    print(f"{len(translations)} keys to inject across {len(LOCALES)} locales")

    for locale in LOCALES:
        added = inject(locale, translations)
        print(f"  {locale:6s} +{added} entries")

    return 0


if __name__ == "__main__":
    sys.exit(main())
