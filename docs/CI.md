# Continuous Integration

This document tracks Agent-X's CI gates and their status against audit finding **AX-QA-006**
("CI does not gate the surfaces that failed this audit").

## Active workflows

| Workflow | File | Gates |
|---|---|---|
| Build & Test | `.github/workflows/build-test.yml` | Restores, builds `AgentX.Core` + `AgentX.Tests` (Release, x64), installs Playwright Chromium, runs the full unit-test suite. |
| LocaleAudit | `.github/workflows/locale-audit.yml` | Localization coverage ≥ 98% per locale + locale snapshot tests. |
| Extension CI | `.github/workflows/extension-ci.yml` | Browser-extension lint, typecheck, production build, and production-dependency `npm audit`. |
| Dependency Audit | `.github/workflows/dependency-audit.yml` | NuGet vulnerable-package scan across the solution; fails on any High/Critical advisory not on the accepted list. Runs on dependency changes and weekly. |

## AX-QA-006 gate matrix

| Audit-listed gap | Status | Where / why |
|---|---|---|
| Lint/typecheck/build the browser extension | ✅ Added | `extension-ci.yml` |
| `npm audit` (extension) | ✅ Added | `extension-ci.yml` — `npm audit --omit=dev` (production deps; dev-only advisory is AX-QA-016) |
| NuGet vulnerability checks | ✅ Added | `dependency-audit.yml` — allowlist gate (see below) |
| Build Android / iOS | ⏸ Deferred → **AX-QA-004** | No mobile project in `AgentX.sln`; `maui-android` workload absent. Add an Android job once the mobile transport is decided and the project is added to the solution. |
| Enforce code coverage | ⏸ Deferred → **AX-QA-009** | Coverage is currently 46% line / 33% branch. Setting and ratcheting the enforced threshold (with higher floors for security/migration code) is tracked under AX-QA-009. |
| `dotnet format --verify-no-changes` | ⏸ Deferred → **AX-QA-012** | The repo currently has ~69k CRLF/whitespace diagnostics from `.gitattributes`/`.editorconfig`/`autocrlf` drift. A format gate must be added **in the same change** that performs the mechanical normalization, so it is not mixed with functional fixes. |
| Publish / install / smoke-test the Windows artifact | ⏸ Deferred → **AX-QA-001 / 007** | Belongs to the signed release pipeline. |
| Verify the artifact was built from the release tag/HEAD (provenance) | ⏸ Deferred → **AX-QA-001 / 007** | Release-pipeline concern; pairs with signing. |
| Sign / verify signatures | ⏸ Deferred → **AX-QA-007** | Requires a code-signing certificate held in a protected pipeline; cannot be added from a normal CI run. |

## NuGet vulnerability allowlist

`dependency-audit.yml` fails on any **High/Critical** advisory **except** those explicitly accepted:

| Advisory | Package | Reason | Tracked by |
|---|---|---|---|
| [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) (CVE-2025-6965) | `SQLitePCLRaw.lib.e_sqlite3` 2.1.6 | No patched package version exists. Agent-X loads the SQLCipher provider (`e_sqlcipher.dll`) at runtime, not `e_sqlite3.dll`. | **AX-QA-010** |

Remove an entry from the allowlist (in the workflow and here) as soon as a fixed version ships, so
the gate starts enforcing it again.
