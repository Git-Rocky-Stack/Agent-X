#!/usr/bin/env python3
"""
inject-translations.py
======================

Restores key-set parity for non-English locales after commit 8d68083 added
263 UserGuide_* keys to en-US/Resources.resw without parallel translations.

For each locale in {de, es, fr, ja, zh-CN}:
  1. Read existing Resources.resw (308 keys)
  2. Read scripts/translations/<locale>.json (263 key → value pairs)
  3. Append the new <data> entries to the locale's <root> before </root>
  4. Write the file back with UTF-8 + LF preserved

Idempotent: re-running skips any key already present in the target file.

The XAML resw schema accepts <data> entries in any order (the .NET resw reader
indexes by name, not by position), so appending preserves correctness while
keeping a clean diff. en-US ordering is the canonical reference for humans;
locale files diverge in order but match in key set.
"""

from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
STRINGS_DIR = ROOT / "src" / "AgentX.App" / "Strings"
TRANSLATIONS_DIR = ROOT / "scripts" / "translations"
EN_RESW = STRINGS_DIR / "en-US" / "Resources.resw"

LOCALES = ["de", "es", "fr", "ja", "zh-CN"]


def read_resw_keys(path: Path) -> set[str]:
    """Return the set of <data name="..."> keys present in a .resw file."""
    text = path.read_text(encoding="utf-8")
    return set(re.findall(r'<data\s+name="([^"]+)"', text))


def xml_escape_value(s: str) -> str:
    """Escape special characters for safe insertion into a <value> body."""
    return (s
            .replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;"))


def inject(locale: str) -> tuple[int, int, int]:
    """
    Inject missing keys into a locale's Resources.resw.
    Returns (added, skipped_existing, total_after).
    """
    json_path = TRANSLATIONS_DIR / f"{locale}.json"
    resw_path = STRINGS_DIR / locale / "Resources.resw"

    if not json_path.exists():
        print(f"  [skip] {locale}: no translation JSON at {json_path}")
        return (0, 0, 0)

    with open(json_path, encoding="utf-8") as f:
        translations: dict[str, str] = json.load(f)

    existing = read_resw_keys(resw_path)
    original_text = resw_path.read_text(encoding="utf-8")

    # Append new entries before </root>, grouped under a "UserGuide Extended"
    # comment so future readers know where they came from.
    new_lines: list[str] = []
    added = 0
    skipped = 0
    en_keys_order = list(read_resw_keys_ordered(EN_RESW))
    for key in en_keys_order:
        if key not in translations:
            continue
        if key in existing:
            skipped += 1
            continue
        value = xml_escape_value(translations[key])
        new_lines.append(
            f'  <data name="{key}" xml:space="preserve"><value>{value}</value></data>'
        )
        added += 1

    if added == 0:
        print(f"  [ok]   {locale}: already complete (+0, ={len(existing)} keys)")
        return (0, skipped, len(existing))

    block = "\n  <!-- UserGuide Extended sections (parity restoration) -->\n" + "\n".join(new_lines) + "\n"
    if "</root>" not in original_text:
        raise RuntimeError(f"No </root> closing tag in {resw_path}")
    updated_text = original_text.replace("</root>", block + "</root>", 1)

    # Preserve LF line endings (the existing files are LF; resw writers may
    # emit CRLF but the repo uses LF). Read original to detect.
    if "\r\n" in original_text and "\r\n" not in updated_text[:200]:
        updated_text = updated_text.replace("\n", "\r\n")

    resw_path.write_text(updated_text, encoding="utf-8")
    final_count = len(existing) + added
    print(f"  [ok]   {locale}: +{added} added, {skipped} skipped, ={final_count} keys total")
    return (added, skipped, final_count)


def read_resw_keys_ordered(path: Path) -> list[str]:
    """Return <data name="..."> keys in file order (canonical for en-US)."""
    text = path.read_text(encoding="utf-8")
    return re.findall(r'<data\s+name="([^"]+)"', text)


def main() -> int:
    if not EN_RESW.exists():
        print(f"ERROR: en-US/Resources.resw missing at {EN_RESW}", file=sys.stderr)
        return 2

    en_keys = read_resw_keys(EN_RESW)
    print(f"en-US canonical key count: {len(en_keys)}")
    print(f"Translations dir: {TRANSLATIONS_DIR}")
    print()

    total_added = 0
    for loc in LOCALES:
        added, _, _ = inject(loc)
        total_added += added

    print()
    print(f"Total entries added across {len(LOCALES)} locales: {total_added}")

    # Verify post-state
    print()
    print("Post-injection key counts:")
    for loc in LOCALES + ["en-US"]:
        p = STRINGS_DIR / loc / "Resources.resw"
        n = len(read_resw_keys(p))
        marker = "OK  " if n == len(en_keys) or loc == "en-US" else "FAIL"
        print(f"  [{marker}] {loc:<6}  {n} keys")

    return 0


if __name__ == "__main__":
    sys.exit(main())
