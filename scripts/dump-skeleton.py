#!/usr/bin/env python3
"""Compact one-line-per-entry dump of an app-l10n changeset skeleton (review aid)."""
import io
import json
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
path = Path(__file__).resolve().parents[1] / "scripts" / "translations" / "app-l10n-2026-07" / sys.argv[1]
data = json.loads(path.read_text(encoding="utf-8"))
print(data["file"], f"({len(data['entries'])} entries)")
for i, e in enumerate(data["entries"]):
    flag = " [skipUid]" if e.get("skipUid") else ""
    print(f"{i:>3} {e['uid']:<42} {e['attr']:<26}{flag} | {e['en']}")
