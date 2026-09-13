#!/usr/bin/env python3
"""
normalize-spacing.py
====================

Puts every spacing value in the app on DESIGN.md's base-4 grid.

DESIGN.md (Spacing) says all stops are 4px multiples and asks for a broken,
Fibonacci-flavoured cadence rather than mechanical even spacing. Those two
sentences are not in tension: the cadence comes from choosing 4 / 8 / 12 / 20 /
32 / 52 / 84 for different jobs, not from values like 6, 10 and 14 that sit
between stops. The 2026-09 audit found 767 XAML attributes and 15 code-behind
literals carrying such a component.

The rule, exactly as the guard test (SpacingIsOnTheFourPixelGridTests) enforces it:

  * Attributes: Margin, Padding, Spacing, RowSpacing, ColumnSpacing in XAML, and
    new Thickness(...) literals in C#.
  * A component of 5 or more must be a multiple of 4.
  * Components 0 to 4 are optical fine adjustments (hairline offsets, 1px bevels,
    2px stacks between a label and its value) and are left alone, as are
    negative values (overlap and optical alignment) and anything non-numeric
    (resource references, bindings).
  * An off-grid component snaps to the nearest multiple of 4; an exact tie (6,
    10, 14, ...) rounds toward the compact stop, because DESIGN.md's density is
    "professional compact" and tightening honours that where loosening would not.

Usage:
  python scripts/normalize-spacing.py --check    # report, exit 1 if anything is off-grid
  python scripts/normalize-spacing.py --apply    # rewrite files in place, then report
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "AgentX.App"

XAML_ATTR = re.compile(
    r'\b(?P<attr>Margin|Padding|Spacing|RowSpacing|ColumnSpacing)="(?P<val>-?[0-9][0-9,\-\s\.]*)"')
CS_THICKNESS = re.compile(r'new Thickness\((?P<args>[^)]*)\)')

MIN_GOVERNED = 5


def snap(value: float) -> float:
    """Nearest multiple of 4; exact ties round down (toward compact)."""
    lower = (value // 4) * 4
    upper = lower + 4
    if value - lower < upper - value:
        return lower
    if upper - value < value - lower:
        return upper
    return lower


def is_off_grid(value: float) -> bool:
    return value >= MIN_GOVERNED and value % 4 != 0


def fmt(value: float) -> str:
    return str(int(value)) if value == int(value) else str(value)


def normalize_components(raw: str) -> tuple[str, int]:
    """Rewrites a comma-separated component list; returns (text, components changed)."""
    parts = [p.strip() for p in raw.split(",")]
    changed = 0
    out = []
    for part in parts:
        try:
            value = float(part)
        except ValueError:
            out.append(part)
            continue
        if is_off_grid(value):
            out.append(fmt(snap(value)))
            changed += 1
        else:
            out.append(part)
    return ",".join(out), changed


def source_files():
    for path in APP.rglob("*"):
        if path.suffix.lower() not in (".xaml", ".cs"):
            continue
        parts = set(path.parts)
        if "bin" in parts or "obj" in parts:
            continue
        yield path


def process(path: Path, apply: bool) -> tuple[int, list[str]]:
    text = path.read_text(encoding="utf-8-sig")
    offenders: list[str] = []
    total = 0

    def xaml_sub(m: re.Match) -> str:
        nonlocal total
        new_val, n = normalize_components(m.group("val"))
        if n:
            total += n
            line = text[: m.start()].count("\n") + 1
            offenders.append(f"{path.relative_to(ROOT)}:{line} {m.group(0)} -> {m.group('attr')}=\"{new_val}\"")
            return f'{m.group("attr")}="{new_val}"'
        return m.group(0)

    def cs_sub(m: re.Match) -> str:
        nonlocal total
        new_args, n = normalize_components(m.group("args"))
        if n:
            total += n
            line = text[: m.start()].count("\n") + 1
            # keep the original spacing style "a, b, c"
            pretty = ", ".join(x.strip() for x in new_args.split(","))
            offenders.append(f"{path.relative_to(ROOT)}:{line} {m.group(0)} -> new Thickness({pretty})")
            return f"new Thickness({pretty})"
        return m.group(0)

    if path.suffix.lower() == ".xaml":
        new_text = XAML_ATTR.sub(xaml_sub, text)
    else:
        new_text = CS_THICKNESS.sub(cs_sub, text)

    if apply and new_text != text:
        # Preserve the file's BOM state and LF endings.
        had_bom = path.read_bytes().startswith(b"\xef\xbb\xbf")
        path.write_text(new_text, encoding="utf-8-sig" if had_bom else "utf-8", newline="\n")

    return total, offenders


def main() -> int:
    if len(sys.argv) != 2 or sys.argv[1] not in ("--check", "--apply"):
        print(__doc__)
        return 2
    apply = sys.argv[1] == "--apply"

    grand = 0
    files_touched = 0
    all_offenders: list[str] = []
    for path in sorted(source_files()):
        n, offenders = process(path, apply)
        if n:
            grand += n
            files_touched += 1
            all_offenders.extend(offenders)

    verb = "normalized" if apply else "off-grid"
    print(f"{grand} spacing component(s) {verb} across {files_touched} file(s)")
    for line in all_offenders[:400]:
        print("  " + line)
    if len(all_offenders) > 400:
        print(f"  ... {len(all_offenders) - 400} more")
    return 0 if (apply or grand == 0) else 1


if __name__ == "__main__":
    sys.exit(main())
