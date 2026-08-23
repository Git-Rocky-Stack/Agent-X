#!/usr/bin/env python3
"""
name-unlabelled-controls.py
===========================

Gives an accessible name to every interactive control whose content is a panel rather than
text. WinUI derives a Button's automation name from its Content only when that content is a
string, so a button holding an icon plus a label reaches a screen reader as an unlabelled
button.

Rather than duplicate each label into a new resource string, the button is pointed at the
label it already renders:

    AutomationProperties.LabeledBy="{x:Bind SomeLabel}"          (page scope)
    AutomationProperties.LabeledBy="{Binding ElementName=...}"   (inside a DataTemplate)

x:Bind is wrong inside a DataTemplate here: the name would be resolved against the
template's x:DataType first. This adds no translation debt, because the referenced TextBlock
is already localized and the announced name follows the UI language automatically.

Controls with no TextBlock at all (pure icon buttons) are reported rather than touched, since
they need a real localized name instead of a reference.

Usage:
    python scripts/name-unlabelled-controls.py [--dry-run]
"""

from pathlib import Path
import io
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "AgentX.App"
NL = chr(10)

CONTROLS = r"(Button|HyperlinkButton|AppBarButton|ToggleButton)"


def element_end(src, start, tag):
    """Index just past the matching close tag for the element opening at `start`."""
    depth = 0
    i = start
    pattern = re.compile(r"<" + tag + r"[\s>/]|</" + tag + r">")
    while i < len(src):
        m = pattern.search(src, i)
        if not m:
            return len(src)
        if src.startswith("</" + tag + ">", m.start()):
            depth -= 1
            if depth == 0:
                return m.end()
            i = m.end()
        else:
            end = src.find(">", m.start())
            if src[end - 1] == "/":
                if depth == 0:
                    return end + 1
            else:
                depth += 1
            i = end + 1
    return len(src)


def template_spans(src):
    """Character ranges covered by DataTemplate elements, with each template's key."""
    spans = []
    for m in re.finditer(r"<DataTemplate\b([^>]*)>", src):
        key = re.search(r'x:Key="([^"]+)"', m.group(1))
        spans.append((m.start(),
                      element_end(src, m.start(), "DataTemplate"),
                      key.group(1) if key else None))
    return spans


def label_name(button_attrs, label_text, page, used, template_key=None):
    """
    Picks a readable x:Name for the label, preferring the button's own name so the
    relationship is obvious in markup: FilterPdf -> FilterPdfLabel.
    """
    button_name = re.search(r'x:Name="([^"]+)"', button_attrs)
    if button_name:
        candidate = button_name.group(1) + "Label"
    elif (label_text or "").lstrip().startswith("{"):
        # The label is data-bound, so its text says nothing useful. Name it after the
        # template whose row it labels.
        candidate = (template_key or page) + "Label"
    else:
        # Upper-case the first letter only, so an already-PascalCase source such as a resw
        # segment ("InstallButton") keeps its shape instead of becoming "Installbutton".
        words = re.findall(r"[A-Za-z]+", label_text or "")
        stem = "".join(w[:1].upper() + w[1:] for w in words)[:28]
        candidate = (stem or page) + "Label"

    name = candidate
    suffix = 2
    while name in used:
        name = f"{candidate}{suffix}"
        suffix += 1
    used.add(name)
    return name


def attribute_indent(src, tag_start):
    """
    Indentation used by this element's continuation attribute lines, so the inserted
    attribute lines up with its siblings instead of breaking the block's alignment.
    """
    line_start = src.rfind(NL, 0, tag_start) + 1
    first_line_end = src.find(NL, tag_start)
    if first_line_end != -1:
        next_line = src[first_line_end + 1:src.find(NL, first_line_end + 1)]
        # A continuation attribute line is "Name=" or "Namespace.Name=" and nothing else.
        if re.match(r'^\s*[\w:]+(\.[\w:]+)*\s*=', next_line):
            return " " * (len(next_line) - len(next_line.lstrip()))
    # Single-line element: indent one level past the tag itself.
    return " " * (tag_start - line_start + 4)


def process(path, used_names, dry_run):
    src = io.open(path, encoding="utf-8").read()
    original = src
    templates = template_spans(src)
    skipped = []
    named = 0

    # Work backwards so earlier offsets stay valid while splicing.
    for m in reversed(list(re.finditer(r"<" + CONTROLS + r"(\s[^<]*?)?>", src, re.S))):
        attrs = m.group(2) or ""
        if "AutomationProperties.Name" in attrs or "AutomationProperties.LabeledBy" in attrs:
            continue
        if re.search(r'\bContent\s*=\s*"', attrs) or "x:Uid" in attrs:
            continue

        tag = m.group(1)
        body_start, body_end = m.end(), element_end(src, m.start(), tag)

        # A label whose text comes from a resw x:Uid carries no Text attribute, but it is
        # still the label the user reads.
        label = next(
            (t for t in re.finditer(r'<TextBlock\b[^>]*?/?>', src[body_start:body_end])
             if 'Text="' in t.group(0) or 'x:Uid="' in t.group(0)),
            None)
        if not label:
            skipped.append(f"{path.name}:{src[:m.start()].count(NL) + 1}")
            continue

        label_start = body_start + label.start()
        label_open_end = src.find(">", label_start)
        label_tag = src[label_start:label_open_end]

        enclosing = next((t for t in templates if t[0] < m.start() < t[1]), None)

        existing = re.search(r'x:Name="([^"]+)"', label_tag)
        if existing:
            name = existing.group(1)
            used_names.add(name)
        else:
            text = re.search(r'\bText="([^"]*)"', label.group(0))
            uid = re.search(r'\bx:Uid="([^"]*)"', label.group(0))
            stem_source = text.group(1) if text else (uid.group(1).split("_")[-1] if uid else "")
            name = label_name(attrs, stem_source, path.stem, used_names,
                              template_key=enclosing[2] if enclosing else None)
            anchor = label_start + len("<TextBlock")
            src = src[:anchor] + f' x:Name="{name}"' + src[anchor:]

        in_template = enclosing is not None
        reference = (f"{{Binding ElementName={name}}}" if in_template
                     else f"{{x:Bind {name}}}")

        indent = attribute_indent(src, m.start())
        close = src.find(">", m.start())
        src = (src[:close] +
               NL + indent + f'AutomationProperties.LabeledBy="{reference}"' +
               src[close:])
        named += 1

    if src != original and not dry_run:
        io.open(path, "w", encoding="utf-8", newline=NL).write(src)

    return named, skipped


def main():
    dry_run = "--dry-run" in sys.argv
    total, all_skipped = 0, []

    for path in sorted(APP.rglob("*.xaml")):
        if "bin" in path.parts or "obj" in path.parts:
            continue
        named, skipped = process(path, set(), dry_run)
        total += named
        all_skipped.extend(skipped)

    print(f"named {total} controls via LabeledBy{' (dry run)' if dry_run else ''}")
    print(f"{len(all_skipped)} icon-only controls need a localized name instead:")
    for s in sorted(all_skipped):
        print(f"    {s}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
