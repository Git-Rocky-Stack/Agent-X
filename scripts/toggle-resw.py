#!/usr/bin/env python3
"""Bisect aid: disable/enable resw data entries by uid-name regex across all
six locales (renames `<data name="X..."` to `<data name="ZZDISABLED_X..."`).

  python scripts/toggle-resw.py disable "^QuickAct_"          # disable all
  python scripts/toggle-resw.py disable "^QuickAct_[A-M]"     # disable subset
  python scripts/toggle-resw.py enable  "^QuickAct_"          # restore all
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STRINGS = ROOT / "src" / "AgentX.App" / "Strings"
LOCALES = ["en-US", "de", "es", "fr", "ja", "zh-CN"]
MARK = "ZZDISABLED_"

mode, pattern = sys.argv[1], sys.argv[2]
rx = re.compile(pattern)
total = 0
for loc in LOCALES:
    p = STRINGS / loc / "Resources.resw"
    text = p.read_text(encoding="utf-8")
    count = 0

    def sub(m):
        global count
        name = m.group(1)
        if mode == "disable":
            if not name.startswith(MARK) and rx.search(name):
                count += 1
                return f'<data name="{MARK}{name}"'
        else:
            if name.startswith(MARK) and rx.search(name[len(MARK):]):
                count += 1
                return f'<data name="{name[len(MARK):]}"'
        return m.group(0)

    text = re.sub(r'<data name="([^"]+)"', sub, text)
    p.write_text(text, encoding="utf-8")
    total += count
print(f"{mode}d {total} entries ({total // len(LOCALES)} per locale)")
