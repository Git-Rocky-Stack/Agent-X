#!/usr/bin/env python3
"""
localize-pages.py
=================

Applies the 2026-07 page-localization changeset
(scripts/translations/pages-l10n-2026-07/*.json) that retrofits x:Uid
localization onto the six previously hardcoded-English pages:

  InboxPage, ComparisonPage, OperationsPage, AnalyticsPage,
  SyncSettingsPage, OnboardingPage

For every changeset entry the script:

  1. INSTRUMENTS the XAML: finds the next occurrence of `attr="en"` (ordered,
     cursor-based, so duplicate literals within a page resolve deterministically)
     and inserts `x:Uid="<uid>" ` immediately before it. The English literal
     stays in the markup as the design-time fallback — the house convention
     established by SettingsPage (x:Uid + inline Text) — and the resw value
     overrides it at runtime.
  2. ADDS the resw entry `<uid>.<property>` to all six locale Resources.resw
     files (idempotent — existing names are skipped).
  3. EXTENDS the legacy per-locale translation JSONs
     (scripts/translations/<locale>.json) so inject-translations.py stays a
     complete record and cannot drift.

Entries with "skipUid": true add a second resw property to an element whose
x:Uid was inserted by the preceding entry (e.g. ToolTip + AutomationProperties.Name).

Run from anywhere:  python scripts/localize-pages.py
"""

from pathlib import Path
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
STRINGS_DIR = ROOT / "src" / "AgentX.App" / "Strings"
TRANSLATIONS_DIR = ROOT / "scripts" / "translations"
CHANGESET_DIR = TRANSLATIONS_DIR / "pages-l10n-2026-07"

LOCALES = ["en-US", "de", "es", "fr", "ja", "zh-CN"]
LEGACY_JSON_LOCALES = ["de", "es", "fr", "ja", "zh-CN"]

# XAML attribute -> resw property path. AutomationProperties is an attached
# property outside the default XAML namespace, so its resw name needs the
# [using:] qualifier (WinUI 3 / MRT Core resolution rule); ToolTipService
# lives in the default namespace and resolves unqualified (shipped precedent:
# Plugin_Install.ToolTipService.ToolTip).
RESW_PROP = {
    "Text": "Text",
    "Content": "Content",
    "Header": "Header",
    "PlaceholderText": "PlaceholderText",
    "OnContent": "OnContent",
    "OffContent": "OffContent",
    "ToolTipService.ToolTip": "ToolTipService.ToolTip",
    "AutomationProperties.Name": "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
}


def xml_unescape(s: str) -> str:
    return (s.replace("&lt;", "<").replace("&gt;", ">")
             .replace("&quot;", '"').replace("&apos;", "'")
             .replace("&#39;", "'").replace("&amp;", "&"))


def xml_escape(s: str) -> str:
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def instrument_xaml(page: dict) -> tuple[int, int]:
    """Insert x:Uid attributes; returns (inserted, already_present)."""
    xaml_path = ROOT / page["file"]
    text = xaml_path.read_text(encoding="utf-8")
    cursor = 0
    inserted = already = 0

    for entry in page["entries"]:
        needle = f'{entry["attr"]}="{entry["en"]}"'
        idx = text.find(needle, cursor)
        if idx < 0:
            raise RuntimeError(
                f'{page["file"]}: could not find {needle!r} for uid '
                f'{entry["uid"]} after offset {cursor}')
        if entry.get("skipUid"):
            cursor = idx + len(needle)
            continue
        uid_attr = f'x:Uid="{entry["uid"]}" '
        # Idempotency: a prior run already placed this uid.
        if f'x:Uid="{entry["uid"]}"' in text:
            already += 1
            cursor = idx + len(needle)
            continue
        text = text[:idx] + uid_attr + text[idx:]
        inserted += 1
        cursor = idx + len(uid_attr) + len(needle)

    xaml_path.write_text(text, encoding="utf-8")
    return inserted, already


def collect_resw_entries(pages: list[dict]) -> dict[str, dict[str, str]]:
    """resw name -> {locale -> value}."""
    out: dict[str, dict[str, str]] = {}
    for page in pages:
        for entry in page["entries"]:
            prop = RESW_PROP[entry["attr"]]
            name = f'{entry["uid"]}.{prop}'
            if name in out:
                raise RuntimeError(f"Duplicate resw name in changeset: {name}")
            values = {"en-US": xml_unescape(entry["en"])}
            for loc in LEGACY_JSON_LOCALES:
                values[loc] = entry["t"][loc]
            out[name] = values
    return out


def append_resw(locale: str, entries: dict[str, dict[str, str]]) -> int:
    resw = STRINGS_DIR / locale / "Resources.resw"
    text = resw.read_text(encoding="utf-8")
    existing = set(re.findall(r'<data\s+name="([^"]+)"', text))

    lines = []
    for name, values in entries.items():
        if name in existing:
            continue
        value = xml_escape(values[locale])
        lines.append(f'  <data name="{name}" xml:space="preserve"><value>{value}</value></data>')
    if not lines:
        return 0
    block = ("\n  <!-- Page localization pass 2026-07: Smart Inbox / Comparison / "
             "Operations / Analytics / Collaborative Sync / Onboarding -->\n"
             + "\n".join(lines) + "\n")
    if "</root>" not in text:
        raise RuntimeError(f"No </root> in {resw}")
    text = text.replace("</root>", block + "</root>", 1)
    resw.write_text(text, encoding="utf-8")
    return len(lines)


def extend_legacy_json(locale: str, entries: dict[str, dict[str, str]]) -> int:
    path = TRANSLATIONS_DIR / f"{locale}.json"
    if not path.exists():
        return 0
    data = json.loads(path.read_text(encoding="utf-8"))
    extended = 0
    for name, values in entries.items():
        if data.get(name) != values[locale]:
            data[name] = values[locale]
            extended += 1
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8")
    return extended


def main() -> int:
    pages = []
    for f in sorted(CHANGESET_DIR.glob("*.json")):
        pages.append(json.loads(f.read_text(encoding="utf-8")))

    total_entries = sum(len(p["entries"]) for p in pages)
    print(f"Changeset: {len(pages)} pages, {total_entries} entries")

    print("\nInstrumenting XAML with x:Uid:")
    for page in pages:
        inserted, already = instrument_xaml(page)
        print(f"  [{Path(page['file']).name:<24}] +{inserted} x:Uid inserted"
              + (f", {already} already present" if already else ""))

    entries = collect_resw_entries(pages)
    print(f"\nAppending {len(entries)} resw names per locale:")
    for locale in LOCALES:
        added = append_resw(locale, entries)
        print(f"  [{locale:<6}] +{added} added")

    print("\nSyncing legacy translation JSONs:")
    for locale in LEGACY_JSON_LOCALES:
        extended = extend_legacy_json(locale, entries)
        print(f"  [{locale:<6}] +{extended} keys")

    # Parity check across all six resw files.
    print("\nParity check:")
    key_sets = {}
    for locale in LOCALES:
        text = (STRINGS_DIR / locale / "Resources.resw").read_text(encoding="utf-8")
        key_sets[locale] = set(re.findall(r'<data\s+name="([^"]+)"', text))
        print(f"  [{locale:<6}] {len(key_sets[locale])} keys")
    canonical = key_sets["en-US"]
    ok = True
    for locale, keys in key_sets.items():
        if keys != canonical:
            ok = False
            print(f"  PARITY FAIL {locale}: missing={sorted(canonical - keys)[:5]} "
                  f"extra={sorted(keys - canonical)[:5]}")
    print("  PARITY OK" if ok else "  PARITY FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
