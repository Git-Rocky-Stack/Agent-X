# Agent-X

**Local-First AI-Powered Document Intelligence for Windows.** A native Windows desktop application that transforms your personal document collection into a queryable, AI-augmented knowledge base. No cloud, no telemetry, no internet dependency. Built on .NET 8.0 and WinUI 3, with support for Ollama (local), OpenAI, and Anthropic providers.

> **Current version:** [v2.1.0-preview.1](docs/v2.1.0-preview.1-RELEASE-NOTES.md) — "Bedrock" data-layer hardening (EF Core migration runner + SQLCipher at-rest encryption)
> **Platform:** Windows 10 build 19041+ (x64)
> **License:** Proprietary — Copyright (c) 2026 Rocky Stack. All rights reserved.

## Documentation

Full product, architecture, and developer documentation lives under `docs/`. Start with:

| Document | Description |
|---|---|
| [`docs/README.md`](docs/README.md) | Complete product documentation — features, install, build, configuration, architecture, data storage, keyboard shortcuts, license tiers |
| [`docs/user-guide/getting-started/quick-start.md`](docs/user-guide/getting-started/quick-start.md) | 10-minute setup walkthrough for new users |
| [`docs/user-guide/faq.md`](docs/user-guide/faq.md) | 100+ frequently asked questions |
| [`docs/user-guide/troubleshooting.md`](docs/user-guide/troubleshooting.md) | Solutions to common issues |
| [`docs/user-guide/glossary.md`](docs/user-guide/glossary.md) | 100+ term glossary |
| [`docs/user-guide/keyboard-shortcuts.md`](docs/user-guide/keyboard-shortcuts.md) | Power user navigation guide |
| [`docs/user-guide/scenarios/README.md`](docs/user-guide/scenarios/README.md) | Real-world scenarios (5 use cases) |
| [`docs/user-guide/templates/README.md`](docs/user-guide/templates/README.md) | Document and chat templates |
| [`docs/user-guide/video-scripts/README.md`](docs/user-guide/video-scripts/README.md) | Tutorial video scripts |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | System architecture, component diagrams, startup sequence, data-layer design |
| [`docs/DEVELOPER-GUIDE.md`](docs/DEVELOPER-GUIDE.md) | Developer-facing reference — services, DI, migrations, error handling, extension points |
| [`docs/API-REFERENCE.md`](docs/API-REFERENCE.md) | Public API reference |
| [`docs/SERVICE-REFERENCE.md`](docs/SERVICE-REFERENCE.md) | Internal service reference |
| [`docs/PLUGIN-DEVELOPMENT-GUIDE.md`](docs/PLUGIN-DEVELOPMENT-GUIDE.md) | Plugin development |
| [`CHANGELOG.md`](CHANGELOG.md) | Keep-a-Changelog history across all releases |

## Release Notes

- [v2.1.0-preview.1](docs/v2.1.0-preview.1-RELEASE-NOTES.md) — 2026-04-17 — Bedrock data-layer (B9 + C13)
- [v2.1.0](docs/v2.1.0-RELEASE-NOTES.md) — full v2.1 scope including deferred items
- [v1.5.0](docs/v1.5.0-RELEASE-NOTES.md) · [v1.4.0](docs/v1.4.0-RELEASE-NOTES.md) · [v1.3.0](docs/v1.3.0-RELEASE-NOTES.md)

---

## Documentation

Agent-X includes comprehensive documentation across 20+ files and 8,000+ lines:

### Getting Started
- **[Quick Start Guide](docs/user-guide/getting-started/quick-start.md)** — 10-minute setup walkthrough

### Reference
- **[FAQ](docs/user-guide/faq.md)** — 100+ frequently asked questions
- **[Troubleshooting](docs/user-guide/troubleshooting.md)** — Common issues and solutions
- **[Glossary](docs/user-guide/glossary.md)** — 100+ term reference
- **[Keyboard Shortcuts](docs/user-guide/keyboard-shortcuts.md)** — Power user navigation

### Real-World Scenarios
- **[Scenarios](docs/user-guide/scenarios/README.md)** — Research analysis, meeting intelligence, code review, document migration, personal knowledge base

### Templates
- **[Templates](docs/user-guide/templates/README.md)** — Document templates (project brief, meeting notes, research summary, technical spec) and chat templates

### Video Tutorials
- **[Video Scripts](docs/user-guide/video-scripts/README.md)** — Quick Start, Advanced RAG, Knowledge Graph, Workflows, GPU Acceleration

### AI Discovery
- **[llms.txt](docs/llms.txt)** — AI-optimized documentation index
- **[long-llms.txt](docs/long-llms.txt)** — Extended AI reference

---

## Build from source

```bash
dotnet build -c Release
dotnet run --project src/AgentX.App
dotnet test
```

Full build instructions, installer packaging, and runtime identifiers are in [`docs/README.md#build-from-source`](docs/README.md#build-from-source).
