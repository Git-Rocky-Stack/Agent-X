# SignPath Foundation — Open-Source Code Signing Application

This document is the canonical record of Agent-X's application to the
**[SignPath Foundation](https://signpath.org/)** free code-signing program for open-source
projects, and a paste-ready brief for the application form. SignPath Foundation issues a
certificate held in its HSM and signs submitted artifacts through a managed service — the private
key never leaves SignPath, and there is no annual fee for qualifying OSS projects.

> **Why this matters.** Agent-X ships a Windows installer. Without an Authenticode signature,
> Microsoft Defender SmartScreen shows an "Unknown Publisher" warning on every install. SignPath
> Foundation solves this at zero cost for MIT software, and pairs with the keyless
> [cosign + Rekor provenance](RELEASE-SIGNING.md#layer-2--ci-keyless-provenance-cosign--rekor) we
> already run in CI. Authenticode answers *"can Windows trust this publisher?"*; cosign/Rekor
> answers *"can I prove this artifact came from the public build pipeline, unmodified?"* They are
> complementary, not substitutes.

---

## 1. One-paragraph summary (for the form)

Agent-X is a free, MIT-licensed, **local-first AI document-intelligence application for Windows**.
It turns a user's personal document collection into a private, queryable knowledge base — with
no cloud dependency, no telemetry, and no internet requirement by default. It is built on .NET 8
and WinUI 3 and distributed as a self-contained Windows installer. We are requesting free
Authenticode code signing so that end users no longer encounter a SmartScreen "Unknown Publisher"
warning when installing a fully open-source, auditable application.

---

## 2. Project facts

| Field | Value |
|---|---|
| **Project name** | Agent-X |
| **Description** | Local-first, privacy-preserving AI document-intelligence desktop app for Windows |
| **Repository** | https://github.com/Git-Rocky-Stack/Agent-X |
| **License** | MIT (OSI-approved) — `LICENSE`, © 2026 Rocky Elsalaymeh |
| **Primary language** | C# (.NET 8.0), WinUI 3 / Windows App SDK |
| **Maintainer** | Rocky Elsalaymeh — Elsalaymeh@gmail.com |
| **Platform / target** | Windows 10 build 19041+ (x64) |
| **Artifacts to sign** | `AgentX.exe` (app) and the Inno Setup installer(s) |
| **Build tooling** | `dotnet publish` (self-contained, win-x64) + Inno Setup 6, via `scripts/build-installers.ps1` |
| **CI** | GitHub Actions — build/test + coverage gate, dependency & locale audits, format gate, Android build (all public) |
| **Commercial model** | None. No paid tier, no closed-source components, no paywalled binary features. |

---

## 3. Open-source eligibility

SignPath Foundation requires that the project be genuinely open source and that signed builds be
traceable to public source. Agent-X meets each criterion:

- **OSI-approved license.** MIT. The full license text is in `LICENSE` and is unmodified.
- **Public source, public build.** The entire application source and the installer build script
  (`scripts/build-installers.ps1`, `installer/AgentX-Setup.iss`) are in the public repository.
  There are no private build steps and no closed-source binaries linked into the product.
- **No commercial gating.** The binary is the same software as the source — there is no "pro"
  edition, license server, or feature behind a paywall in the signed artifact.
- **Community benefit.** Agent-X is a privacy-first alternative to cloud document AI: it runs
  models locally (Ollama / bundled GGUF) and stores data in a SQLCipher-encrypted database, so
  sensitive documents never leave the user's machine. Signing removes the install-time friction
  that currently deters non-technical privacy-conscious users.
- **Active and maintained.** The project has a tagged release history, a full unit-test suite
  (1,900+ tests) gated in CI, a security audit trail (Codex audit + the 2026-06-19 QA audit,
  findings AX-QA-001…016 resolved in v2.1.2), and a documented release process.

---

## 4. Build & reproducibility

Signed artifacts must be reproducible from public source. Our release build is fully scripted and
deterministic in inputs:

1. **Publish.** `dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-x64
   --self-contained true -p:Platform=x64 -p:WindowsPackageType=None` → `publish/win-x64/`.
2. **Provenance gate.** Before packaging, `build-installers.ps1` asserts the freshly published
   `AgentX.Core.dll` actually contains the security types `LocalApiSecurity` and
   `ResolveContainedPath`, and prints the source commit. This exists because the public v2.1.1
   asset once shipped built from stale source (finding AX-QA-001); the gate makes that regression
   impossible to repeat.
3. **Package.** Inno Setup 6 compiles two profiles from the same publish output:
   - **SLIM** — `AgentX-Setup-<ver>-x64.exe` (~180 MB; model downloaded on first run; the GitHub
     release asset).
   - **OFFLINE** — `AgentX-Setup-<ver>-x64-offline.exe` (~2 GB; bundles the local GGUF model;
     hosted on Cloudflare R2).
4. **Hashes.** `installer-output/SHA256SUMS.txt` records the SHA-256 of every installer plus the
   source commit. Both the SLIM and OFFLINE artifacts are covered, so a single signed manifest
   proves the integrity of both regardless of where each is hosted.
5. **Keyless provenance (already live in CI).** `.github/workflows/release-provenance.yml`
   cosign-signs `SHA256SUMS.txt` using GitHub's ambient OIDC identity (no secrets), records the
   signature in the public **Rekor** transparency log, and attaches the detached signature +
   certificate to the GitHub release.

**Pinned toolchain** (declared in-repo for reproducibility): .NET SDK band via `global.json`;
self-contained win-x64 runtime; Inno Setup 6. A reviewer can rebuild from a clean checkout of a
release tag and compare `SHA256SUMS.txt`.

---

## 5. What we need signed

| Artifact | Where | Signing need |
|---|---|---|
| `AgentX.exe` | inside both installers | Authenticode — the executed binary |
| `AgentX-Setup-<ver>-x64.exe` (SLIM) | GitHub release | Authenticode — what users download & run |
| `AgentX-Setup-<ver>-x64-offline.exe` (OFFLINE) | Cloudflare R2 | Authenticode — alternative download |

> **Coordination note (OFFLINE size).** The OFFLINE installer is ~2 GB because it bundles the
> local model. If the signing service has a per-artifact size limit, the **SLIM** installer is the
> primary, always-signed GitHub asset; we will confirm the OFFLINE path with SignPath or, if
> necessary, restructure so the signed app/installer stub is decoupled from the bundled model.

---

## 6. Proposed signing integration

SignPath Foundation signs via a managed service (the certificate's private key stays in SignPath's
HSM). Two integration paths, in order of preference:

1. **CI submission (preferred).** Add the
   [`signpath/github-action-submit-signing-request`](https://github.com/SignPath/github-action-submit-signing-request)
   step to a release workflow: CI uploads the unsigned `AgentX.exe` / installer to SignPath, which
   signs and returns them. This keeps signing automated and auditable, and dovetails with the
   existing keyless provenance job.
2. **Portal/API submission (fallback).** The maintainer submits the artifacts produced locally by
   `build-installers.ps1` through the SignPath portal and downloads the signed results.

Either way, `build-installers.ps1` already isolates signing behind `-RequireSign` and a pluggable
sign step, so wiring SignPath in is a localized change. Local Authenticode via `signtool` remains
available for maintainers who hold their own certificate.

---

## 7. Maintainer & contact

- **Name:** Rocky Elsalaymeh
- **Email:** Elsalaymeh@gmail.com
- **GitHub:** https://github.com/Git-Rocky-Stack
- **Role:** Project owner and sole maintainer

---

## 8. Links for the reviewer

- Repository: https://github.com/Git-Rocky-Stack/Agent-X
- License: `LICENSE` (MIT)
- Release process & dual-layer signing model: `docs/RELEASE-SIGNING.md`
- Installer build script: `scripts/build-installers.ps1`
- Inno Setup definition: `installer/AgentX-Setup.iss`
- Keyless provenance workflow: `.github/workflows/release-provenance.yml`
- Security audit trail: `docs/COMPREHENSIVE-QA-AUDIT-2026-06-19.md`, `CHANGELOG.md` (v2.1.2)

---

## 9. Application checklist

- [ ] Repository is public and the MIT `LICENSE` is present and unmodified.
- [ ] Submit the application at <https://signpath.org/apply> (or the current Foundation intake URL).
- [ ] Provide the project description from §1 and the facts table from §2.
- [ ] Link the repository and `docs/RELEASE-SIGNING.md`.
- [ ] On approval, add the SignPath project/organization/signing-policy slugs to repository secrets
      and wire `signpath/github-action-submit-signing-request` into the release workflow.
- [ ] Record the approval date and certificate subject here for the audit trail.
