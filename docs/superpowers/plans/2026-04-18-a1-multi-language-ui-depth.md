# A1 — Multi-Language UI Depth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Elevate Agent-X's 6-locale stub localization (de / en-US / es / fr / ja / zh-CN) to production depth — achieving ≥98% string coverage per locale, CLDR-correct pluralization, RTL-safe FlowDirection for future ar-SA / he-IL locales, per-page locale QA, and a CI gate that blocks PRs introducing untranslated `x:Uid` references.

**Architecture:** Three pillars. **(1) Coverage tooling** — a new `tools/LocaleAudit/` console tool parses every `*.xaml` file in `src/AgentX.App/`, extracts `x:Uid` references and their bound property targets (`.Text`, `.Content`, `.Header`, `.PlaceholderText`), then cross-checks against each `Strings/<locale>/Resources.resw`. It emits per-locale coverage percentages plus a diff of missing keys to `tools/LocaleAudit/audit-report.json`. **(2) Pluralization** — `ILocalizationService.FormatPlural(key, count, args)` uses `System.Globalization.PluralRules` (.NET 8 built-in) to select CLDR plural category (`zero` / `one` / `two` / `few` / `many` / `other`) and maps to a `Key_<category>` resw convention. **(3) RTL safety** — root `FlowDirection` binds to `CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft`; a `PseudoLocale` test fixture flips direction without requiring a real ar-SA resw bundle. A GitHub Actions CI gate runs LocaleAudit.Tool on every PR and fails the build if (a) global coverage < 98% across any locale, or (b) any `x:Uid` added in the diff lacks entries in ≥5 of 6 locales.

**Tech Stack:** .NET 8 (console tool + `System.Globalization.PluralRules`), WinUI 3 `Resources.resw` + XAML `x:Uid`, `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader`, CommunityToolkit.Mvvm, GitHub Actions (existing `ci.yml`), xUnit + FluentAssertions (existing).

**Prerequisites:** None from other A/B/C items. This plan may run in parallel with A2 (keyboard power mode) and B1–B7 (monolith splits). Must complete before v2.1.0 final ships.

**Target release:** v2.1.0 (final).

---

## ⚠️ Post-Spike Revision Required (2026-04-18)

Spikes 0–3 completed 2026-04-18 returned findings that reshape the A1 plan's scope and tool design. Key shifts:

### Revision 1 — Scope is 10–100× smaller than the plan assumed
The plan presumed "hundreds" of `x:Uid` references. Reality: **24 unique `x:Uid` values** across **2 files only** (`Views/SettingsPage.xaml` — 7 uids, `Views/PluginManagerPage.xaml` — 17 uids). Every other page uses code-driven `GetString(key)` calls instead of XAML `x:Uid`. The tool must scan BOTH.

### Revision 2 — All 24 `x:Uid` values are 100% unlocalized
Not "shallow coverage" — **zero coverage**. No `Resources.resw` in any of the 6 locales contains a matching entry for any of the 24 uids. Task 6 (en-US backfill) must add all 24 × 1 = 24 canonical entries; Task 7 must add 24 × 5 = 120 translated entries before the 98% gate is achievable for `x:Uid`-derived coverage.

### Revision 3 — 139 orphan keys in every resw
Every locale has **163 resw entries** but only 24 `x:Uid` references in XAML — so there are 139 entries per locale that have no matching XAML `x:Uid`. Sample orphan prefix: `Nav_*` (15 keys — `Nav_Dashboard`, `Nav_Chat`, etc.), which **violate the `<Uid>.<Property>` convention** (no dot). These are called directly from C# code via `GetString("Nav_Dashboard")`. **The audit tool must also scan C# source for `GetString("<key>")` and `GetString($"<key>")` invocations** to discover code-bound keys. Coverage must be measured as `(xml_uids ∪ code_keys)` vs `resw_keys`, not XAML-only.

### Revision 4 — `ILocalizationService` exists cleanly (no refactor needed)
Interface at `src/AgentX.Core/Services/Localization/ILocalizationService.cs`, implementation at `src/AgentX.App/Services/LocalizationService.cs`. Public surface: `CurrentLanguage`, `SupportedLanguages`, `SetLanguageAsync`, `GetString(key)`, `GetString(key, args)`. **No pluralization support today** — Task 8's `FormatPlural` addition is clean (no legacy conflicts). Culture source: `ApplicationLanguages.PrimaryLanguageOverride` + `LanguageOverride` from `AppSettingsExtended`.

### Revision 5 — Fallback behavior = literal key (bad UX, makes CI gate essential)
`LocalizationService.cs:107–126` returns the resource key itself (e.g., literal string `"Plugin_Manager"`) when the locale has no entry. The user sees raw keys in the UI — this is the visible-failure scenario. The CI gate at 98% threshold is therefore **load-bearing**, not advisory.

### Revision 6 — `.resw` schema specifics (apply to Task 2 parser)
- Root element: `<root>`
- Namespaces: `xsd` (W3C schema) + `msdata` (Microsoft data set)
- **Zero `<resheader>` entries** (unusual — tool must not require them)
- **Zero `<comment>` elements** today (translator notes absent — but parser should preserve them if added in future)
- All 6 locales use identical XML structure

### Plan tasks affected
- **Task 2 (`XamlUidExtractor`)** — keep as-is; scope covers XAML only, correct.
- **Task 3 (`ReswReader`)** — keep as-is; schema confirmed.
- **Task 4 (`CoverageReport`)** — REVISED: must merge XAML `x:Uid` set with C# `GetString()` key set before computing coverage. Add a new `CSharpGetStringExtractor` type (Task 2.5, inserted below).
- **NEW Task 2.5 (`CSharpGetStringExtractor`)** — parse C# source for `GetString("<key>")` / `GetString($"<key>")` / `_localization.GetString(...)` invocations; emit a `CodeKeyReference` list matching `UidReference` shape. MUST be added before Task 4.
- **Task 6 (en-US backfill)** — 24 new canonical entries required (list provided in Spike 0 findings below).
- **Task 7 (other-locale backfill)** — 24 × 5 = 120 translated entries minimum.
- **Task 11 (snapshot test)** — revised to iterate both XAML uids AND C# code keys.
- **Task 12 (CI gate)** — no change.
- **All other tasks** — unchanged.

**Decisions resolved by Rocky (2026-04-18):**

1. ✅ **Task 2.5 `CSharpGetStringExtractor` — ADDED** (decision: yes). Without it, the audit tool is blind to 85% of real localization keys (all `Nav_*` and every page that skips XAML `x:Uid` in favor of code-driven `GetString`).
2. ✅ **Orphan-key policy — TRIAGE VIA EXTRACTOR** (controller judgment per Rocky's "best judgement" directive). Once Task 2.5 catches code-bound keys, the union `(xaml_uids ∪ code_keys)` eliminates the apparent 139-orphan count. Any key still orphaned after that union is flagged as genuinely dead and queued for deletion in a follow-up cleanup task (not scope for v2.1.0 final — tool only surfaces them).
3. ✅ **Backfill scope — IN-SESSION with machine-translated drafts flagged for review** (controller judgment). Task 6 ships 24 canonical en-US entries authoritatively. Task 7 produces machine-translated drafts for de / es / fr / ja / zh-CN with inline `<!-- MACHINE_TRANSLATED: <en-US source> -->` review markers. Rocky reviews each locale at implementation time; infrastructure is ready for a professional translator engagement if he later wants human-sourced translations.

Plan tasks below are unfrozen and revised in place for each decision.

---

## Pre-Implementation Spikes (REQUIRED — run first)

Every spike records a concrete finding. If a spike invalidates any implementation task below, the plan is revised **in place** and committed before the first implementation task starts.

### Spike 0 — Inventory existing `x:Uid` usage and baseline coverage

**Question:** How many `x:Uid` references exist in `src/AgentX.App/`, spread across which files? What is the current coverage per locale?

- [ ] Run: `cd 'C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X' && grep -rohE 'x:Uid="[^"]+"' src/AgentX.App --include='*.xaml' | sort -u | wc -l` — record total unique `x:Uid` count.
- [ ] Run: `ls src/AgentX.App/Strings/` — confirm locale folders present (expect: `de`, `en-US`, `es`, `fr`, `ja`, `zh-CN`).
- [ ] For each locale, run: `grep -c '<data ' src/AgentX.App/Strings/<locale>/Resources.resw` — record entry count per locale.
- [ ] Compute rough baseline coverage: `entries(locale) / total_unique_uids * 100`. Expect per magnum opus: "only 1 `Resources.resw` per locale — depth coverage suspect".

**Spike Findings (2026-04-18):**
- **Total unique `x:Uid` references: 24** (114 total references before dedupe — avg 4.75 refs per uid)
- **XAML files containing `x:Uid`: 2** — both in `src/AgentX.App/Views/`
  - `Views/SettingsPage.xaml` — 7 unique uids
  - `Views/PluginManagerPage.xaml` — 17 unique uids
- **Per-locale resw entry counts**: de: 163 / en-US: 163 / es: 163 / fr: 163 / ja: 163 / zh-CN: 163 (identical counts — unusual symmetry)
- **Baseline coverage** (naive `entries/uids × 100`): all locales score 679% — indicating **139 orphan keys per locale** with no matching XAML uid (see Revision 3 above — these are likely C# code-bound keys, confirmed by orphan-prefix `Nav_*`)
- **Largest gap locale (among x:Uid-derived coverage)**: TIE — all 6 locales have **0% x:Uid coverage**. Every `x:Uid` reference is fully unlocalized.
- **Property suffixes present in resw**: `.Text`, `.ToolTip`, `.OnContent`, `.OffContent` (4 suffixes — parser must recognize all four)
- **24 completely-unlocalized x:Uids (zero entries in any locale)**: 22 from `PluginManagerPage.xaml` (`Plugin_Active`, `Plugin_Configuration`, `Plugin_Manager`, `Plugin_NoPlugins`, `Plugin_Refresh`, `Plugin_SelectPlugin`, `Plugin_Install`, `Plugin_Installed`, `Plugin_Uninstall`, + 13 more) + 2 from `SettingsPage.xaml` (`Encryption_SectionHeader`, `Encryption_Description`)

### Spike 1 — Inventory `ILocalizationService` and existing pluralization support

**Question:** Does `ILocalizationService` already exist? What's its current surface? Does it handle pluralization?

- [ ] Run: `grep -rn 'ILocalizationService\|LocalizationService' src/AgentX.Core --include='*.cs' src/AgentX.App --include='*.cs' | head -30` — locate existing service.
- [ ] Read the interface file and document: methods available, fallback behavior, culture source (`CultureInfo.CurrentUICulture`?), any existing `FormatPlural` or `GetString(key, count)` method.
- [ ] If no `ILocalizationService` exists, Task 6 is revised to also create the interface. If one exists, Task 6 only adds the `FormatPlural` method.
- [ ] Grep for existing usages of `"{0}"` / `string.Format` in resw values or C# calls — these may need pluralization migration.

**Spike Findings (2026-04-18):**
- **`ILocalizationService` exists: YES** at `src/AgentX.Core/Services/Localization/ILocalizationService.cs`
- **Implementation**: `src/AgentX.App/Services/LocalizationService.cs`
- **Current public methods**:
  - `string CurrentLanguage { get; }` — read-only current locale code
  - `IReadOnlyList<LanguageOption> SupportedLanguages { get; }` — all 6 supported locales
  - `Task SetLanguageAsync(string? languageCode)` — language override + settings persistence
  - `string GetString(string resourceKey)` — single-key lookup
  - `string GetString(string resourceKey, params object[] args)` — key + format args
- **Culture source**: `ApplicationLanguages.PrimaryLanguageOverride` (WinUI 3 API) + `LanguageOverride` field from `AppSettingsExtended` (user-configured), falling back to OS locale
- **Pluralization support today**: **NONE** — no `FormatPlural`, no `_one`/`_other` convention anywhere. Task 8 is a clean greenfield addition.
- **Existing `{0}` format placeholders** in resw: one observed — `Search_ResultCount` (not plural-aware today). Candidate for migration to `FormatPlural` in Task 8 integration step.
- **Task 8 revision needed**: No — plan adds `FormatPlural` alongside existing `GetString` methods, both coexist cleanly.

### Spike 2 — Confirm WinUI 3 `ResourceLoader` fallback behavior

**Question:** If `Strings/fr/Resources.resw` is missing key `MyButton.Text`, does WinUI 3 fall back to `Strings/en-US/Resources.resw`? Or does it render the `x:Uid` literally, or throw?

- [ ] Temporary spike: rename a single `fr/Resources.resw` key to verify behavior (or inspect the `Microsoft.Windows.ApplicationModel.Resources.ResourceManager` documentation reference loaded via context7).
- [ ] Record fallback behavior.
- [ ] If fallback = literal-Uid (common for resw resources targeting `.Properties`), CI gate is more critical — UI breaks visibly. If fallback = en-US, the gate can be a soft warning at lower coverage.

**Spike Findings (2026-04-18):**
- **Fallback behavior observed**: `LocalizationService.cs:107–126` — on missing key, returns the **literal key string** (e.g., user sees `"Plugin_Manager"` rendered as-is on the button). NOT en-US fallback, NOT an exception, NOT an empty string. The raw resource-key text leaks to the UI.
- **Init-time degradation**: `Log.Warning` on `ResourceLoader` init failure, then continues — misconfigured locales do not crash the app but do degrade silently.
- **Implication for CI-gate strictness**: **CI gate is load-bearing** — since missing keys produce visible UI failures (raw key text), coverage regressions ship broken UX. The 98% threshold must be enforced strictly (no advisory / warning-only mode for the initial release).

### Spike 3 — Confirm the `Resources.resw` XML schema

**Question:** Task 1's parser reads `.resw` as XML. Confirm the exact schema so the parser extracts `name="<uid>.<property>"` correctly.

- [ ] Open `src/AgentX.App/Strings/en-US/Resources.resw` and record the root element, namespace, and typical `<data>` entry shape.
- [ ] Note whether `<comment>` elements exist (translator notes) — the parser should preserve them.
- [ ] Confirm `<data name>` uses the `<Uid>.<Property>` convention consistently (e.g., `MyButton.Content`, `TitleText.Text`).

**Spike Findings (2026-04-18):**
- **Root element**: `<root>` (XML 1.0, UTF-8 encoding)
- **Namespaces**: `xsd` (W3C schema), `msdata` (Microsoft data set)
- **`<resheader>` entries**: **0** (unusual — standard .resx files typically have 2 resheader entries for version + reader type; Agent-X has zero. Parser must NOT require them.)
- **Comment elements**: **0** — no translator notes today. Parser should preserve `<comment>` elements if added in future releases.
- **Typical `<data>` entry shape**: `<data name="Nav_Dashboard" xml:space="preserve"><value>Dashboard</value></data>` — single-line, compact (not the multi-line form).
- **Naming convention consistency**: **MIXED** — of 163 entries:
  - ~25 follow `<Uid>.<Property>` convention (e.g., `Encryption_Toggle.Text`, `AboutLink.ToolTip`)
  - ~15 are flat `Nav_*` keys (`Nav_Dashboard`, `Nav_Chat`, etc.) WITHOUT a property suffix — called from C# code, not XAML
  - Remaining ~123 are a mix of flat code-keys and dotted property keys
- **Non-conforming entries (sample)**: `Nav_Dashboard`, `Nav_Chat`, `Nav_Documents`, `Nav_Settings`, `Nav_Collections` — 15 navigation keys total, all flat (no dot). The audit tool's algorithm must treat these as "C#-code keys" (scanned via the new `CSharpGetStringExtractor` in revised Task 2.5), NOT as XAML uids.
- All 6 locale resw files use identical XML structure and encoding.

### Spike Closure

Before starting Task 1:
- [ ] All 4 spike findings recorded above
- [ ] Plan tasks revised in place for every finding that changes implementation
- [ ] Commit the revised plan with message: `docs(plans): revise A1 plan after pre-implementation spikes`
- [ ] Only then begin Task 1.

---

## File Structure

**Create:**
- `tools/LocaleAudit/LocaleAudit.Tool.csproj` — console app csproj
- `tools/LocaleAudit/Program.cs` — entry point
- `tools/LocaleAudit/XamlUidExtractor.cs` — XAML parser (x:Uid references)
- `tools/LocaleAudit/CSharpGetStringExtractor.cs` — C# source parser (`GetString("key")` invocations) — **added per Decision 1**
- `tools/LocaleAudit/ReswReader.cs` — `.resw` parser
- `tools/LocaleAudit/CoverageReport.cs` — report DTO + writer (consumes the union of both extractors)
- `tools/LocaleAudit/baseline.json` — captured baseline after Task 5
- `tools/LocaleAudit/README.md` — tool usage docs
- `tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj`
- `tests/LocaleAudit.Tests/XamlUidExtractorTests.cs`
- `tests/LocaleAudit.Tests/CSharpGetStringExtractorTests.cs` — **added**
- `tests/LocaleAudit.Tests/ReswReaderTests.cs`
- `tests/LocaleAudit.Tests/CoverageReportTests.cs`
- `src/AgentX.Core/Services/Localization/IPluralRuleProvider.cs`
- `src/AgentX.Core/Services/Localization/CldrPluralRuleProvider.cs`
- `tests/AgentX.Tests/Services/Localization/CldrPluralRuleProviderTests.cs`
- `src/AgentX.App/Helpers/FlowDirectionHelper.cs`
- `tests/AgentX.Tests/Services/Localization/PseudoLocaleFlowDirectionTests.cs`
- `tests/AgentX.Tests/Services/Localization/PerPageLocaleSnapshotTests.cs`
- `.github/workflows/locale-audit.yml` — CI gate job

**Modify:**
- `src/AgentX.Core/Services/Localization/ILocalizationService.cs` — add `FormatPlural`
- `src/AgentX.Core/Services/Localization/LocalizationService.cs` — implement `FormatPlural`
- `src/AgentX.App/Strings/en-US/Resources.resw` — add all missing canonical strings
- `src/AgentX.App/Strings/de/Resources.resw` — backfill de translations
- `src/AgentX.App/Strings/es/Resources.resw` — backfill es translations
- `src/AgentX.App/Strings/fr/Resources.resw` — backfill fr translations
- `src/AgentX.App/Strings/ja/Resources.resw` — backfill ja translations
- `src/AgentX.App/Strings/zh-CN/Resources.resw` — backfill zh-CN translations
- `src/AgentX.App/MainWindow.xaml` — wire `FlowDirection` binding at root Grid
- `src/AgentX.App/MainWindow.xaml.cs` — hook `FlowDirectionHelper` on startup + culture change
- `AgentX.sln` — add `LocaleAudit.Tool` project + `LocaleAudit.Tests` project
- `docs/ARCHITECTURE.md` — new "Localization (A1)" section
- `docs/USER-GUIDE.md` — new "Language selection" section
- `docs/DEVELOPER-GUIDE.md` — new "Adding localized strings" workflow
- `docs/v2.1.0-RELEASE-NOTES.md` — A1 feature entry

---

### Task 1: Create `LocaleAudit.Tool` project scaffold

**Files:**
- Create: `tools/LocaleAudit/LocaleAudit.Tool.csproj`
- Create: `tools/LocaleAudit/Program.cs`
- Modify: `AgentX.sln` — add project reference

- [ ] **Step 1: Create csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- TEMPORARY during Tasks 1-4: must be Library because Program.cs is excluded -->
    <!-- (Exe target requires Main, and excluding Program.cs removes it). -->
    <!-- Task 4 Step 5 restores this to Exe. -->
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>LocaleAudit</RootNamespace>
    <AssemblyName>LocaleAudit.Tool</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Text.Json" Version="8.0.5" />
  </ItemGroup>
  <!-- TEMPORARY during Tasks 1-4: Program.cs references types (XamlUidExtractor, -->
  <!-- CSharpGetStringExtractor, ReswReader, CoverageReport) that don't exist yet. -->
  <!-- Excluding it lets the test project's ProjectReference compile cleanly so -->
  <!-- tests can run red/green as each type lands. Task 4 Step 5 removes this block. -->
  <ItemGroup>
    <Compile Remove="Program.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write minimal `Program.cs` entry point**

Note: per Decision 1, the tool consumes BOTH XAML `x:Uid` references AND C# `GetString("key")` invocations. Both extractors feed into a unified `CoverageReport.Build`.

```csharp
using LocaleAudit;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: LocaleAudit.Tool <app-xaml-root> <app-csharp-root> <strings-root> [--output report.json] [--fail-below 98]");
    return 2;
}

var xamlRoot = args[0];
var csharpRoot = args[1];
var stringsRoot = args[2];
var outputPath = ParseArg(args, "--output") ?? "audit-report.json";
var failBelow = double.TryParse(ParseArg(args, "--fail-below"), out var t) ? t : 98.0;

try
{
    var xamlUids = XamlUidExtractor.ExtractAll(xamlRoot);
    var codeKeys = CSharpGetStringExtractor.ExtractAll(csharpRoot);
    var locales = ReswReader.ReadAllLocales(stringsRoot);
    var report = CoverageReport.Build(xamlUids, codeKeys, locales);
    CoverageReport.WriteJson(report, outputPath);
    CoverageReport.PrintSummary(report, Console.Out);

    return report.ShouldFail(failBelow) ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"LocaleAudit failed: {ex.Message}");
    return 3;
}

static string? ParseArg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}
```

- [ ] **Step 3: Add to solution**

```bash
dotnet sln AgentX.sln add tools/LocaleAudit/LocaleAudit.Tool.csproj
```

- [ ] **Step 4: Build — expect compile fails** (missing types `XamlUidExtractor`, `CSharpGetStringExtractor`, `ReswReader`, `CoverageReport`)

```bash
dotnet build tools/LocaleAudit/LocaleAudit.Tool.csproj
```

Expected immediately after Task 1: **1 `CS0246` error** on `using LocaleAudit;` — Roslyn short-circuits when the namespace has zero defined types. This is the correct TDD-gate signal (build must fail; any missing-dependency error satisfies that). As Tasks 2, 2.5, 3, and 4 land types into the `LocaleAudit` namespace, the `using` resolves and the error set transitions to the 4 individual `CS0103: The name '...' does not exist` errors (progressively shrinking as each type is added). Task 4 Step 4 brings the error count to zero.

- [ ] **Step 5: Commit**

```bash
git add tools/LocaleAudit/LocaleAudit.Tool.csproj tools/LocaleAudit/Program.cs AgentX.sln
git commit -m "feat(a1): scaffold LocaleAudit.Tool console project"
```

---

### Task 2: `XamlUidExtractor` — parse `x:Uid` from XAML

**Files:**
- Create: `tools/LocaleAudit/XamlUidExtractor.cs`
- Create: `tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj`
- Create: `tests/LocaleAudit.Tests/XamlUidExtractorTests.cs`

- [ ] **Step 1: Create test project csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\tools\LocaleAudit\LocaleAudit.Tool.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write failing tests**

```csharp
using System.IO;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

public class XamlUidExtractorTests
{
    [Fact]
    public void Extract_finds_single_uid_with_text_property()
    {
        var xaml = """<Button x:Uid="MyButton" />""";
        var tmp = WriteTempXaml(xaml);

        var result = XamlUidExtractor.ExtractFromFile(tmp);

        result.Should().ContainSingle().Which.Uid.Should().Be("MyButton");
        File.Delete(tmp);
    }

    [Fact]
    public void Extract_handles_multiple_uids_in_one_file()
    {
        var xaml = """
            <StackPanel>
                <Button x:Uid="BtnOk" />
                <Button x:Uid="BtnCancel" />
                <TextBlock x:Uid="LblStatus" />
            </StackPanel>
            """;
        var tmp = WriteTempXaml(xaml);

        var result = XamlUidExtractor.ExtractFromFile(tmp);

        result.Select(u => u.Uid).Should().BeEquivalentTo("BtnOk", "BtnCancel", "LblStatus");
        File.Delete(tmp);
    }

    [Fact]
    public void ExtractAll_recurses_subdirectories()
    {
        var tmpRoot = Directory.CreateTempSubdirectory("locale-audit-test").FullName;
        var sub = Path.Combine(tmpRoot, "Views");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(tmpRoot, "Root.xaml"), """<Button x:Uid="Root" />""");
        File.WriteAllText(Path.Combine(sub, "Nested.xaml"), """<Button x:Uid="Nested" />""");

        var result = XamlUidExtractor.ExtractAll(tmpRoot);

        result.Select(u => u.Uid).Should().Contain(new[] { "Root", "Nested" });
        Directory.Delete(tmpRoot, recursive: true);
    }

    [Fact]
    public void Extract_ignores_commented_out_uids()
    {
        var xaml = """
            <StackPanel>
                <!-- <Button x:Uid="OldButton" /> -->
                <Button x:Uid="NewButton" />
            </StackPanel>
            """;
        var tmp = WriteTempXaml(xaml);

        var result = XamlUidExtractor.ExtractFromFile(tmp);

        result.Select(u => u.Uid).Should().BeEquivalentTo("NewButton");
        File.Delete(tmp);
    }

    [Fact]
    public void Extract_records_source_file_path()
    {
        var xaml = """<Button x:Uid="MyButton" />""";
        var tmp = WriteTempXaml(xaml);

        var result = XamlUidExtractor.ExtractFromFile(tmp);

        result.Single().SourceFile.Should().EndWith(Path.GetFileName(tmp));
        File.Delete(tmp);
    }

    private static string WriteTempXaml(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"locale-audit-{Guid.NewGuid():N}.xaml");
        var wrapped = $"""
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                {content}
            </Page>
            """;
        File.WriteAllText(path, wrapped);
        return path;
    }
}
```

- [ ] **Step 3: Run — expect compile fail**

```bash
dotnet sln AgentX.sln add tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj
dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj
```

Expected: `XamlUidExtractor` type missing.

- [ ] **Step 4: Write the extractor**

```csharp
using System.Text.RegularExpressions;

namespace LocaleAudit;

public sealed record UidReference(string Uid, string SourceFile, int LineNumber);

public static class XamlUidExtractor
{
    // Captures x:Uid="SomeValue"; tolerates single or double quotes.
    private static readonly Regex UidRegex = new(
        @"x:Uid\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled);

    // Strip XAML comments before matching so commented-out x:Uid is not counted.
    private static readonly Regex XamlCommentRegex = new(
        @"<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static IReadOnlyList<UidReference> ExtractAll(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException($"XAML root not found: {rootDirectory}");

        var results = new List<UidReference>();
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            results.AddRange(ExtractFromFile(path));
        }
        return results;
    }

    public static IReadOnlyList<UidReference> ExtractFromFile(string path)
    {
        var raw = File.ReadAllText(path);
        var stripped = XamlCommentRegex.Replace(raw, string.Empty);

        var results = new List<UidReference>();
        foreach (Match m in UidRegex.Matches(stripped))
        {
            var uid = m.Groups[1].Value;
            // Approximate line number by counting newlines up to the match in the stripped text.
            var line = stripped.Substring(0, m.Index).Count(c => c == '\n') + 1;
            results.Add(new UidReference(uid, path, line));
        }
        return results;
    }
}
```

- [ ] **Step 5: Run — expect pass**

```bash
dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj
```

Expected: 5 tests pass.

- [ ] **Step 6: Commit**

```bash
git add tests/LocaleAudit.Tests/ tools/LocaleAudit/XamlUidExtractor.cs AgentX.sln
git commit -m "feat(a1): XamlUidExtractor with x:Uid parsing"
```

---

### Task 2.5: `CSharpGetStringExtractor` — parse `GetString("key")` from C# source

**Added per Decision 1.** Agent-X uses `ILocalizationService.GetString(key)` directly from C# in ~85% of localization call sites (confirmed by Spike 0 — 139 orphan resw keys per locale are code-bound, not XAML-bound). The audit tool must scan C# source to discover these keys so coverage is computed against the union `(xaml_uids ∪ code_keys)`.

**Files:**
- Create: `tools/LocaleAudit/CSharpGetStringExtractor.cs`
- Create: `tests/LocaleAudit.Tests/CSharpGetStringExtractorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.IO;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

public class CSharpGetStringExtractorTests
{
    [Fact]
    public void Extract_finds_direct_GetString_string_literal()
    {
        var cs = """
            public class Foo
            {
                public string Bar() => _localization.GetString("Nav_Dashboard");
            }
            """;
        var tmp = WriteTempCs(cs);

        var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

        result.Select(r => r.Key).Should().BeEquivalentTo("Nav_Dashboard");
        File.Delete(tmp);
    }

    [Fact]
    public void Extract_finds_multiple_distinct_keys_in_one_file()
    {
        var cs = """
            public class Foo
            {
                public void Run()
                {
                    var a = _localization.GetString("Nav_Dashboard");
                    var b = _localization.GetString("Nav_Chat");
                    var c = localizationService.GetString("Nav_Settings");
                }
            }
            """;
        var tmp = WriteTempCs(cs);

        var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

        result.Select(r => r.Key).Should().BeEquivalentTo("Nav_Dashboard", "Nav_Chat", "Nav_Settings");
        File.Delete(tmp);
    }

    [Fact]
    public void Extract_finds_GetString_with_format_args()
    {
        var cs = """
            var msg = _localization.GetString("Search_ResultCount", count);
            """;
        var tmp = WriteTempCs(cs);

        var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

        result.Select(r => r.Key).Should().BeEquivalentTo("Search_ResultCount");
        File.Delete(tmp);
    }

    [Fact]
    public void Extract_ignores_non_literal_args()
    {
        var cs = """
            var a = _localization.GetString(dynamicKey);
            var b = _localization.GetString(GetKey());
            var c = _localization.GetString(someVar + "Suffix");
            """;
        var tmp = WriteTempCs(cs);

        var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

        result.Should().BeEmpty();
        File.Delete(tmp);
    }

    [Fact]
    public void Extract_ignores_single_line_commented_out_calls()
    {
        var cs = """
            public class Foo
            {
                public void Run()
                {
                    // var old = _localization.GetString("Legacy_Key");
                    var n = _localization.GetString("Active_Key");
                }
            }
            """;
        var tmp = WriteTempCs(cs);

        var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

        result.Select(r => r.Key).Should().BeEquivalentTo("Active_Key");
        File.Delete(tmp);
    }

    [Fact]
    public void ExtractAll_recurses_subdirectories()
    {
        var tmpRoot = Directory.CreateTempSubdirectory("cs-audit-test").FullName;
        var sub = Path.Combine(tmpRoot, "Services");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(tmpRoot, "Root.cs"),
            "class R { void M() => _l.GetString(\"K_Root\"); }");
        File.WriteAllText(Path.Combine(sub, "Nested.cs"),
            "class N { void M() => _l.GetString(\"K_Nested\"); }");

        var result = CSharpGetStringExtractor.ExtractAll(tmpRoot);

        result.Select(r => r.Key).Should().Contain(new[] { "K_Root", "K_Nested" });
        Directory.Delete(tmpRoot, recursive: true);
    }

    [Fact]
    public void Extract_does_not_pick_up_unrelated_string_literals()
    {
        var cs = """
            var label = "Nav_Dashboard"; // this is NOT a GetString call
            var r = _localization.GetString("Nav_Real");
            """;
        var tmp = WriteTempCs(cs);

        var result = CSharpGetStringExtractor.ExtractFromFile(tmp);

        result.Select(r => r.Key).Should().BeEquivalentTo("Nav_Real");
        File.Delete(tmp);
    }

    private static string WriteTempCs(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cs-audit-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, content);
        return path;
    }
}
```

- [ ] **Step 2: Run — expect compile fail** (`CSharpGetStringExtractor` missing)

```bash
dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj --filter "FullyQualifiedName~CSharpGetStringExtractorTests"
```

- [ ] **Step 3: Write the extractor**

```csharp
using System.Text.RegularExpressions;

namespace LocaleAudit;

public sealed record CodeKeyReference(string Key, string SourceFile, int LineNumber);

public static class CSharpGetStringExtractor
{
    // Match: (anything).GetString("<literal>") possibly followed by , ...extra args... )
    // Captures the literal key only. Non-literal args (identifiers, interpolated strings,
    // concatenations) are intentionally excluded because the audit cannot verify them.
    private static readonly Regex GetStringRegex = new(
        @"\.GetString\s*\(\s*""([^""\\]*(?:\\.[^""\\]*)*)""\s*(?:,\s*[^)]*)?\)",
        RegexOptions.Compiled);

    // Strip C# single-line comments before matching — blockcomments are rare enough
    // in Agent-X to defer; can be added if Spike 0 later finds a false-positive.
    private static readonly Regex SingleLineCommentRegex = new(
        @"//[^\n]*",
        RegexOptions.Compiled);

    public static IReadOnlyList<CodeKeyReference> ExtractAll(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException($"C# root not found: {rootDirectory}");

        var results = new List<CodeKeyReference>();
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.cs", SearchOption.AllDirectories))
        {
            // Skip auto-generated files — they pollute results and typically don't call GetString.
            if (path.Contains("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (path.Contains("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (path.EndsWith(".g.cs", StringComparison.Ordinal)) continue;
            if (path.EndsWith(".g.i.cs", StringComparison.Ordinal)) continue;

            results.AddRange(ExtractFromFile(path));
        }
        return results;
    }

    public static IReadOnlyList<CodeKeyReference> ExtractFromFile(string path)
    {
        var raw = File.ReadAllText(path);
        var stripped = SingleLineCommentRegex.Replace(raw, string.Empty);

        var results = new List<CodeKeyReference>();
        foreach (Match m in GetStringRegex.Matches(stripped))
        {
            var key = m.Groups[1].Value;
            // Unescape any `\"` inside the key literal.
            key = key.Replace("\\\"", "\"");
            var line = stripped.Substring(0, m.Index).Count(c => c == '\n') + 1;
            results.Add(new CodeKeyReference(key, path, line));
        }
        return results;
    }
}
```

- [ ] **Step 4: Run — expect pass**

```bash
dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj --filter "FullyQualifiedName~CSharpGetStringExtractorTests"
```

Expected: all 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add tools/LocaleAudit/CSharpGetStringExtractor.cs tests/LocaleAudit.Tests/CSharpGetStringExtractorTests.cs
git commit -m "feat(a1): CSharpGetStringExtractor for code-bound localization keys"
```

---

### Task 3: `ReswReader` — parse `Resources.resw` per locale

**Files:**
- Create: `tools/LocaleAudit/ReswReader.cs`
- Create: `tests/LocaleAudit.Tests/ReswReaderTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.IO;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

public class ReswReaderTests
{
    [Fact]
    public void ReadFile_returns_all_name_value_pairs()
    {
        var resw = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="MyButton.Content" xml:space="preserve">
                <value>Click me</value>
              </data>
              <data name="MyLabel.Text" xml:space="preserve">
                <value>Hello</value>
              </data>
            </root>
            """;
        var tmp = WriteTempResw(resw);

        var result = ReswReader.ReadFile(tmp);

        result.Should().HaveCount(2);
        result["MyButton.Content"].Should().Be("Click me");
        result["MyLabel.Text"].Should().Be("Hello");
        File.Delete(tmp);
    }

    [Fact]
    public void ReadFile_handles_empty_resw()
    {
        var resw = """
            <?xml version="1.0" encoding="utf-8"?>
            <root></root>
            """;
        var tmp = WriteTempResw(resw);

        var result = ReswReader.ReadFile(tmp);

        result.Should().BeEmpty();
        File.Delete(tmp);
    }

    [Fact]
    public void ReadAllLocales_discovers_all_locale_folders()
    {
        var root = Directory.CreateTempSubdirectory("resw-locales-test").FullName;
        WriteResw(root, "en-US", "<data name=\"A.Text\"><value>A</value></data>");
        WriteResw(root, "fr", "<data name=\"A.Text\"><value>A-fr</value></data>");
        WriteResw(root, "ja", "");

        var result = ReswReader.ReadAllLocales(root);

        result.Keys.Should().BeEquivalentTo("en-US", "fr", "ja");
        result["en-US"]["A.Text"].Should().Be("A");
        result["fr"]["A.Text"].Should().Be("A-fr");
        result["ja"].Should().BeEmpty();
        Directory.Delete(root, recursive: true);
    }

    private static string WriteTempResw(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"resw-{Guid.NewGuid():N}.resw");
        File.WriteAllText(path, content);
        return path;
    }

    private static void WriteResw(string root, string locale, string entriesXml)
    {
        var dir = Path.Combine(root, locale);
        Directory.CreateDirectory(dir);
        var resw = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <root>{entriesXml}</root>
            """;
        File.WriteAllText(Path.Combine(dir, "Resources.resw"), resw);
    }
}
```

- [ ] **Step 2: Run — expect compile fail**

Run: `dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj --filter "FullyQualifiedName~ReswReaderTests"`
Expected: `ReswReader` missing.

- [ ] **Step 3: Write the reader**

```csharp
using System.Xml.Linq;

namespace LocaleAudit;

public static class ReswReader
{
    public static IReadOnlyDictionary<string, string> ReadFile(string path)
    {
        var doc = XDocument.Load(path);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            var value = data.Element("value")?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(name))
                result[name] = value;
        }
        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadAllLocales(string stringsRoot)
    {
        if (!Directory.Exists(stringsRoot))
            throw new DirectoryNotFoundException($"Strings root not found: {stringsRoot}");

        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var localeDir in Directory.EnumerateDirectories(stringsRoot))
        {
            var locale = Path.GetFileName(localeDir);
            var reswPath = Path.Combine(localeDir, "Resources.resw");
            result[locale] = File.Exists(reswPath) ? ReadFile(reswPath) : new Dictionary<string, string>();
        }
        return result;
    }
}
```

- [ ] **Step 4: Run — expect pass**

Run: `dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj --filter "FullyQualifiedName~ReswReaderTests"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add tools/LocaleAudit/ReswReader.cs tests/LocaleAudit.Tests/ReswReaderTests.cs
git commit -m "feat(a1): ReswReader with per-locale parsing"
```

---

### Task 4: `CoverageReport` — coverage calculation + JSON emission

**Files:**
- Create: `tools/LocaleAudit/CoverageReport.cs`
- Create: `tests/LocaleAudit.Tests/CoverageReportTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.IO;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

public class CoverageReportTests
{
    [Fact]
    public void Build_counts_coverage_per_locale_and_emits_missing_keys()
    {
        var uids = new List<UidReference>
        {
            new("BtnOk", "x.xaml", 1),
            new("BtnCancel", "x.xaml", 2),
            new("Greeting", "y.xaml", 1),
        };
        var codeKeys = new List<CodeKeyReference>(); // none in this test
        // en-US has all 3, fr has 2 (missing Greeting), ja has 1 (BtnOk only).
        var locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en-US"] = new Dictionary<string, string>
            {
                ["BtnOk.Content"] = "OK",
                ["BtnCancel.Content"] = "Cancel",
                ["Greeting.Text"] = "Hello",
            },
            ["fr"] = new Dictionary<string, string>
            {
                ["BtnOk.Content"] = "OK",
                ["BtnCancel.Content"] = "Annuler",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["BtnOk.Content"] = "OK",
            },
        };

        var report = CoverageReport.Build(uids, codeKeys, locales);

        report.PerLocale["en-US"].CoveragePercent.Should().Be(100.0);
        report.PerLocale["fr"].CoveragePercent.Should().BeApproximately(66.67, 0.1);
        report.PerLocale["ja"].CoveragePercent.Should().BeApproximately(33.33, 0.1);
        report.PerLocale["fr"].MissingKeys.Should().BeEquivalentTo("Greeting");
        report.PerLocale["ja"].MissingKeys.Should().BeEquivalentTo("BtnCancel", "Greeting");
    }

    [Fact]
    public void Build_unions_xaml_uids_with_csharp_code_keys()
    {
        var uids = new List<UidReference> { new("BtnOk", "x.xaml", 1) };
        var codeKeys = new List<CodeKeyReference>
        {
            new("Nav_Dashboard", "N.cs", 10),
            new("Nav_Chat", "N.cs", 11),
        };
        // en-US has ALL three — total unique keys should be 3 (union).
        var locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en-US"] = new Dictionary<string, string>
            {
                ["BtnOk.Content"] = "OK",
                ["Nav_Dashboard"] = "Dashboard",
                ["Nav_Chat"] = "Chat",
            },
        };

        var report = CoverageReport.Build(uids, codeKeys, locales);

        report.TotalKeys.Should().Be(3);
        report.PerLocale["en-US"].CoveragePercent.Should().Be(100.0);
    }

    [Fact]
    public void Build_dedupes_when_same_key_appears_in_both_xaml_and_code()
    {
        var uids = new List<UidReference> { new("BtnOk", "x.xaml", 1) };
        var codeKeys = new List<CodeKeyReference> { new("BtnOk", "y.cs", 1) };
        var locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en-US"] = new Dictionary<string, string> { ["BtnOk.Content"] = "OK" },
        };

        var report = CoverageReport.Build(uids, codeKeys, locales);

        report.TotalKeys.Should().Be(1); // deduped
    }

    [Fact]
    public void ShouldFail_returns_true_when_any_locale_below_threshold()
    {
        var report = new CoverageReport
        {
            TotalKeys = 100,
            PerLocale = new Dictionary<string, LocaleCoverage>
            {
                ["en-US"] = new() { Locale = "en-US", Covered = 100, CoveragePercent = 100.0 },
                ["fr"] = new() { Locale = "fr", Covered = 90, CoveragePercent = 90.0 },
            },
        };

        report.ShouldFail(threshold: 98.0).Should().BeTrue();
    }

    [Fact]
    public void ShouldFail_returns_false_when_all_locales_above_threshold()
    {
        var report = new CoverageReport
        {
            TotalKeys = 100,
            PerLocale = new Dictionary<string, LocaleCoverage>
            {
                ["en-US"] = new() { Locale = "en-US", Covered = 100, CoveragePercent = 100.0 },
                ["fr"] = new() { Locale = "fr", Covered = 99, CoveragePercent = 99.0 },
            },
        };

        report.ShouldFail(threshold: 98.0).Should().BeFalse();
    }

    [Fact]
    public void WriteJson_emits_valid_json_with_required_fields()
    {
        var report = new CoverageReport
        {
            TotalKeys = 10,
            PerLocale = new Dictionary<string, LocaleCoverage>
            {
                ["en-US"] = new() { Locale = "en-US", Covered = 10, CoveragePercent = 100.0 },
            },
        };
        var path = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.json");

        CoverageReport.WriteJson(report, path);

        var json = File.ReadAllText(path);
        json.Should().Contain("\"totalKeys\": 10");
        json.Should().Contain("\"en-US\"");
        File.Delete(path);
    }
}
```

- [ ] **Step 2: Run — expect compile fail**

Run: `dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj --filter "FullyQualifiedName~CoverageReportTests"`
Expected: `CoverageReport`, `LocaleCoverage` missing.

- [ ] **Step 3: Write the report types**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocaleAudit;

public sealed class LocaleCoverage
{
    public string Locale { get; set; } = string.Empty;
    public int Covered { get; set; }
    public double CoveragePercent { get; set; }
    public List<string> MissingKeys { get; set; } = new();
}

public sealed class CoverageReport
{
    public int TotalKeys { get; set; }
    public Dictionary<string, LocaleCoverage> PerLocale { get; set; } = new();

    /// <summary>
    /// Coverage is computed over the UNION of XAML x:Uid references and C# GetString("key")
    /// call sites. A key counts as "covered" in a locale if EITHER:
    ///   (a) the locale has an entry whose name starts with "<key>." (XAML-style, e.g. "BtnOk.Content"), OR
    ///   (b) the locale has an entry whose name equals "<key>" exactly (code-style, e.g. "Nav_Dashboard").
    /// This matches Agent-X's mixed naming convention (Spike 3 finding).
    /// </summary>
    public static CoverageReport Build(
        IReadOnlyList<UidReference> xamlUids,
        IReadOnlyList<CodeKeyReference> codeKeys,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> locales)
    {
        var unionKeys = xamlUids.Select(u => u.Uid)
            .Concat(codeKeys.Select(c => c.Key))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var report = new CoverageReport { TotalKeys = unionKeys.Count };

        foreach (var (locale, entries) in locales)
        {
            var coverage = new LocaleCoverage { Locale = locale };
            foreach (var key in unionKeys)
            {
                var hasXamlStyle = entries.Keys.Any(k => k.StartsWith(key + ".", StringComparison.Ordinal));
                var hasCodeStyle = entries.ContainsKey(key);
                if (hasXamlStyle || hasCodeStyle) coverage.Covered++;
                else coverage.MissingKeys.Add(key);
            }
            coverage.CoveragePercent = unionKeys.Count == 0
                ? 100.0
                : Math.Round(coverage.Covered * 100.0 / unionKeys.Count, 2);
            report.PerLocale[locale] = coverage;
        }
        return report;
    }

    public bool ShouldFail(double threshold)
        => PerLocale.Values.Any(c => c.CoveragePercent < threshold);

    public static void WriteJson(CoverageReport report, string path)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var json = JsonSerializer.Serialize(report, opts);
        File.WriteAllText(path, json);
    }

    public static void PrintSummary(CoverageReport report, TextWriter writer)
    {
        writer.WriteLine($"LocaleAudit — {report.TotalKeys} unique localization keys (XAML + C# union)");
        foreach (var (locale, c) in report.PerLocale.OrderBy(kv => kv.Key))
        {
            var status = c.CoveragePercent >= 98.0 ? "OK" : "LOW";
            writer.WriteLine($"  [{status}] {locale,-6} {c.CoveragePercent,6:F2}% ({c.Covered}/{report.TotalKeys})  missing: {c.MissingKeys.Count}");
        }
    }
}
```

- [ ] **Step 4: Run — expect pass**

Run: `dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj`
Expected: all 21 tests pass (5 XamlUidExtractor + 7 CSharpGetStringExtractor + 3 ReswReader + 6 CoverageReport).

- [ ] **Step 5: Restore tool to full-executable state**

Open `tools/LocaleAudit/LocaleAudit.Tool.csproj` and revert BOTH temporary blocks added in Task 1 Step 1:

(a) Change `OutputType` back to `Exe` and remove the 3 comment lines flagging it as temporary:
```xml
<!-- REVERT TO: -->
<OutputType>Exe</OutputType>
```

(b) DELETE the entire temporary `<ItemGroup>` that excludes `Program.cs`:
```xml
<!-- DELETE THESE 8 LINES: -->
<!-- TEMPORARY during Tasks 1-4: Program.cs references types ... -->
<!-- ... -->
<ItemGroup>
  <Compile Remove="Program.cs" />
</ItemGroup>
```

- [ ] **Step 6: Verify full build + full test suite**

```bash
dotnet build tools/LocaleAudit/LocaleAudit.Tool.csproj
dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj
```

Expected: tool builds clean (0W/0E) as an executable, all 21 tests still pass. `Program.cs` now compiles against all four now-existing types.

- [ ] **Step 7: Commit the restoration**

```bash
git add tools/LocaleAudit/LocaleAudit.Tool.csproj
git commit -m "build(a1): restore Exe output + Program.cs compilation (all extractor types exist)"
```

- [ ] **Step 5: Verify Program.cs builds**

```bash
dotnet build tools/LocaleAudit/LocaleAudit.Tool.csproj
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add tools/LocaleAudit/CoverageReport.cs tests/LocaleAudit.Tests/CoverageReportTests.cs
git commit -m "feat(a1): CoverageReport with per-locale calculation"
```

---

### Task 5: Run baseline + capture `baseline.json`

**Files:**
- Create: `tools/LocaleAudit/baseline.json`
- Create: `tools/LocaleAudit/README.md`

- [ ] **Step 1: Run the tool against the real codebase**

```bash
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- \
  src/AgentX.App \
  src \
  src/AgentX.App/Strings \
  --output tools/LocaleAudit/baseline.json \
  --fail-below 0
```

Note: three positional args are now `<xaml-root> <csharp-root> <strings-root>`. The C# root is `src` (covers both `src/AgentX.App` and `src/AgentX.Core` — Task 2.5's extractor recurses and skips `bin/` + `obj/` + generated files).

Expected: `baseline.json` written. Summary printed to stdout shows per-locale coverage across the unified XAML + C# key set — copy/paste into a scratchpad for reference. Per Spike 0 findings, **expect ~163 total keys** (24 XAML uids + ~140 code-bound keys, deduped), with per-locale coverage approaching ~90–95% (most code-bound keys already have resw entries; the 24 XAML-bound uids are the big gap).

Note: `--fail-below 0` lets this pass even at low coverage — we're capturing the starting point, not enforcing it yet. Task 12 enables the gate at 98%.

- [ ] **Step 2: Review coverage output**

Open `tools/LocaleAudit/baseline.json`. Expected shape:

```json
{
  "totalUids": <N>,
  "perLocale": {
    "de":    { "locale": "de",    "covered": 12, "coveragePercent": 8.33, "missingUids": ["..."] },
    "en-US": { "locale": "en-US", "covered": 140, "coveragePercent": 97.22, "missingUids": ["..."] },
    ...
  }
}
```

- [ ] **Step 3: Write `tools/LocaleAudit/README.md`**

```markdown
# LocaleAudit.Tool

Console tool that measures locale-coverage of localization keys — the union of XAML `x:Uid` references in `src/AgentX.App/` and C# `GetString("key")` invocations across `src/` — against each `Strings/<locale>/Resources.resw`.

## Usage

```bash
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- \
    <app-xaml-root> <csharp-root> <strings-root> \
    [--output audit-report.json] \
    [--fail-below 98]
```

- **Exit 0** — all locales ≥ threshold.
- **Exit 1** — at least one locale below threshold (use in CI).
- **Exit 2** — argument error.
- **Exit 3** — internal failure (I/O, XML parse, etc.).

## Baseline

`baseline.json` captures the locale-coverage state at the start of A1 (v2.1.0-final planning). Diff against it to show progress.

## How coverage is counted

A localization key is considered covered by a locale if EITHER:
- **XAML-style** — any resw entry of the form `<key>.*` exists (e.g., `BtnOk.Content` covers `BtnOk`), OR
- **Code-style** — an exact-match resw entry `<key>` exists (e.g., `Nav_Dashboard` covers itself).

The total key count is the union of:
- All unique `x:Uid="..."` values across `src/AgentX.App/**/*.xaml`.
- All unique literal string arguments passed to `.GetString("...")` across `src/**/*.cs`.
```

- [ ] **Step 4: Commit**

```bash
git add tools/LocaleAudit/baseline.json tools/LocaleAudit/README.md
git commit -m "docs(a1): capture LocaleAudit baseline and tool usage docs"
```

---

### Task 6: Extract missing canonical strings to `en-US/Resources.resw`

Using the `missingKeys` list from `baseline.json` for locale `en-US`, add entries to the en-US resw. Every `x:Uid` in `src/AgentX.App/` (24 uids per Spike 0) must have a matching `<Uid>.<Property>` entry in en-US — en-US is the canonical source that other locales translate FROM.

Per Spike 0 findings, the 24 missing uids are fully enumerated:

- **From `Views/PluginManagerPage.xaml` (17 uids — sample)**: `Plugin_Manager`, `Plugin_Active`, `Plugin_Configuration`, `Plugin_NoPlugins`, `Plugin_Refresh`, `Plugin_SelectPlugin`, `Plugin_Install`, `Plugin_Installed`, `Plugin_Uninstall`, + 8 more (full list from `baseline.json`).
- **From `Views/SettingsPage.xaml` (7 uids — sample)**: `Encryption_SectionHeader`, `Encryption_Description`, + 5 more.

**Files:**
- Modify: `src/AgentX.App/Strings/en-US/Resources.resw`

- [ ] **Step 1: Open `baseline.json` and filter to `perLocale["en-US"].missingKeys`**

This is the workset for Step 2. Expected size: 24 keys (per Spike 0) — all from the two identified XAML files.

- [ ] **Step 2: For each missing uid, open the XAML file that references it (from `XamlUidExtractor` output, also in the report)**

Determine the correct property target. The rules:

| XAML control | Property suffix |
|---|---|
| `Button`, `MenuFlyoutItem` | `.Content` |
| `TextBlock`, `Run` | `.Text` |
| `TextBox`, `PasswordBox`, `AutoSuggestBox` | `.PlaceholderText` |
| `ComboBox`, `ListView` (header) | `.Header` |
| `NavigationViewItem` | `.Content` + `.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` |
| `Custom control with DependencyProperty` | the dependency property name |

- [ ] **Step 3: Add entries to `en-US/Resources.resw`**

Example entries, pattern:

```xml
<data name="BtnOk.Content" xml:space="preserve">
  <value>OK</value>
</data>
<data name="SearchBox.PlaceholderText" xml:space="preserve">
  <value>Search documents…</value>
</data>
<data name="NavDocuments.Content" xml:space="preserve">
  <value>Documents</value>
</data>
```

Keep entries alphabetically sorted by `name` to minimize merge conflicts.

- [ ] **Step 4: Re-run LocaleAudit to confirm en-US is now ≥98%**

```bash
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- \
  src/AgentX.App \
  src/AgentX.App/Strings \
  --fail-below 0
```

Expected: `en-US` row shows ≥98%. Other locales still low — they'll be fixed in Task 7.

- [ ] **Step 5: Build app to confirm no XAML references break**

```bash
dotnet build src/AgentX.App
```

Expected: 0 errors. If a misnamed property suffix was used, the binding will silently fail at runtime (not compile-time) — smoke-test in Task 13.

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.App/Strings/en-US/Resources.resw
git commit -m "feat(a1): backfill canonical en-US Resources.resw to ≥98% coverage"
```

---

### Task 7: Backfill translations per locale (de, es, fr, ja, zh-CN)

**Files:**
- Modify: `src/AgentX.App/Strings/de/Resources.resw`
- Modify: `src/AgentX.App/Strings/es/Resources.resw`
- Modify: `src/AgentX.App/Strings/fr/Resources.resw`
- Modify: `src/AgentX.App/Strings/ja/Resources.resw`
- Modify: `src/AgentX.App/Strings/zh-CN/Resources.resw`

Workflow per Decision 3 (in-session with machine-translated drafts + review markers): for each locale, use the `missingKeys` diff against en-US as the translation workset. Draft translations may come from a machine translation service (DeepL preferred for de/es/fr/ja; Google Translate for zh-CN). Every machine-drafted entry MUST carry an inline `<!-- MACHINE_TRANSLATED: <en-US source> -->` review-marker comment so Rocky can spot-check before shipping. Rocky reviews each locale at implementation time — do NOT remove the review markers until Rocky signs off on that locale.

- [ ] **Step 1: Generate missing-keys diff per locale**

```bash
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- \
  src/AgentX.App \
  src/AgentX.App/Strings \
  --output /tmp/current.json \
  --fail-below 0
```

Parse `/tmp/current.json` → for each locale, collect `missingUids`, then for each missing uid look up the en-US value (canonical source).

- [ ] **Step 2: Translate in waves — one locale at a time**

Per locale, pattern:

1. Prepare a two-column worksheet: `Key | en-US value | <locale> translation`.
2. Fill translation column using a human translator OR machine translation + expert review (Rocky must sign off).
3. For strings that contain format placeholders (`{0}`, `{1}`), preserve them exactly. Example: `"Imported {0} documents"` → `"{0} Dokumente importiert"` (German).
4. For UI-chrome strings ("OK", "Cancel", "Close", "Save"), prefer platform-standard translations from Windows Settings screenshots (matches user's OS-level vocabulary).
5. Add entries alphabetically-sorted to `Strings/<locale>/Resources.resw`.

- [ ] **Step 3: German (de) backfill**

Add to `src/AgentX.App/Strings/de/Resources.resw`. Each machine-translated entry gets a `<!-- MACHINE_TRANSLATED: ... -->` review marker. Example:

```xml
<!-- MACHINE_TRANSLATED: OK -->
<data name="BtnOk.Content" xml:space="preserve">
  <value>OK</value>
</data>
<!-- MACHINE_TRANSLATED: Search documents… -->
<data name="SearchBox.PlaceholderText" xml:space="preserve">
  <value>Dokumente durchsuchen…</value>
</data>
<!-- MACHINE_TRANSLATED: Documents -->
<data name="NavDocuments.Content" xml:space="preserve">
  <value>Dokumente</value>
</data>
```

Once Rocky reviews and approves a locale, strip the review markers in a follow-up commit `docs(a1): approve <locale> translations (markers removed)`. Commit the initial backfill with: `git commit -m "feat(a1): backfill de Resources.resw (machine-translated, awaiting review)"`

- [ ] **Step 4: Spanish (es) backfill** — same pattern with review markers. Commit: `feat(a1): backfill es Resources.resw (machine-translated, awaiting review)`
- [ ] **Step 5: French (fr) backfill** — same pattern with review markers. Commit: `feat(a1): backfill fr Resources.resw (machine-translated, awaiting review)`
- [ ] **Step 6: Japanese (ja) backfill** — same pattern with review markers. Note: punctuation differs ("…" is "…"/"。" depending on context). Commit: `feat(a1): backfill ja Resources.resw (machine-translated, awaiting review)`
- [ ] **Step 7: Simplified Chinese (zh-CN) backfill** — same pattern with review markers. Commit: `feat(a1): backfill zh-CN Resources.resw (machine-translated, awaiting review)`

- [ ] **Step 8: Verify all locales ≥98%**

```bash
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- \
  src/AgentX.App \
  src/AgentX.App/Strings \
  --fail-below 98
```

Expected: exit code 0. Every row shows `[OK]`.

---

### Task 8: Add `FormatPlural` to `ILocalizationService`

**Files:**
- Create: `src/AgentX.Core/Services/Localization/IPluralRuleProvider.cs`
- Create: `src/AgentX.Core/Services/Localization/CldrPluralRuleProvider.cs`
- Modify: `src/AgentX.Core/Services/Localization/ILocalizationService.cs` — add `FormatPlural`
- Modify: `src/AgentX.Core/Services/Localization/LocalizationService.cs` — implement `FormatPlural`

- [ ] **Step 1: Write `IPluralRuleProvider`**

```csharp
using System.Globalization;

namespace AgentX.Core.Services.Localization;

/// <summary>
/// Resolves a CLDR plural-category name (e.g., "one", "other", "few") for a given culture + count.
/// See https://cldr.unicode.org/index/cldr-spec/plural-rules
/// </summary>
public interface IPluralRuleProvider
{
    /// <summary>Returns a lowercase CLDR category: "zero" / "one" / "two" / "few" / "many" / "other".</summary>
    string GetCategory(CultureInfo culture, double count);
}
```

- [ ] **Step 2: Write `CldrPluralRuleProvider`**

```csharp
using System.Globalization;

namespace AgentX.Core.Services.Localization;

/// <summary>
/// Minimal, direct-coded CLDR plural rules for the six supported Agent-X locales.
/// Matches Unicode CLDR v44 plural rules for cardinal numbers.
/// </summary>
public sealed class CldrPluralRuleProvider : IPluralRuleProvider
{
    public string GetCategory(CultureInfo culture, double count)
    {
        var n = Math.Abs(count);
        var lang = culture.TwoLetterISOLanguageName;

        return lang switch
        {
            // English, German, Spanish, French — "one" for n == 1, else "other".
            // (French treats 0 as "one"; German, Spanish, English do not.)
            "en" => n == 1 ? "one" : "other",
            "de" => n == 1 ? "one" : "other",
            "es" => n == 1 ? "one" : "other",
            "fr" => (n == 0 || n == 1) ? "one" : "other",

            // Japanese, Simplified Chinese — single plural category "other".
            "ja" => "other",
            "zh" => "other",

            // Default: English-style.
            _ => n == 1 ? "one" : "other",
        };
    }
}
```

- [ ] **Step 3: Add `FormatPlural` to `ILocalizationService`**

Per Spike 1 findings, add to the existing interface:

```csharp
/// <summary>
/// Selects a CLDR plural-category resource for the current UI culture.
/// Looks up "<baseKey>_<category>" (e.g., "DocumentsImported_one"), falling back
/// to "<baseKey>_other" if the specific category is absent. Throws if neither exists.
/// </summary>
/// <param name="baseKey">Resource base key. Must have at least "<baseKey>_other" defined.</param>
/// <param name="count">The count driving plural selection.</param>
/// <param name="args">Optional format args substituted into the chosen resource value.</param>
string FormatPlural(string baseKey, double count, params object[] args);
```

- [ ] **Step 4: Implement in `LocalizationService`**

```csharp
public string FormatPlural(string baseKey, double count, params object[] args)
{
    var culture = CultureInfo.CurrentUICulture;
    var category = _pluralRules.GetCategory(culture, count);
    var specificKey = $"{baseKey}_{category}";

    var template = GetString(specificKey)
                   ?? GetString($"{baseKey}_other")
                   ?? throw new KeyNotFoundException(
                       $"No plural resource for '{baseKey}' in category '{category}' or '_other' fallback.");

    return string.Format(culture, template, args);
}
```

Wire `IPluralRuleProvider` into the ctor. Register both in DI.

- [ ] **Step 5: Commit**

```bash
git add -u
git commit -m "feat(a1): add FormatPlural with CLDR plural rules"
```

---

### Task 9: Tests for `CldrPluralRuleProvider` + `FormatPlural`

**Files:**
- Create: `tests/AgentX.Tests/Services/Localization/CldrPluralRuleProviderTests.cs`
- Create: `tests/AgentX.Tests/Services/Localization/LocalizationServicePluralTests.cs`

- [ ] **Step 1: Plural-rule tests**

```csharp
using System.Globalization;
using AgentX.Core.Services.Localization;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Localization;

public class CldrPluralRuleProviderTests
{
    private readonly CldrPluralRuleProvider _sut = new();

    [Theory]
    [InlineData("en-US", 0, "other")]
    [InlineData("en-US", 1, "one")]
    [InlineData("en-US", 2, "other")]
    [InlineData("en-US", 5, "other")]
    [InlineData("de", 1, "one")]
    [InlineData("de", 2, "other")]
    [InlineData("es", 1, "one")]
    [InlineData("es", 3, "other")]
    [InlineData("fr", 0, "one")]  // French: 0 and 1 both "one"
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("ja", 0, "other")]
    [InlineData("ja", 1, "other")]
    [InlineData("ja", 99, "other")]
    [InlineData("zh-CN", 1, "other")]
    [InlineData("zh-CN", 999, "other")]
    public void GetCategory_returns_expected_cldr_category(string cultureName, double count, string expected)
    {
        var result = _sut.GetCategory(new CultureInfo(cultureName), count);
        result.Should().Be(expected);
    }
}
```

- [ ] **Step 2: `FormatPlural` integration tests**

```csharp
using System.Globalization;
using AgentX.Core.Services.Localization;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Localization;

public class LocalizationServicePluralTests
{
    [Fact]
    public void FormatPlural_selects_one_form_for_count_1_en()
    {
        using var _ = new CultureScope("en-US");
        var sut = BuildServiceWithResources(
            ("DocumentsImported_one", "Imported {0} document"),
            ("DocumentsImported_other", "Imported {0} documents"));

        var result = sut.FormatPlural("DocumentsImported", 1, 1);

        result.Should().Be("Imported 1 document");
    }

    [Fact]
    public void FormatPlural_selects_other_form_for_count_2_en()
    {
        using var _ = new CultureScope("en-US");
        var sut = BuildServiceWithResources(
            ("DocumentsImported_one", "Imported {0} document"),
            ("DocumentsImported_other", "Imported {0} documents"));

        var result = sut.FormatPlural("DocumentsImported", 2, 2);

        result.Should().Be("Imported 2 documents");
    }

    [Fact]
    public void FormatPlural_falls_back_to_other_when_specific_category_absent_ja()
    {
        using var _ = new CultureScope("ja");
        var sut = BuildServiceWithResources(
            ("DocumentsImported_other", "{0}件のドキュメントをインポートしました"));

        var result = sut.FormatPlural("DocumentsImported", 3, 3);

        result.Should().Be("3件のドキュメントをインポートしました");
    }

    [Fact]
    public void FormatPlural_throws_when_other_fallback_also_missing()
    {
        using var _ = new CultureScope("en-US");
        var sut = BuildServiceWithResources(
            ("Unrelated_one", "unused"));

        Action act = () => sut.FormatPlural("DocumentsImported", 1, 1);

        act.Should().Throw<KeyNotFoundException>()
           .WithMessage("*DocumentsImported*");
    }

    private static ILocalizationService BuildServiceWithResources(params (string key, string value)[] entries)
    {
        var fake = new FakeResourceLoader(entries.ToDictionary(e => e.key, e => e.value));
        return new LocalizationService(fake, new CldrPluralRuleProvider());
    }

    private sealed class FakeResourceLoader : IResourceLoaderAdapter
    {
        private readonly Dictionary<string, string> _map;
        public FakeResourceLoader(Dictionary<string, string> map) => _map = map;
        public string? GetString(string key) => _map.TryGetValue(key, out var v) ? v : null;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _prev;
        public CultureScope(string name)
        {
            _prev = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = new CultureInfo(name);
        }
        public void Dispose() => CultureInfo.CurrentUICulture = _prev;
    }
}
```

Note: `IResourceLoaderAdapter` is the abstraction `LocalizationService` uses to read strings. If Spike 1 found a different abstraction, update this test fixture accordingly.

- [ ] **Step 3: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~CldrPluralRuleProviderTests|FullyQualifiedName~LocalizationServicePluralTests"
```

Expected: 15 tests pass (15 InlineData for plural + 4 FormatPlural = 19 test cases).

- [ ] **Step 4: Commit**

```bash
git add tests/AgentX.Tests/Services/Localization/
git commit -m "test(a1): plural-rule coverage across all 6 locales"
```

---

### Task 10: RTL-safe `FlowDirection` binding

**Files:**
- Create: `src/AgentX.App/Helpers/FlowDirectionHelper.cs`
- Modify: `src/AgentX.App/MainWindow.xaml` — bind root `FlowDirection`
- Modify: `src/AgentX.App/MainWindow.xaml.cs` — hook helper on startup
- Create: `tests/AgentX.Tests/Services/Localization/PseudoLocaleFlowDirectionTests.cs`

- [ ] **Step 1: Write `FlowDirectionHelper`**

```csharp
using System.Globalization;
using Microsoft.UI.Xaml;

namespace AgentX.App.Helpers;

/// <summary>
/// Computes the correct <see cref="FlowDirection"/> for the current UI culture.
/// Agent-X ships with LTR locales only, but this helper is wired from day one so
/// ar-SA / he-IL / fa-IR can be added later without touching XAML.
/// </summary>
public static class FlowDirectionHelper
{
    /// <summary>Returns <see cref="FlowDirection.RightToLeft"/> for RTL cultures, otherwise LTR.</summary>
    public static FlowDirection ForCulture(CultureInfo culture)
        => culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Current UI-culture-derived flow direction.</summary>
    public static FlowDirection Current() => ForCulture(CultureInfo.CurrentUICulture);
}
```

- [ ] **Step 2: Bind root `FlowDirection` in `MainWindow.xaml`**

Find the root `Grid` / `Frame` / `NavigationView` in `MainWindow.xaml` and add the attribute (via code-behind binding since `FlowDirection` is not trivially bindable from XAML itself):

```xaml
<!-- In MainWindow.xaml, the root panel (e.g., Grid x:Name="RootGrid") -->
<Grid x:Name="RootGrid">
    <!-- existing content -->
</Grid>
```

- [ ] **Step 3: Apply in code-behind on startup**

In `MainWindow.xaml.cs` constructor, after `InitializeComponent()`:

```csharp
using AgentX.App.Helpers;

public MainWindow()
{
    InitializeComponent();
    RootGrid.FlowDirection = FlowDirectionHelper.Current();
    // ... existing setup
}
```

- [ ] **Step 4: Pseudo-locale flow-direction test**

```csharp
using System.Globalization;
using AgentX.App.Helpers;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Xunit;

namespace AgentX.Tests.Services.Localization;

public class PseudoLocaleFlowDirectionTests
{
    [Theory]
    [InlineData("en-US", FlowDirection.LeftToRight)]
    [InlineData("de",    FlowDirection.LeftToRight)]
    [InlineData("fr",    FlowDirection.LeftToRight)]
    [InlineData("ja",    FlowDirection.LeftToRight)]
    [InlineData("zh-CN", FlowDirection.LeftToRight)]
    [InlineData("ar-SA", FlowDirection.RightToLeft)]  // Future locale — verify helper
    [InlineData("he-IL", FlowDirection.RightToLeft)]  // Future locale — verify helper
    public void ForCulture_returns_expected_direction(string cultureName, FlowDirection expected)
    {
        var culture = new CultureInfo(cultureName);
        FlowDirectionHelper.ForCulture(culture).Should().Be(expected);
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~PseudoLocaleFlowDirectionTests"
```

Expected: 7 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.App/Helpers/FlowDirectionHelper.cs src/AgentX.App/MainWindow.xaml src/AgentX.App/MainWindow.xaml.cs tests/AgentX.Tests/Services/Localization/PseudoLocaleFlowDirectionTests.cs
git commit -m "feat(a1): root FlowDirection binding + RTL pseudo-locale tests"
```

---

### Task 11: Per-page locale snapshot QA harness

**Files:**
- Create: `tests/AgentX.Tests/Services/Localization/PerPageLocaleSnapshotTests.cs`

- [ ] **Step 1: Write snapshot test**

Rationale: this is a lightweight integration test that enumerates every key referenced in every XAML page and asserts that (a) it exists in en-US, and (b) it resolves to a non-empty string in each of the 6 locales. It does NOT render the page visually (WinUI 3 requires a UI thread) — visual QA is manual per Task 14.

```csharp
using System.Globalization;
using AgentX.Core.Services.Localization;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace AgentX.Tests.Services.Localization;

public class PerPageLocaleSnapshotTests
{
    // Point these at the real app/strings roots from the test's working directory.
    // Adjust relative path if the test project runs from a different base.
    private const string AppXamlRoot = "../../../../../src/AgentX.App";
    private const string CSharpRoot  = "../../../../../src";
    private const string StringsRoot = "../../../../../src/AgentX.App/Strings";

    private static readonly string[] RequiredLocales =
        { "de", "en-US", "es", "fr", "ja", "zh-CN" };

    [Fact]
    public void Every_referenced_key_resolves_to_non_empty_value_in_every_locale()
    {
        var xamlUids = XamlUidExtractor.ExtractAll(AppXamlRoot);
        var codeKeys = CSharpGetStringExtractor.ExtractAll(CSharpRoot);
        var locales = ReswReader.ReadAllLocales(StringsRoot);

        var unionKeys = xamlUids.Select(u => u.Uid)
            .Concat(codeKeys.Select(c => c.Key))
            .Distinct(StringComparer.Ordinal);

        foreach (var locale in RequiredLocales)
        {
            locales.Should().ContainKey(locale, $"locale folder missing for '{locale}'");
            foreach (var key in unionKeys)
            {
                // A key is covered if the locale has either an XAML-style "<key>.*" entry
                // or a code-style exact "<key>" entry.
                var xamlStyle = locales[locale].Where(kv => kv.Key.StartsWith(key + ".", StringComparison.Ordinal)).ToList();
                var codeStyle = locales[locale].TryGetValue(key, out var codeVal) ? codeVal : null;
                var hasAny = xamlStyle.Count > 0 || codeStyle is not null;
                hasAny.Should().BeTrue($"'{key}' is missing from '{locale}/Resources.resw'");

                var anyNonEmpty = xamlStyle.Any(kv => !string.IsNullOrWhiteSpace(kv.Value))
                                  || !string.IsNullOrWhiteSpace(codeStyle);
                anyNonEmpty.Should().BeTrue($"'{key}' is present but blank in '{locale}/Resources.resw'");
            }
        }
    }
}
```

- [ ] **Step 2: Run — expect pass (if Tasks 6–7 completed properly)**

```bash
dotnet test --filter "FullyQualifiedName~PerPageLocaleSnapshotTests"
```

Expected: 1 test passes. If it fails, the error message points to the specific missing/blank uid and locale — fix the resw file and re-run.

- [ ] **Step 3: Commit**

```bash
git add tests/AgentX.Tests/Services/Localization/PerPageLocaleSnapshotTests.cs
git commit -m "test(a1): per-page locale snapshot QA — 100% uid/locale coverage"
```

---

### Task 12: Locale-coverage CI gate

**Files:**
- Create: `.github/workflows/locale-audit.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: LocaleAudit

on:
  pull_request:
    paths:
      - 'src/AgentX.App/**/*.xaml'
      - 'src/**/*.cs'
      - 'src/AgentX.App/Strings/**/*.resw'
      - 'tools/LocaleAudit/**'
  push:
    branches: [main]

jobs:
  coverage-gate:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore LocaleAudit.Tool
        run: dotnet restore tools/LocaleAudit/LocaleAudit.Tool.csproj

      - name: Run LocaleAudit
        run: |
          dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- `
            src/AgentX.App `
            src `
            src/AgentX.App/Strings `
            --output locale-audit-report.json `
            --fail-below 98

      - name: Upload report artifact
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: locale-audit-report
          path: locale-audit-report.json

      - name: Comment coverage on PR
        if: github.event_name == 'pull_request' && always()
        uses: actions/github-script@v7
        with:
          script: |
            const fs = require('fs');
            if (!fs.existsSync('locale-audit-report.json')) return;
            const report = JSON.parse(fs.readFileSync('locale-audit-report.json', 'utf8'));
            const rows = Object.entries(report.perLocale)
              .sort(([a],[b]) => a.localeCompare(b))
              .map(([k, v]) => `| ${k} | ${v.coveragePercent.toFixed(2)}% | ${v.covered}/${report.totalUids} | ${v.missingUids.length} |`)
              .join('\n');
            const body = `### LocaleAudit\n\n| Locale | Coverage | Entries | Missing |\n|---|---|---|---|\n${rows}\n`;
            github.rest.issues.createComment({
              issue_number: context.issue.number,
              owner: context.repo.owner,
              repo: context.repo.repo,
              body
            });
```

- [ ] **Step 2: Commit + push a test PR to verify**

```bash
git add .github/workflows/locale-audit.yml
git commit -m "ci(a1): locale-coverage gate blocks PRs below 98%"
```

- [ ] **Step 3: Force a failure to confirm the gate works**

Deliberately delete an entry from `de/Resources.resw`, open a draft PR, and confirm the GitHub Actions job fails. Then restore and confirm it passes.

- [ ] **Step 4: Commit the restoration**

```bash
git checkout src/AgentX.App/Strings/de/Resources.resw
```

---

### Task 13: End-to-end smoke test per locale

- [ ] **Step 1: Run the app for each locale**

In `src/AgentX.App/App.xaml.cs`, temporarily force the UI culture (comment out after smoke):

```csharp
// Temporary: test ja-JP locale
System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ja");
```

Launch:

```bash
dotnet run --project src/AgentX.App
```

Navigate through every page and confirm:
- No English "x:Uid" literal leaks onto the UI (indicates missing resw entry).
- No visible text-overflow in navigation labels (German/French tend to be 1.3× longer than English).
- No garbled glyphs (zh-CN and ja require CJK-capable fonts — `Segoe UI` falls back to `Yu Gothic` / `Microsoft YaHei` automatically).
- FlowDirection stays LTR.

Repeat for de / es / fr / ja / zh-CN.

- [ ] **Step 2: Revert the temporary culture override**

```bash
git checkout src/AgentX.App/App.xaml.cs
```

- [ ] **Step 3: Smoke-test FlowDirection with ar-SA pseudo-locale**

Temporarily force `CultureInfo.CurrentUICulture = new CultureInfo("ar-SA")`. Launch the app. Expected: root layout flips RTL — nav rail on the right, content mirrors. No resw entries exist yet for Arabic, so all text will render as literal uid — that's fine for this flow-direction visual verification only.

Revert the override. Document the screenshot in the release notes as proof of RTL readiness.

- [ ] **Step 4: Commit smoke-test evidence (if any screenshots captured)**

Screenshots go to `docs/images/a1-locale-smoke/` (create folder). Optional but strongly recommended.

---

### Task 14: Docs + release notes

**Files:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/USER-GUIDE.md`
- Modify: `docs/DEVELOPER-GUIDE.md`
- Modify: `docs/v2.1.0-RELEASE-NOTES.md`

- [ ] **Step 1: Append architecture section**

In `docs/ARCHITECTURE.md`:

```markdown
### Localization (A1)

Agent-X ships six UI locales — German (de), English (en-US, canonical), Spanish (es), French (fr), Japanese (ja), Simplified Chinese (zh-CN).

**Coverage enforcement.** Every `x:Uid` referenced in `src/AgentX.App/*.xaml` must have a matching `<Uid>.<Property>` entry in each locale's `Strings/<locale>/Resources.resw`. The `tools/LocaleAudit/LocaleAudit.Tool.csproj` console tool computes per-locale coverage; a GitHub Actions gate (`.github/workflows/locale-audit.yml`) fails PRs that drop global coverage below 98%.

**Pluralization.** `ILocalizationService.FormatPlural(baseKey, count, args)` uses `CldrPluralRuleProvider` to select the correct CLDR plural category (`one`, `other`, etc.) and resolves `<baseKey>_<category>` in resw. Fallback to `<baseKey>_other` if specific category is absent.

**RTL readiness.** Root `FlowDirection` is bound to `CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft` via `FlowDirectionHelper`. Agent-X has no RTL locales today — but ar-SA / he-IL / fa-IR can be added with no XAML changes, only new resw bundles.
```

- [ ] **Step 2: Append user-guide section**

In `docs/USER-GUIDE.md`:

```markdown
## Language selection

Agent-X uses your Windows display language by default. To change languages, open **Settings → Windows Language** and restart Agent-X.

Supported UI languages:
- English (United States)
- Deutsch
- Español
- Français
- 日本語
- 简体中文

Documentation is in English. Future releases will expand this list.
```

- [ ] **Step 3: Append developer-guide section**

In `docs/DEVELOPER-GUIDE.md`:

```markdown
## Adding or changing localized strings

1. **Add the `x:Uid` to your XAML**:
   ```xml
   <Button x:Uid="MyNewButton" />
   ```

2. **Add entries to all 6 `Resources.resw` files** — start with `Strings/en-US/Resources.resw` (canonical):
   ```xml
   <data name="MyNewButton.Content" xml:space="preserve">
     <value>My Button</value>
   </data>
   ```
   Then add translated entries to `Strings/{de,es,fr,ja,zh-CN}/Resources.resw`.

3. **Pluralization** — use `_one` / `_other` suffixes:
   ```xml
   <data name="DocumentsImported_one"><value>Imported {0} document</value></data>
   <data name="DocumentsImported_other"><value>Imported {0} documents</value></data>
   ```
   Call with `_localization.FormatPlural("DocumentsImported", count, count)`.

4. **Run locale audit locally** before pushing:
   ```bash
   dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- src/AgentX.App src src/AgentX.App/Strings
   ```

5. **CI gate** (`.github/workflows/locale-audit.yml`) will block the PR if any locale falls below 98% coverage.
```

- [ ] **Step 4: Append v2.1.0 release-notes entry**

Append to `docs/v2.1.0-RELEASE-NOTES.md`:

```markdown
### Multi-Language UI Depth (A1)

- ≥98% string-coverage across all 6 supported locales (de / en-US / es / fr / ja / zh-CN)
- CLDR-correct pluralization via `ILocalizationService.FormatPlural`
- RTL-ready `FlowDirection` binding — Arabic / Hebrew / Persian can be added via resw only
- `LocaleAudit.Tool` console tool + CI gate enforces coverage on every PR
- Per-page locale snapshot tests verify every `x:Uid` resolves to a non-empty value in every locale
```

- [ ] **Step 5: Full test + build gate**

```bash
dotnet build && dotnet test
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- src/AgentX.App src src/AgentX.App/Strings --fail-below 98
```

Expected: build 0W/0E, all tests pass, LocaleAudit exit 0.

- [ ] **Step 6: Final commit**

```bash
git add -u
git commit -m "docs(a1): architecture + user + developer + release notes for multi-language depth"
```

---

## Self-Review Summary

- **Spec coverage:**
  - String-extraction audit → Task 1 (scaffold) + Task 2 (XamlUidExtractor) + **Task 2.5 (CSharpGetStringExtractor — per Decision 1)** + Task 3 (ReswReader) + Task 4 (CoverageReport — consumes union)
  - ≥98% coverage per locale → Task 5 baseline + Task 6 en-US (24 canonical entries) + Task 7 other-locales (machine-translated with review markers) + Task 12 CI gate
  - Pluralization rules → Task 8 (`FormatPlural`) + Task 9 tests
  - RTL safety → Task 10 (`FlowDirectionHelper` + root binding)
  - Per-page locale QA → Task 11 snapshot test (union of XAML uids + C# keys) + Task 13 manual smoke
  - Locale-coverage CI gate → Task 12 GitHub Actions workflow
  - Orphan-key triage (Decision 2) → handled naturally by Task 4's union-coverage algorithm; keys still orphan after union are flagged in the report for a follow-up cleanup task (out-of-scope for v2.1.0 final)
- **Placeholder scan:** every code step contains complete code. Task 7's translation entries ship as machine-translated drafts with `<!-- MACHINE_TRANSLATED: ... -->` inline review markers; Rocky strips markers in follow-up commits after review.
- **Type consistency:** `UidReference`, `CodeKeyReference`, `LocaleCoverage`, `CoverageReport`, `IPluralRuleProvider`, `CldrPluralRuleProvider`, `FlowDirectionHelper`, `ILocalizationService.FormatPlural` — all names consistent across tasks. `XamlUidExtractor.ExtractAll` / `.ExtractFromFile`, `CSharpGetStringExtractor.ExtractAll` / `.ExtractFromFile`, `ReswReader.ReadFile` / `.ReadAllLocales`, `CoverageReport.Build(uids, codeKeys, locales)` / `.WriteJson` / `.PrintSummary` / `.ShouldFail`, `TotalKeys` / `MissingKeys` — methods called from `Program.cs` all match their definitions.

## Follow-up (not in this plan)

1. Arabic (ar-SA), Hebrew (he-IL), Persian (fa-IR) locale bundles — when a translator is contracted. FlowDirection binding is already ready.
2. Crowdsourced translation contributions via a `contributing/translations.md` workflow and a locale-specific issue template.
3. Documentation localization — USER-GUIDE.md translated into non-English locales. Scope deferred because UI-string volume is 10× docs volume.
4. Translation-memory / glossary file for consistent terminology across locales (e.g., "document" always translates the same way).
5. Per-locale font stack audit — confirm `Yu Gothic`, `Microsoft YaHei`, and `Segoe UI Emoji` fall back correctly on stripped Windows installs.
