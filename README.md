# Agent-X

**Local-first AI document intelligence for Windows.** Agent-X turns your personal document collection into a queryable, AI-augmented knowledge base — a native .NET 8 / WinUI 3 desktop app that runs entirely on your machine. Local by default: your documents, embeddings, conversations, and database never leave your device unless you explicitly opt in to a cloud model provider or web search.

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Windows-10%2019041%2B%20(x64)-0078d4)](docs/README.md)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/WinUI-3-blue)](https://learn.microsoft.com/windows/apps/winui/)
[![Tests](https://img.shields.io/badge/tests-2%2C773-brightgreen)](docs/CI.md)

> **Latest release:** [v2.1.1](https://github.com/Git-Rocky-Stack/Agent-X/releases/latest) (installers below) · **Current source:** [v2.1.2](CHANGELOG.md) — "Bedrock" security & supply-chain hardening; the signed v2.1.2 installer republish is pending code-signing certificate issuance.
> **License:** MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Rocky Elsalaymeh.

---

## Download & Install

| Installer | Size | Local model | Best for |
|---|---|---|---|
| [`AgentX-Setup-x64.exe` (SLIM)](https://github.com/Git-Rocky-Stack/Agent-X/releases/latest) | ~230 MB | Downloaded on first run | Most users |
| `AgentX-Setup-x64-offline.exe` (OFFLINE) — linked from the [release notes](https://github.com/Git-Rocky-Stack/Agent-X/releases/latest) | ~2.1 GB | Llama 3.2 3B pre-bundled | Air-gapped / offline installs |

Requirements: Windows 10 build 19041+ or Windows 11, x64. No account, no subscription, no telemetry.

## What Agent-X Does

**Knowledge Vault & ingestion**
- Import PDFs, Word documents, Markdown, text, code files, and web pages — single files, URLs, or entire folders
- Automatic chunking, embedding, auto-tagging, and duplicate detection on import; collections, annotations, and full document management
- **Smart Inbox** triage queue: captures from the browser extension, mobile companion, and plugins arrive with AI-drafted previews (suggested summary, collection, tags) for one-click accept / defer / reject
- Audio transcription turns recordings into searchable documents

**Search & retrieval (RAG)**
- **Ask Your Files**: natural-language questions answered from your own documents, with citations
- Hybrid retrieval: HNSW vector search + SQLite FTS5 (BM25) keyword search fused with Reciprocal Rank Fusion
- Retrieval pipeline with HyDE (hypothetical document embeddings) and LLM reranking
- Knowledge Graph visualization of entities and relationships across the vault

**AI chat & models**
- Streaming Markdown chat with conversation branching, pinning, folders, and full-text history search
- **Built-in local model** (Llama 3.2 3B, GGUF via LLamaSharp with optional GPU offload) — works fully offline, out of the box
- Ollama for any local model; opt-in cloud providers: OpenAI (GPT-4o, GPT-4o-mini, o1, o3) and Anthropic (Claude Sonnet 4, Opus, Haiku)
- Model Manager for download/removal and per-task model selection; Hardware Advisor recommends models and GPU offload settings for your machine

**Document intelligence**
- **Document Comparison**: structured AI comparison of 2+ documents — shared themes, key differences, unique points, Markdown export
- Temporal Identity: belief tracking, insight harvesting, Past Self queries, and generate-as-you drafting learned from your own writing voice
- Weekly Digest and an Analytics dashboard (usage, trends, indexing health) — computed locally from your own data

**Automation & power use**
- **Workflows**: on-demand multi-step AI pipelines built from five step types (AI Prompt, Document Lookup, Text Transform, Conditional Branch, Output Format) in a visual builder, with full run history
- Quick Actions, a command palette, remappable keyboard-first navigation, and a Quick Chat window
- Plugin system (sandboxed, manifest-validated) and an authenticated local REST API

**Data safety & sync**
- SQLCipher AES-256 encryption at rest; EF Core migrations; DPAPI-protected secrets
- **Backup & Restore** with AES-256-GCM encrypted archives
- **Collaborative Sync** between installations through an encrypted, file-based transport (any shared folder: OneDrive, Google Drive, NAS, USB) — no server, no cloud account
- Calendar & email connectors (Outlook, Google Workspace, CalDAV / IMAP / EWS) — read-only by default, OAuth tokens stored encrypted

**Platform**
- Six UI languages (en-US, de, es, fr, ja, zh-CN) — user guide, onboarding, and core pages localized, with coverage expanding release by release
- Dark, light, and high-contrast themes; WCAG-minded WinUI 3 design
- Android mobile companion (.NET MAUI) and a browser extension for capture on the go
- 29 navigation destinations, ~88 services, 2,773 unit tests, in-app onboarding and user guide

## Documentation

Full product, architecture, and developer documentation lives under [`docs/`](docs/README.md):

| Document | Description |
|---|---|
| [`docs/README.md`](docs/README.md) | Complete product documentation — features, install, build, configuration, architecture, data storage |
| [`docs/user-guide/getting-started/quick-start.md`](docs/user-guide/getting-started/quick-start.md) | 10-minute setup walkthrough |
| [`docs/user-guide/faq.md`](docs/user-guide/faq.md) | 100+ frequently asked questions |
| [`docs/user-guide/troubleshooting.md`](docs/user-guide/troubleshooting.md) | Solutions to common issues |
| [`docs/user-guide/glossary.md`](docs/user-guide/glossary.md) | 100+ term glossary |
| [`docs/user-guide/keyboard-shortcuts.md`](docs/user-guide/keyboard-shortcuts.md) | Power-user navigation |
| [`docs/user-guide/scenarios/README.md`](docs/user-guide/scenarios/README.md) | Real-world scenarios (5 use cases) |
| [`docs/user-guide/templates/README.md`](docs/user-guide/templates/README.md) | Document and chat templates |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | System architecture, component diagrams, startup sequence, data-layer design |
| [`docs/DEVELOPER-GUIDE.md`](docs/DEVELOPER-GUIDE.md) | Services, DI, migrations, error handling, extension points |
| [`docs/API-REFERENCE.md`](docs/API-REFERENCE.md) | Public API reference |
| [`docs/PLUGIN-DEVELOPMENT-GUIDE.md`](docs/PLUGIN-DEVELOPMENT-GUIDE.md) | Plugin development |
| [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md) | Release provenance: keyless cosign/Rekor attestation over SHA256SUMS |
| [`CHANGELOG.md`](CHANGELOG.md) | Keep-a-Changelog history across all releases |

AI-crawler indexes: [`docs/llms.txt`](docs/llms.txt) · [`docs/long-llms.txt`](docs/long-llms.txt)

**Release notes:** [v2.1.0 "Bedrock"](docs/v2.1.0-RELEASE-NOTES.md) · [v2.1.0-preview.1](docs/v2.1.0-preview.1-RELEASE-NOTES.md) · [v1.5.0](docs/v1.5.0-RELEASE-NOTES.md) · [v1.4.0](docs/v1.4.0-RELEASE-NOTES.md) · [v1.3.0](docs/v1.3.0-RELEASE-NOTES.md)

## Build from Source

```bash
git clone https://github.com/Git-Rocky-Stack/Agent-X.git
cd Agent-X

# The solution targets x64 explicitly — a bare `dotnet build` fails with a
# win-anycpu restore error. Always pass the platform:
dotnet build -c Release -p:Platform=x64

# Run the desktop app
dotnet run --project src/AgentX.App -c Release -p:Platform=x64

# Run the test suite (2,773 tests) and the coverage gate
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64
pwsh scripts/check-coverage.ps1 -ReportOnly
```

Full build instructions, installer packaging (SLIM / OFFLINE Inno Setup profiles), and runtime identifiers are in [`docs/README.md`](docs/README.md). CI (build + tests + coverage floors, `dotnet format`, locale parity audit) is documented in [`docs/CI.md`](docs/CI.md).

## Contributing

Issues and pull requests are welcome. Before submitting a PR:

1. `dotnet build -c Release -p:Platform=x64` — zero warnings expected
2. `dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64` — the full suite must stay green
3. `pwsh scripts/check-coverage.ps1` — coverage floors are a ratchet: they only go up
4. `dotnet format AgentX.sln` — CI verifies formatting
5. String changes must keep all six locales in key parity (CI's LocaleAudit enforces this)

## License

[MIT](LICENSE) © 2026 Rocky Elsalaymeh. Built by [Strategia-X](https://strategia-x.com).
