# Contributing to Agent-X

Thanks for your interest. Agent-X is a native Windows desktop application: .NET 8 and
WinUI 3, with a MAUI Android companion, a browser extension and a sample plugin alongside
it. This file covers how to build it, what CI will check, and the conventions the codebase
follows.

## Prerequisites

- **Windows 10 version 2004 (build 19041) or newer.** The installer enforces this floor,
  and the app will not launch on older builds.
- **.NET 8 SDK 8.0.421 or a later 8.0 SDK.** [`global.json`](global.json) pins 8.0.421
  with `rollForward: latestFeature`.
- **No separate Windows App SDK install.** The app references the `Microsoft.WindowsAppSDK`
  1.6 NuGet package and builds self-contained (`WindowsAppSDKSelfContained`), so a restore
  brings everything WinUI 3 needs.
- **Visual Studio 2022** with the .NET desktop and WinUI workloads, or the `dotnet` CLI
  alone. CI uses the CLI only.
- **Node 20** if you are touching `browser-extension/`.
- **The MAUI Android workload** (`dotnet workload install maui-android`) only if you are
  touching `src/AgentX.Mobile`.

## Building

```bash
dotnet build -p:Platform=x64
```

**The platform argument is required.** A bare `dotnet build` fails: the WinUI app project
supports only x86, x64 and ARM64 and derives its runtime identifier from the platform, so
Any CPU asks for a `win-anycpu` runtime that does not exist. This trips up nearly everyone
once.

From the repository root this builds `AgentX.sln`: the app (`src/AgentX.App`), the core
library (`src/AgentX.Core`), the tests (`tests/AgentX.Tests`) and the LocaleAudit tool and
its tests. The Android companion (`src/AgentX.Mobile`) and the sample plugin
(`plugins/sample-plugin`) are separate projects outside the solution.

## Running the tests

The same three steps CI runs:

```bash
dotnet restore tests/AgentX.Tests/AgentX.Tests.csproj
dotnet build   tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-restore
dotnet test    tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-build
```

Some tests drive a headless Chromium through Playwright; without the browser they are
skipped. To run them, install Chromium once after a build (PowerShell):

```powershell
& (Get-ChildItem tests/AgentX.Tests/bin -Recurse -Filter playwright.ps1 | Select-Object -First 1).FullName install chromium
```

**Do not pass `--no-build` after editing anything in `src/AgentX.App`.** The test project
compiles a subset of the app's sources (view models and pure services) into its own
assembly, so a run without a rebuild tests stale code and gives a false green. This has
burned us before.

### Structural (CodeQuality) tests

`tests/AgentX.Tests/CodeQuality` holds tests that read the source, XAML and scripts rather
than running features. They catch defects the compiler and the unit tests cannot see: a
button with no Click handler or Command, a `[RelayCommand]` nothing binds, a XAML resource
key nothing defines, a keyed style nothing uses, a document processor missing from the
composition root, an import picker offering a format no processor reads, an interactive
control a screen reader cannot name, spacing off the 4-pixel grid, radius values outside
the machined stops, banned palette hues, and release scripts that no longer match the app
and the installer. They run in the full suite; to run only them:

```bash
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~AgentX.Tests.CodeQuality"
```

## What CI will check

Six workflows gate pull requests and pushes to `main`. Each one runs when a change touches
its paths (listed in the workflow file); LocaleAudit runs on every push to `main`, and
Dependency Audit also runs weekly. Run the relevant checks locally before opening a pull
request.

| Workflow | Job | What it enforces |
| --- | --- | --- |
| [`build-test.yml`](.github/workflows/build-test.yml) | Build + unit tests (Windows, x64) | Builds `AgentX.Tests` and the WinUI app (`src/AgentX.App`) in Release x64, installs Playwright Chromium, runs the full test suite with coverage collection, then the coverage gate |
| [`format.yml`](.github/workflows/format.yml) | dotnet format (whitespace + imports) | `dotnet format AgentX.sln --verify-no-changes`: whitespace, LF line endings and using-directive order |
| [`locale-audit.yml`](.github/workflows/locale-audit.yml) | Locale coverage >= 98% | Runs the LocaleAudit tool and fails if any of the six locales covers less than 98% of the keys referenced by XAML `x:Uid` and C# `GetString` |
| | Locale snapshot tests | Runs `tests/LocaleAudit.Tests`: identical key sets in all six locales, no blank values, no orphan entries, the 98% floor, and the translation-mirror guard (no key in `scripts/translations/*.json` that the resw files no longer define) |
| [`dependency-audit.yml`](.github/workflows/dependency-audit.yml) | NuGet vulnerable packages | Fails on any High or Critical advisory in `AgentX.sln` or the sample plugin (`plugins/sample-plugin/SamplePlugin.csproj`); the accepted-advisory list is empty |
| | NuGet vulnerable packages (AgentX.Mobile) | The same scan for the Android companion, on Linux with the `maui-android` workload |
| [`extension-ci.yml`](.github/workflows/extension-ci.yml) | Lint, typecheck, build, audit | `npm ci`, `npm run lint`, `npm run typecheck`, `npm run build` (production) and `npm audit --audit-level=high` over the whole dependency tree |
| [`android-build.yml`](.github/workflows/android-build.yml) | Build AgentX.Mobile (net8.0-android) | `dotnet build src/AgentX.Mobile/AgentX.Mobile.csproj -c Release` on Linux with the `maui-android` workload |

A seventh workflow, [`release-provenance.yml`](.github/workflows/release-provenance.yml), is
not a pull-request gate: it signs a published release's `SHA256SUMS.txt` with keyless
cosign. See [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md).

[`docs/CI.md`](docs/CI.md) describes every gate in more detail.

### The coverage gate

[`scripts/check-coverage.ps1`](scripts/check-coverage.ps1) enforces a floor on authored
`AgentX.Core` code (65% line, 55% branch today), with higher floors for five critical
namespaces: Security, Privacy, OAuth, Backup and the migration runner. The floors only ever
move up. If your change lands new coverage, ratchet the floor in the same commit and record
the measured figure in the comment beside it, the way every prior round did. Never lower a
floor to turn a red build green; add tests.

To run the gate locally the way CI does:

```bash
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --configuration Release -p:Platform=x64 \
  --results-directory TestResults \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings
pwsh scripts/check-coverage.ps1 -CoverageFile TestResults
```

Add `-ReportOnly` to the script to print the table without failing.

### Formatting

CI runs the formatter with `Platform=x64` set in the environment, because the WinUI project
only loads its project graph when a platform is set. Locally, in PowerShell:

```powershell
$env:Platform = 'x64'
dotnet format AgentX.sln --verify-no-changes
```

Drop `--verify-no-changes` to apply the fixes, then commit the result.

### Localization

The UI is localized in `en-US`, `de`, `es`, `fr`, `ja`, and `zh-CN`. User-facing strings
belong in `src/AgentX.App/Strings/<locale>/Resources.resw` and are referenced from XAML with
`x:Uid` or from code through the localization service, never hardcoded in a view or a view
model. A new string means a new key, with a real translation, in all six resw files, or the
snapshot tests fail. When you remove a key, also remove it from the
`scripts/translations/<locale>.json` mirrors. To run both locale checks locally:

```bash
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -c Release -- \
  src/AgentX.App src src/AgentX.App/Strings --output locale-audit-report.json --fail-below 98
dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj -c Release
```

### Browser extension and Android companion

```bash
cd browser-extension
npm ci
npm run lint
npm run typecheck
npm run build
npm audit --audit-level=high
```

The Android companion builds with `dotnet build src/AgentX.Mobile/AgentX.Mobile.csproj -c Release`
after `dotnet workload install maui-android`. CI builds it on Linux, where the project
targets `net8.0-android` only; on Windows and macOS it also targets `net8.0-ios`, which needs
the iOS workload.

## Conventions

- **Read [`DESIGN.md`](DESIGN.md) before any visual change.** It is the source of truth for
  the Command Console design system: color, type, spacing, depth, and lamp semantics.
  HighContrast is deliberately exempt from the hardware skin and stays bound to
  `SystemColor*` tokens.
- **Plain ASCII in documentation and code comments.** No em dashes, no decorative glyphs.
- **A feature is not done when it compiles.** It is done when it is reachable from an entry
  point. The CodeQuality tests above fail on unwired controls, unbound commands, undefined
  resources and unregistered processors, because shipping finished-but-unreachable code was
  a real defect class here.
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

Open an issue with the version shown at the bottom of the **Settings** page (for example
`Agent-X v2.2.0`), your Windows build, and reproduction steps. The log files in
`%LOCALAPPDATA%\AgentX\Logs` (one file per day, the last seven kept) usually show what
went wrong. For anything security-related, follow [`SECURITY.md`](SECURITY.md) instead and
do not open a public issue.

## License

By contributing you agree that your contributions are licensed under the
[MIT License](LICENSE) that covers this project.
