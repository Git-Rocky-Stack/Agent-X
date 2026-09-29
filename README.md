# Agent-X

**Local-first AI document intelligence for Windows.** Agent-X turns your own documents into a knowledge base you can search and question. It is a native .NET 8 / WinUI 3 desktop app that runs on your computer. Your documents, embeddings, conversations and database stay on your device unless you choose a cloud model provider or turn on web search.

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Windows-10%2019041%2B%20(x64)-0078d4)](docs/README.md)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/WinUI-3-blue)](https://learn.microsoft.com/windows/apps/winui/)
[![Tests](https://img.shields.io/badge/tests-4%2C967%20passing-brightgreen)](docs/CI.md)

> **Latest release:** [v2.2.0](https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.2.0) "Command Console" (source and release notes). **Latest installers:** [v2.1.1](https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.1). They are not code-signed yet, so Windows SmartScreen shows an "unknown publisher" prompt. Installers for v2.1.2 and v2.2.0 wait for a code-signing certificate; see [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md).
>
> **License:** MIT, see [LICENSE](LICENSE). Copyright (c) 2026 Rocky Elsalaymeh.

---

## What's New

The changes below are merged into `main` and will ship in the next release. The installers linked above do not contain them yet; to use them today, [build from source](#build-from-source). Every change, with the problem it fixes, is listed in the [changelog](CHANGELOG.md#unreleased).

This is a review and repair release. Every part of the app was audited, more than 250 problems were found, and each one was checked against the code and fixed at its cause, with tests wherever the test host can run them. The automated test suite grows from 3,029 to 4,967 tests. Existing databases are repaired automatically the first time the new version starts; nothing has to be migrated by hand.

**Features that now work end to end**

- **Imported documents become searchable on their own.** Indexing (chunking, embedding and full-text indexing) starts with the app and runs in the background. Before this release nothing started it, so new documents were never found by search or Ask Your Files.
- **Audio transcription works.** Install the speech-to-text model (Whisper base, about 142 MB) on the Model Manager page; nothing is downloaded until you ask. MP3, M4A, FLAC and WAV at any sample rate are converted for the transcriber (OGG and WebM when Windows has the codecs), and audio imported before the model was installed is transcribed once it is.
- **Calendar and email connectors can be set up in the app.** Enter your own Google or Microsoft OAuth app credentials on the Calendar or Email page; they apply at once, without editing settings.json or restarting. Events deleted or cancelled at the source leave Agent-X too, and you choose which mail folders to sync.
- **Annotations:** select text in a document's preview in the Knowledge Vault to save a highlight with a note. Annotations appear on the Annotations page and feed Past Self.
- **Past Self** (Temporal Identity) saves again on upgraded databases, learns from your chat prompts, highlights and reading time, answers with the stance you held at the time you pick, and shows when your view has changed since. **Draft as Me** now has your AI provider write the draft in your voice, instead of filling fixed templates.
- **Chat memories:** the context inspector lists what chat remembers about you, and you can delete one item or clear them all.
- **Quick Chat screen awareness** can be turned on in Settings: Quick Chat then adds the text of the window in front, read with Windows OCR on your computer.
- **Import Files** offers every format Agent-X can read, including images, audio and formats added by plugins.
- **Document summaries are kept.** Summarizing a document on the Quick Actions page saves the summary with the document; the Knowledge Vault preview shows it and the vault's Workflow action sends it.
- **Watch folders** are managed in Settings, and Agent-X imports new and changed files, including files added while it was closed.
- **Backups** can be scheduled on the Backup and Restore page, and **restore** now works on Windows: it checks the backup first, keeps a safety copy, and rolls back if anything fails.
- **Plugins** are activated when the app starts, can load their own libraries, and can add support for new file formats.
- **Sync Now** imports changes from your other computers and reports what happened, and records are matched safely between machines.
- **Research Mode** fetches web results, cites them in the answer, shows them as numbered sources under it, and keeps them with the message and in exports.
- **Workflow Builder** offers all five step types and checks each step's settings as you type.
- **Built-in model on the GPU:** a GPU layers setting (automatic or a fixed number) for the built-in model. GPU offload needs the NVIDIA CUDA 12 Toolkit; without it the model runs on the CPU, and the app now says so.

**Every message in your language**

- Pick English, German, Spanish, French, Japanese or Simplified Chinese in Settings, or follow Windows.
- Status lines, error messages, dialogs, notifications and relative times ("5m ago") are now translated as well as page labels: more than 1,500 messages moved from code into the translation files, and the Operations and Dashboard status logic no longer depends on English text.

**More reliable and more honest**

- Background work (indexing, the local API, sync, backups) no longer collides with what you do in the window, and one failed save no longer breaks every later save.
- AI providers report errors instead of presenting half an answer as complete. Current OpenAI and Anthropic models get the parameters they accept, and the built-in model's embeddings are correct.
- Keyword search, hybrid search and RAG return better matches, and filters apply to the whole vault instead of only the top results.
- The chat's "100% Private" badge appears only when nothing leaves your computer; otherwise the chat names where your messages go.
- Deleting documents, collections or plugins asks first; failed documents show why they failed; cost totals survive a restart.

**Safer**

- HTML exports can no longer run scripts from imported content, and CSV exports are protected against formula injection.
- Web import cannot be steered into your local network by redirects, links in remote pages or DNS tricks, and cloud metadata addresses are always blocked.
- Personal information is redacted before any AI model sees retrieved text.
- The local API token can be regenerated without a restart and is masked in Settings. Unused services, including a collaboration listener, were removed.

**Documentation** now describes what the app actually does, in every language, and uses plain punctuation throughout.

## Download and Install

| Installer | Size | Local model | Best for |
|---|---|---|---|
| [`AgentX-Setup-2.1.1-x64.exe` (SLIM)](https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.1) | 228 MB | Downloaded on first run | Most users |
| `AgentX-Setup-2.1.1-x64-offline.exe` (OFFLINE), linked from the [v2.1.1 release notes](https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.1) | 2.1 GB | Llama 3.2 3B included | Air-gapped or offline installs |

The release notes list the SHA-256 of both installers.

Requirements: Windows 10 build 19041 or later, or Windows 11, on x64. No account, no subscription, no telemetry.

## What Agent-X Does

**Knowledge Vault and import**
- Import PDF, Word (.docx), Markdown, plain text and data files (.txt, .csv, .json, .xml, .yaml and more), source code (26 file types), images (text is read with Windows OCR), audio recordings (transcribed on your computer once you install the speech-to-text model on the Model Manager page), and web pages (Web Import, or .url and .webloc shortcuts). Import single files or whole folders, or let watch folders keep a folder in sync.
- Every document is chunked, embedded and indexed in the background, auto-tagged by the active model, and checked for duplicates by content hash.
- Collections (rename, nest one level with Move into..., export, bulk delete) and annotations (select text in a document's preview to highlight it with a note; edit, filter and export them to Markdown on the Annotations page).
- **Smart Inbox:** pages clipped with the browser extension, and items from connector plugins, wait in a triage queue. Generate AI Previews drafts a summary, collection and tags for each item; you accept, defer or reject it. Calendar and email items are imported automatically.

**Search and Ask Your Files (RAG)**
- **Ask Your Files** answers questions from your documents, with citations.
- Hybrid retrieval: vector search (an HNSW index, with an exact scan for small vaults) and SQLite FTS5 keyword search (BM25), combined with Reciprocal Rank Fusion.
- HyDE for longer questions and LLM reranking, both on by default. Personal information is redacted before any model sees retrieved text.
- **Knowledge Graph:** a map of your documents, collections and tags that links documents sharing a collection or tag.

**AI chat and models**
- Streaming Markdown chat with branching, pinning, folders and history search. **Research Mode** (opt-in) adds web results from Brave, Serper or SearXNG and lists the sources under the answer. The context inspector shows what each answer was built from and the memories chat keeps about you, which you can delete.
- Export a conversation to Markdown, HTML, PDF, JSON, plain text, CSV, Word or PowerPoint, copy it as Markdown, or export all listed conversations.
- **Built-in local model:** Llama 3.2 3B Instruct (GGUF, run with LLamaSharp). GPU layers are set automatically for NVIDIA cards or by hand in Settings; offload needs the NVIDIA CUDA 12 Toolkit, otherwise the model runs on the CPU. The standard installer downloads the model on first run; the offline installer includes it.
- **Ollama** for any local model. Optional cloud providers: **OpenAI** (the chat models your API key can use, GPT-4o mini by default, including the o-series and GPT-5 reasoning models) and **Anthropic** (the models your key can use; Claude Opus 5.5, Claude Sonnet 5 and Claude Haiku 4.5 are listed when the model list cannot be fetched; Claude Sonnet 5 is the default).
- **Model Manager** pulls, removes and switches Ollama models and installs or removes the speech-to-text model. Optional multi-model routing picks a model per request with the Cost Optimized, Quality Optimized or Balanced profile. **Hardware Advisor** recommends model sizes for your memory and GPU.

**Document intelligence**
- **Document Comparison:** compare two or more documents (similarities, differences, contradictions, unique points and a summary) and export the report as Markdown.
- **Temporal Identity and Past Self:** Agent-X records what you believe, highlight and spend time reading, learns your writing voice, and answers as you would have at a chosen point in time. **Draft as Me** has your AI provider write a draft in that voice.
- **Weekly Digest** (the last seven days, computed locally without AI calls) and **Analytics** (usage, indexing health, 30-day trends).

**Automation and power use**
- **Workflows:** on-demand multi-step pipelines built from five step types (AI Prompt, Document Lookup, Text Transform, Conditional Branch, Output Format) in the Workflow Builder, with a run history.
- Command palette (Ctrl+K), Jump-To (Ctrl+P), a keyboard shortcut cheatsheet (F1), Quick Actions, and a Quick Chat window (Win+Shift+A).
- **Plugins:** manifest-validated, able to add file formats, with bulk enable, disable and uninstall. Plugins run inside Agent-X with your Windows user rights (they are not sandboxed), so install only plugins you trust.
- **Local REST API** on `localhost:9846`, protected by a bearer token, used by the browser extension and the Android companion.

**Data safety and sync**
- Optional SQLCipher (AES-256) database encryption, with the key protected by Windows DPAPI or by your passphrase. API keys and tokens are protected with DPAPI.
- **Backup and Restore** with optional AES-256-GCM password protection, scheduled backups, and a restore that validates the backup and rolls back on failure.
- **Sync** between your computers through encrypted package files in any shared folder (OneDrive, Google Drive, a NAS or a USB drive). No server and no account.
- **Calendar and email connectors** for Google (Gmail, Google Calendar) and Microsoft (Outlook mail and calendar), with read-only access. Agent-X ships no shared OAuth credentials: you register your own app with Google or Microsoft and enter its client ID on the Calendar or Email page. OAuth tokens are stored encrypted. There is no CalDAV, IMAP or Exchange (EWS) support.

**Platform**
- Six UI languages: English, German, Spanish, French, Japanese and Simplified Chinese, chosen in Settings or taken from Windows. Pages, messages and the in-app user guide are translated.
- The Command Console design system ([`DESIGN.md`](DESIGN.md)): a hardware-instrument look with live status lamps and phosphor readouts, in Night Ops (dark) and Day Shift (light), plus a HighContrast theme that follows Windows.
- 29 pages, an in-app user guide and first-run onboarding. A structural test checks that every interactive control has an accessible name.
- A browser extension that clips pages into the Smart Inbox, and a read-only Android companion (.NET MAUI) that browses and searches your vault over USB (`adb reverse`) or from the Android emulator.

## Documentation

Product, architecture and developer documentation lives under [`docs/`](docs/README.md):

| Document | Description |
|---|---|
| [`docs/README.md`](docs/README.md) | Complete product documentation: features, install, build, configuration, architecture, data storage |
| [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md) | User guide for every page and setting |
| [`docs/user-guide/getting-started/quick-start.md`](docs/user-guide/getting-started/quick-start.md) | Ten-minute setup walkthrough |
| [`docs/user-guide/faq.md`](docs/user-guide/faq.md) | Frequently asked questions |
| [`docs/user-guide/troubleshooting.md`](docs/user-guide/troubleshooting.md) | Solutions to common problems |
| [`docs/user-guide/glossary.md`](docs/user-guide/glossary.md) | Glossary |
| [`docs/user-guide/keyboard-shortcuts.md`](docs/user-guide/keyboard-shortcuts.md) | Keyboard shortcuts |
| [`docs/user-guide/scenarios/README.md`](docs/user-guide/scenarios/README.md) | Real-world scenarios |
| [`docs/user-guide/templates/README.md`](docs/user-guide/templates/README.md) | Built-in prompts, workflow templates and export templates |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | System architecture, startup sequence, data-layer design |
| [`docs/DEVELOPER-GUIDE.md`](docs/DEVELOPER-GUIDE.md) | Services, dependency injection, migrations, error handling, extension points |
| [`DESIGN.md`](DESIGN.md) | The Command Console design system: tokens, typography, hardware recipes, status semantics (the source of truth for all UI work) |
| [`docs/API-REFERENCE.md`](docs/API-REFERENCE.md) | Public API reference |
| [`API_ENDPOINTS.md`](API_ENDPOINTS.md) | The local REST API, and every outbound connection the app can make |
| [`docs/PLUGIN-DEVELOPMENT-GUIDE.md`](docs/PLUGIN-DEVELOPMENT-GUIDE.md) | Plugin development |
| [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md) | Release provenance: keyless cosign and Rekor attestation over SHA256SUMS |
| [`CHANGELOG.md`](CHANGELOG.md) | Keep a Changelog history of every release |

Indexes for AI crawlers: [`docs/llms.txt`](docs/llms.txt) and [`docs/long-llms.txt`](docs/long-llms.txt).

**Release notes:** [Unreleased](CHANGELOG.md#unreleased), [v2.2.0 "Command Console"](CHANGELOG.md), [v2.1.2](CHANGELOG.md), [v2.1.0 "Bedrock"](docs/v2.1.0-RELEASE-NOTES.md), [v2.1.0-preview.1](docs/v2.1.0-preview.1-RELEASE-NOTES.md), [v1.5.0](docs/v1.5.0-RELEASE-NOTES.md), [v1.4.0](docs/v1.4.0-RELEASE-NOTES.md), [v1.3.0](docs/v1.3.0-RELEASE-NOTES.md).

## Build from Source

```bash
git clone https://github.com/Git-Rocky-Stack/Agent-X.git
cd Agent-X

# The solution targets x64 explicitly. A bare `dotnet build` fails with a
# win-anycpu restore error, so always pass the platform:
dotnet build -c Release -p:Platform=x64

# Run the desktop app
dotnet run --project src/AgentX.App -c Release -p:Platform=x64

# Run the test suite with coverage, then check the coverage floors
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 \
  --results-directory TestResults \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings
pwsh scripts/check-coverage.ps1 -CoverageFile TestResults
```

Full build instructions, installer packaging (the SLIM and OFFLINE Inno Setup profiles) and runtime identifiers are in [`docs/README.md`](docs/README.md). CI (build, tests and coverage floors, `dotnet format`, the locale audit, NuGet and npm audits, the Android build) is documented in [`docs/CI.md`](docs/CI.md).

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the full guide, [SECURITY.md](SECURITY.md) to report a vulnerability privately, and our [Code of Conduct](CODE_OF_CONDUCT.md).

Before you open a pull request:

1. `dotnet build -c Release -p:Platform=x64` builds with no warnings.
2. The full test suite passes (see [Build from Source](#build-from-source)).
3. `pwsh scripts/check-coverage.ps1 -CoverageFile TestResults` passes. Coverage floors are a ratchet: they only go up.
4. `dotnet format AgentX.sln` leaves nothing to change. CI verifies formatting.
5. New or changed strings exist in all six locales. CI's locale audit fails a locale below 98 percent coverage and reports missing and orphaned keys.

UI changes also have to pass the structural tests in `tests/AgentX.Tests/CodeQuality/`. They exist because WinUI fails silently in ways the compiler cannot see: a button with no handler swallows the click, a `[RelayCommand]` that nothing binds is dead weight that still counts as covered, and a misspelled resource key builds cleanly and throws when the page first opens. Among them:

- `NoUnwiredInteractiveControlsTests`: every interactive control invokes something.
- `NoUnreachableViewModelCommandsTests`: every command is reachable from a view or a code path.
- `NoUndefinedXamlResourceKeysTests`: every resource key used in XAML is defined.
- `InteractiveControlsHaveAccessibleNamesTests`: every control has a name for assistive technology.

## License

[MIT](LICENSE). Copyright (c) 2026 Rocky Elsalaymeh. Built by [Strategia-X](https://strategia-x.com).
