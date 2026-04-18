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
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | System architecture, component diagrams, startup sequence, data-layer design |
| [`docs/DEVELOPER-GUIDE.md`](docs/DEVELOPER-GUIDE.md) | Developer-facing reference — services, DI, migrations, error handling, extension points |
| [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md) | End-user guide — importing documents, search, chat, encryption toggle, settings |
| [`docs/API-REFERENCE.md`](docs/API-REFERENCE.md) | Public API reference |
| [`docs/SERVICE-REFERENCE.md`](docs/SERVICE-REFERENCE.md) | Internal service reference |
| [`docs/PLUGIN-DEVELOPMENT-GUIDE.md`](docs/PLUGIN-DEVELOPMENT-GUIDE.md) | Plugin development |
| [`CHANGELOG.md`](CHANGELOG.md) | Keep-a-Changelog history across all releases |

## Release Notes

- [v2.1.0-preview.1](docs/v2.1.0-preview.1-RELEASE-NOTES.md) — 2026-04-17 — Bedrock data-layer (B9 + C13)
- [v2.1.0](docs/v2.1.0-RELEASE-NOTES.md) — full v2.1 scope including deferred items
- [v1.5.0](docs/v1.5.0-RELEASE-NOTES.md) · [v1.4.0](docs/v1.4.0-RELEASE-NOTES.md) · [v1.3.0](docs/v1.3.0-RELEASE-NOTES.md)

## Build from source

```bash
dotnet build -c Release
dotnet run --project src/AgentX.App
dotnet test
```

Full build instructions, installer packaging, and runtime identifiers are in [`docs/README.md#build-from-source`](docs/README.md#build-from-source).
