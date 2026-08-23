#!/usr/bin/env python3
"""
mirror-tooltips-to-automation-names.py
======================================

Gives icon-only controls an accessible name by mirroring the tooltip they already carry.

A control whose only content is a glyph has no name for assistive technology: WinUI derives
one from string Content, and a FontIcon is not a string. These controls do have a tooltip,
but UIA exposes a tooltip as help text, not as the name, so a screen reader still announces
"button" with nothing to distinguish it.

The tooltip text is exactly the right name and is already translated in all six locales, so
this copies each `<key>.ToolTipService.ToolTip` value to
`<key>.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name`. No new strings, and
the announced name follows the UI language automatically.

Idempotent: keys that already have an automation name are left alone.

Usage:
    python scripts/mirror-tooltips-to-automation-names.py <uid> [<uid> ...]
"""

from pathlib import Path
from xml.sax.saxutils import escape
import io
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
STRINGS_DIR = ROOT / "src" / "AgentX.App" / "Strings"
LOCALES = ["de", "en-US", "es", "fr", "ja", "zh-CN"]

TOOLTIP_SUFFIX = ".ToolTipService.ToolTip"
AUTOMATION_SUFFIX = ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"


def mirror(locale, uids):
    """Adds the missing automation-name entries for one locale. Returns the count added."""
    path = STRINGS_DIR / locale / "Resources.resw"
    text = io.open(path, encoding="utf-8").read()

    additions = []
    for uid in uids:
        if f'name="{uid}{AUTOMATION_SUFFIX}"' in text:
            continue

        tooltip = re.search(
            r'name="' + re.escape(uid + TOOLTIP_SUFFIX) + r'"[^>]*><value>(.*?)</value>',
            text,
            re.S)
        if not tooltip:
            print(f"    ! {locale}: {uid} has no tooltip to mirror")
            continue

        additions.append(
            f'  <data name="{uid}{AUTOMATION_SUFFIX}" xml:space="preserve">'
            f"<value>{escape(tooltip.group(1))}</value></data>")

    if not additions:
        return 0

    closing = text.rindex("</root>")
    updated = text[:closing] + "\n".join(additions) + "\n" + text[closing:]
    io.open(path, "w", encoding="utf-8", newline="\n").write(updated)
    return len(additions)


def main():
    uids = sys.argv[1:]
    if not uids:
        print(__doc__)
        return 1

    print(f"mirroring tooltips to automation names for {len(uids)} controls")
    for locale in LOCALES:
        print(f"  {locale:6s} +{mirror(locale, uids)} entries")
    return 0


if __name__ == "__main__":
    sys.exit(main())
