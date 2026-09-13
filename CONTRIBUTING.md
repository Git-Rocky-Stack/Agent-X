# Contributing to Agent-X

Thanks for your interest. Agent-X is a native Windows desktop application: .NET 8 and
WinUI 3, with a MAUI Android companion and a browser extension alongside it. This file
covers how to build it, what CI will check, and the conventions the codebase follows.

## Prerequisites

- **Windows 10 version 2004 (build 19041) or newer.** The installer enforces this floor,
  and the app will not launch on older builds.
- **.NET 8 SDK 8.0.421 or newer.** Pinned in [`global.json`](global.json) with
  `rollForward: latestFeature`.
- **Windows App SDK / Windows App Runtime** for WinUI 3.
- **Visual Studio 2022** with the .NET Desktop and WinUI workloads, or the CLI alone.
- **Node 20** if you are touching `browser-extension/`.
- **The MAUI Android workload** (`dotnet workload install maui-android`) only if you are
  touching `src/AgentX.Mobile`.

## Building

```bash
dotnet build -p:Platform=x64
```

**The platform argument is required.** A bare `dotnet build` fails, because the solution
has no AnyCPU configuration. This trips up nearly everyone once.

## Running the tests

```bash
dotnet restore tests/AgentX.Tests/AgentX.Tests.csproj
dotnet build   tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-restore
dotnet test    tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Some tests drive a headless Chromium through Playwright. If those fail to start, install
the browser once with `pwsh tests/AgentX.Tests/bin/Release/net8.0-windows*/playwright.ps1 install chromium`.

**Do not pass `--no-build` after editing anything in `src/AgentX.App`.** You will test a
stale assembly and get a false green. This has burned us before.

## What CI will check

Six workflows gate every change. Run the relevant ones locally before opening a pull
request.

| Workflow | What it enforces |
| --- | --- |
| [`build-test.yml`](.github/workflows/build-test.yml) | Build plus the full unit suite on Windows x64, then the coverage gate |
| [`format.yml`](.github/workflows/format.yml) | `dotnet format --verify-no-changes` over the solution |
| [`locale-audit.yml`](.github/workflows/locale-audit.yml) | Translation key parity across all six locales, failing below 98% |
| [`dependency-audit.yml`](.github/workflows/dependency-audit.yml) | Vulnerable NuGet packages |
| [`extension-ci.yml`](.github/workflows/extension-ci.yml) | Lint, typecheck, build, and production `npm audit` for the extension |
| [`android-build.yml`](.github/workflows/android-build.yml) | The MAUI companion builds for `net8.0-android` |

### The coverage gate

[`scripts/check-coverage.ps1`](scripts/check-coverage.ps1) enforces a floor on authored
`AgentX.Core` code, with higher floors for namespaces judged critical. The floor only ever
moves up. If your change lands new coverage, ratchet the floor in the same commit and
record the measured figure in the comment beside it, the way every prior round did.

```bash
./scripts/check-coverage.ps1 -CoverageFile TestResults
```

### Localization

The UI is fully localized across `en-US`, `de`, `es`, `fr`, `ja`, and `zh-CN`. User-facing
strings belong in the `.resw` files and are referenced from XAML with `x:Uid`, never
hardcoded in a view or a view-model. A new string means a new key in all six locales, or
LocaleAudit fails.

## Conventions

- **Read [`DESIGN.md`](DESIGN.md) before any visual change.** It is the source of truth for
  the Command Console design system: color, type, spacing, depth, and lamp semantics.
  HighContrast is deliberately exempt from the hardware skin and stays bound to
  `SystemColor*` tokens.
- **Plain ASCII in documentation and code comments.** No em dashes, no decorative glyphs.
- **A feature is not done when it compiles.** It is done when it is reachable from an entry
  point. There are guard tests that fail on unwired views, undefined resources, and
  unreachable navigation targets, because shipping finished-but-unreachable code was a real
  defect class here.
- **A test that cannot fail is not a test.** Before relying on one, break the code it covers
  and confirm it goes red.

## Pull requests

- Branch from `main` and keep the branch focused on one change.
- Write the commit message so it explains why, not just what. The history here is used as
  documentation and the changelog is written from it.
- Update [`CHANGELOG.md`](CHANGELOG.md) under `## [Unreleased]` for anything user-facing.
  Anchor each claim to the code that implements it.
- Make sure the gates above pass.

## Reporting bugs

Open an issue with the version from **Settings**, your Windows build, and reproduction
steps. For anything security-related, follow [`SECURITY.md`](SECURITY.md) instead and do
not open a public issue.

## License

By contributing you agree that your contributions are licensed under the
[MIT License](LICENSE) that covers this project.
