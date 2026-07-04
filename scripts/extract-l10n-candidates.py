#!/usr/bin/env python3
"""
extract-l10n-candidates.py
==========================

Generates ordered changeset SKELETONS for the full-app localization sweep
(scripts/translations/app-l10n-2026-07/). For each target XAML file it scans,
in document order, every localizable literal attribute:

  Text, Content, PlaceholderText, Header, PaneTitle, Title, OnContent,
  OffContent, ToolTipService.ToolTip, AutomationProperties.Name,
  PrimaryButtonText, SecondaryButtonText, CloseButtonText

and emits entries {uid, attr, en, t:{5 locales: ""}} compatible with
scripts/localize-pages.py. Skipped automatically: bindings ({...}), glyph-only
values, letterless values, URLs/paths/env-vars/key formats, exact proper-noun
matches, and any attribute on an element that already carries x:Uid. A second
localizable attribute on the SAME element is emitted with skipUid=true reusing
the first entry's uid (tooltip + automation-name pairs).

The output is a REVIEWED skeleton: a human prunes false positives and authors
the translations before the applier runs. uid names are auto-derived
(<PagePrefix>_<PascalSlug>) and deduped with numeric suffixes.
"""

from pathlib import Path
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "AgentX.App"
OUT_DIR = ROOT / "scripts" / "translations" / "app-l10n-2026-07"

LOCALES = ["de", "es", "fr", "ja", "zh-CN"]

# file (relative to src/AgentX.App) -> (output file stem, uid prefix)
TARGETS = [
    ("MainWindow.xaml", "01-mainwindow", "Main"),
    ("Views/DashboardPage.xaml", "02-dashboard", "Dash"),
    ("Views/ChatPage.xaml", "03-chat", "Chat"),
    ("Views/AskFilesPage.xaml", "04-askfiles", "AskFiles"),
    ("Views/SearchPage.xaml", "05-search", "Search"),
    ("Views/KnowledgeVaultPage.xaml", "06-vault", "Vault"),
    ("Views/CollectionManagerPage.xaml", "07-collections", "CollMgr"),
    ("Views/KnowledgeGraphPage.xaml", "08-graph", "Graph"),
    ("Views/AnnotationsPage.xaml", "09-annotations", "Annot"),
    ("Views/WebImportPage.xaml", "10-webimport", "WebImport"),
    ("Views/DigestPage.xaml", "11-digest", "Digest"),
    ("Views/PastSelfPage.xaml", "12-pastself", "PastSelf"),
    ("Views/QuickActionsPage.xaml", "13-quickactions", "QuickAct"),
    ("Views/ModelManagerPage.xaml", "14-modelmanager", "ModelMgr"),
    ("Views/HardwareAdvisorPage.xaml", "15-hwadvisor", "HwAdvisor"),
    ("Views/WorkflowBuilderPage.xaml", "16-wfbuilder", "WfBuilder"),
    ("Views/WorkspaceProfilePage.xaml", "17-workspace", "Workspace"),
    ("Views/SettingsPage.xaml", "18-settings", "Settings"),
    ("Views/BackupRestorePage.xaml", "19-backup", "Backup"),
    ("Views/EmailSettingsPage.xaml", "20-emailsettings", "EmailSet"),
    ("Views/CalendarSettingsPage.xaml", "21-calendarsettings", "CalSet"),
    ("Views/PluginManagerPage.xaml", "22-pluginmanager", "Plugin"),
    ("Views/ExportDialog.xaml", "23-exportdialog", "ExportDlg"),
    ("Views/Dialogs/JumpToDialog.xaml", "24-jumptodialog", "JumpTo"),
    ("Views/Dialogs/CheatsheetDialog.xaml", "25-cheatsheet", "Cheat"),
    ("Views/UserGuidePage.xaml", "26-userguide", "GuidePage"),
    ("Views/PrivacyPolicyPage.xaml", "27-privacy", "Privacy"),
    ("Views/TermsOfServicePage.xaml", "28-terms", "Terms"),
]

ATTRS = (
    "ToolTipService.ToolTip",
    "AutomationProperties.Name",
    "PlaceholderText",
    "PrimaryButtonText",
    "SecondaryButtonText",
    "CloseButtonText",
    "PaneTitle",
    "OnContent",
    "OffContent",
    "Content",
    "Header",
    "Title",
    "Text",
)

ATTR_RE = re.compile(
    r'(?<![\w.])(' + "|".join(re.escape(a) for a in ATTRS) + r')="([^"]*)"')

COMMENT_RE = re.compile(r"<!--.*?-->", re.S)
GLYPH_RE = re.compile(r"^(?:&#x[0-9A-Fa-f]+;)+$")

PROPER_NOUNS = {
    "AX", "Agent-X", "AgentX", "OpenAI", "Anthropic", "Ollama", "Claude",
    "GPT-4o, GPT-4o-mini", "Claude Sonnet, Claude Opus", "llama3.2",
    "all-minilm", "SQLite", "SQLCipher", "GitHub", "ms", "GB", "MB",
    "Strategia", "Strategia-X",
}


def skip_value(value: str) -> bool:
    v = value.strip()
    if not v or v.startswith("{"):
        return True
    if GLYPH_RE.match(v):
        return True
    if not re.search(r"[A-Za-z]", v):
        return True
    if len(v) == 1:
        return True
    if v in PROPER_NOUNS:
        return True
    lower = v.lower()
    if lower.startswith(("http://", "https://", "www.")) or "://" in lower:
        return True
    if re.match(r"^[A-Za-z]:\\", v) or v.startswith("%") or v.startswith("sk-"):
        return True
    if "localhost" in lower:
        return True
    return False


def slugify(value: str, attr: str) -> str:
    text = re.sub(r"&#?\w+;", " ", value)
    words = re.findall(r"[A-Za-z0-9]+", text)
    if not words:
        return "Item"
    slug = "".join(w.capitalize() for w in words[:4])[:30]
    if attr == "ToolTipService.ToolTip":
        slug += "Tip"
    elif attr == "PlaceholderText":
        slug += "Ph"
    return slug


def tag_span(text: str, pos: int) -> tuple[int, int]:
    start = text.rfind("<", 0, pos)
    end = text.find(">", pos)
    return (start, end if end >= 0 else len(text))


def extract(rel: str, prefix: str) -> list[dict]:
    path = APP / rel
    raw = path.read_text(encoding="utf-8")
    # Blank comments to same length so offsets stay stable.
    text = COMMENT_RE.sub(lambda m: " " * len(m.group(0)), raw)

    entries: list[dict] = []
    used_uids: dict[str, int] = {}
    span_uid: dict[int, str] = {}  # tag-span start -> uid of first entry on that element

    for m in ATTR_RE.finditer(text):
        attr, value = m.group(1), m.group(2)
        if skip_value(value):
            continue
        start, end = tag_span(text, m.start())
        segment = text[start:end]
        if 'x:Uid="' in segment:
            continue  # element already localized
        if start in span_uid:
            entries.append({
                "uid": span_uid[start], "attr": attr, "en": value,
                "skipUid": True,
                "t": {loc: "" for loc in LOCALES},
            })
            continue
        base = f"{prefix}_{slugify(value, attr)}"
        n = used_uids.get(base, 0)
        used_uids[base] = n + 1
        uid = base if n == 0 else f"{base}{n + 1}"
        span_uid[start] = uid
        entries.append({
            "uid": uid, "attr": attr, "en": value,
            "t": {loc: "" for loc in LOCALES},
        })
    return entries


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    total = 0
    for rel, stem, prefix in TARGETS:
        entries = extract(rel, prefix)
        total += len(entries)
        out = OUT_DIR / f"{stem}.json"
        payload = {"file": f"src/AgentX.App/{rel}", "entries": entries}
        out.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
        print(f"  {stem:<22} {len(entries):>4} entries")
    print(f"TOTAL: {total} entries across {len(TARGETS)} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
