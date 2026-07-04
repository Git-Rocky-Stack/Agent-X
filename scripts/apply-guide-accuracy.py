#!/usr/bin/env python3
"""
apply-guide-accuracy.py
=======================

Applies the 2026-07 user-guide accuracy changeset
(scripts/translations/guide-accuracy-2026-07.json) to all six locale
Resources.resw files:

  1. REMOVE every <data> entry whose name starts with a removePrefixes entry
     (guide sections for features that were never implemented).
  2. UPDATE the <value> of each key in "update" with the locale's new text.
  3. ADD each key in "add" (skipped if already present — idempotent).

Also prunes the removed keys from the legacy per-locale translation JSONs
(scripts/translations/<locale>.json) so a re-run of inject-translations.py
cannot resurrect them, and appends the new keys there to keep those files a
complete translation record.

Run from anywhere:  python scripts/apply-guide-accuracy.py
"""

from pathlib import Path
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
STRINGS_DIR = ROOT / "src" / "AgentX.App" / "Strings"
TRANSLATIONS_DIR = ROOT / "scripts" / "translations"
CHANGESET = TRANSLATIONS_DIR / "guide-accuracy-2026-07.json"

LOCALES = ["en-US", "de", "es", "fr", "ja", "zh-CN"]
LEGACY_JSON_LOCALES = ["de", "es", "fr", "ja", "zh-CN"]

DATA_RE = re.compile(
    r'[ \t]*<data\s+name="([^"]+)"[^>]*>.*?</data>[ \t]*\r?\n', re.S)


def xml_escape(s: str) -> str:
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def apply_locale(locale: str, changeset: dict) -> tuple[int, int, int]:
    resw = STRINGS_DIR / locale / "Resources.resw"
    text = resw.read_text(encoding="utf-8")
    prefixes = tuple(changeset["removePrefixes"])

    removed = 0

    def drop(m: re.Match) -> str:
        nonlocal removed
        if m.group(1).startswith(prefixes):
            removed += 1
            return ""
        return m.group(0)

    text = DATA_RE.sub(drop, text)

    updated = 0
    for key, values in changeset["update"].items():
        value = xml_escape(values[locale])
        pattern = re.compile(
            r'(<data\s+name="' + re.escape(key) + r'"[^>]*><value>).*?(</value>)', re.S)
        text, n = pattern.subn(lambda m: m.group(1) + value + m.group(2), text)
        if n != 1:
            raise RuntimeError(f"{locale}: expected exactly 1 match for {key}, got {n}")
        updated += 1

    existing = set(re.findall(r'<data\s+name="([^"]+)"', text))
    add_lines = []
    for key, values in changeset["add"].items():
        if key in existing:
            continue
        add_lines.append(
            f'  <data name="{key}" xml:space="preserve"><value>{xml_escape(values[locale])}</value></data>')
    added = len(add_lines)
    if added:
        block = ("\n  <!-- Guide accuracy pass 2026-07: Comparison / Smart Inbox / "
                 "Analytics / Operations / Collaborative Sync sections -->\n"
                 + "\n".join(add_lines) + "\n")
        if "</root>" not in text:
            raise RuntimeError(f"No </root> in {resw}")
        text = text.replace("</root>", block + "</root>", 1)

    resw.write_text(text, encoding="utf-8")
    return removed, updated, added


def prune_and_extend_legacy_json(locale: str, changeset: dict) -> tuple[int, int]:
    path = TRANSLATIONS_DIR / f"{locale}.json"
    if not path.exists():
        return (0, 0)
    data = json.loads(path.read_text(encoding="utf-8"))
    prefixes = tuple(changeset["removePrefixes"])
    before = len(data)
    data = {k: v for k, v in data.items() if not k.startswith(prefixes)}
    pruned = before - len(data)
    extended = 0
    for section in ("update", "add"):
        for key, values in changeset[section].items():
            if data.get(key) != values[locale]:
                data[key] = values[locale]
                extended += 1
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8")
    return pruned, extended


def main() -> int:
    changeset = json.loads(CHANGESET.read_text(encoding="utf-8"))

    print("Applying guide-accuracy changeset to resw files:")
    counts = {}
    for locale in LOCALES:
        removed, updated, added = apply_locale(locale, changeset)
        counts[locale] = (removed, updated, added)
        print(f"  [{locale:<6}] -{removed} removed, ~{updated} updated, +{added} added")

    print("\nSyncing legacy translation JSONs:")
    for locale in LEGACY_JSON_LOCALES:
        pruned, extended = prune_and_extend_legacy_json(locale, changeset)
        print(f"  [{locale:<6}] -{pruned} pruned, ~{extended} set")

    print("\nParity check:")
    key_sets = {}
    for locale in LOCALES:
        resw = STRINGS_DIR / locale / "Resources.resw"
        key_sets[locale] = set(re.findall(r'<data\s+name="([^"]+)"', resw.read_text(encoding="utf-8")))
    canonical = key_sets["en-US"]
    ok = True
    for locale in LOCALES:
        diff = key_sets[locale] ^ canonical
        marker = "OK  " if not diff else "FAIL"
        if diff:
            ok = False
        print(f"  [{marker}] {locale:<6} {len(key_sets[locale])} keys"
              + (f"  (diff: {sorted(diff)[:4]}...)" if diff else ""))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
