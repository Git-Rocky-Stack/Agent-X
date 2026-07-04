#!/usr/bin/env python3
"""Bisect aid: remove (or restore) x:Uid attributes in a XAML file.

  python scripts/strip-uids.py <xaml-path> strip  Uid1 Uid2 ...
  python scripts/strip-uids.py <xaml-path> strip-regex "^QuickAct_"

Restore by re-running scripts/localize-pages.py (idempotent inserts).
"""
import re
import sys
from pathlib import Path

path = Path(sys.argv[1])
mode = sys.argv[2]
text = path.read_text(encoding="utf-8")
removed = 0
if mode == "strip":
    for uid in sys.argv[3:]:
        new = text.replace(f'x:Uid="{uid}" ', "")
        if new != text:
            removed += 1
        text = new
elif mode == "strip-regex":
    rx = re.compile(sys.argv[3])
    def sub(m):
        global removed
        if rx.search(m.group(1)):
            removed += 1
            return ""
        return m.group(0)
    text = re.sub(r'x:Uid="([^"]+)" ', sub, text)
else:
    raise SystemExit(f"unknown mode {mode}")
path.write_text(text, encoding="utf-8")
print(f"removed {removed} x:Uid attributes from {path.name}")
