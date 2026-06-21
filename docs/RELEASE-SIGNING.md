# Agent-X — Signed Release Process

Covers QA findings **AX-QA-001** (the public v2.1.1 asset predated the security remediation)
and **AX-QA-007** (distributed binaries are unsigned).

`scripts/build-installers.ps1` publishes Agent-X from current source and compiles the SLIM and
OFFLINE Inno Setup installers. It now also enforces a provenance gate, Authenticode-signs and
timestamps the artifacts, verifies the signatures, and records SHA-256 sums.

## Prerequisites

- **Inno Setup 6** (`ISCC.exe`) on PATH or in its default install location.
- **Windows SDK signing tools** (`signtool.exe`).
- A **code-signing certificate** — OV, or EV for instant SmartScreen reputation. Either:
  - imported into the Windows certificate store (preferred — reference it by **thumbprint**, so
    no secret appears on the command line), or
  - a **PFX** file + password.

## Build + sign (release)

Using a cert-store certificate (preferred):

```powershell
scripts/build-installers.ps1 -Profiles both -RequireSign `
    -CertificateThumbprint <THUMBPRINT> `
    -TimestampUrl http://timestamp.digicert.com
```

Using a PFX file:

```powershell
scripts/build-installers.ps1 -Profiles both -RequireSign `
    -CertificatePath C:\path\to\codesign.pfx -CertificatePassword <PWD>
```

`-RequireSign` makes a missing certificate a hard error, so a release run can never silently
produce unsigned artifacts. Omit the certificate parameters for an unsigned **dev** build (a
loud warning is printed and the artifacts must not be published).

## What the script enforces

1. **Provenance (AX-QA-001).** After publishing, it asserts the published `AgentX.Core.dll`
   contains the security types `LocalApiSecurity` and `ResolveContainedPath`. If they are absent
   the build aborts — this is exactly the regression that shipped in the public v2.1.1 asset,
   which was built from stale source. The source commit is printed.
2. **Signing + timestamp (AX-QA-007).** The app binary and both installers are signed with
   SHA-256 and an RFC-3161 timestamp, so signatures stay valid after the certificate expires.
3. **Verification.** Every signed file is checked with `Get-AuthenticodeSignature`; any status
   other than `Valid` aborts the build.
4. **Hashes.** `installer-output/SHA256SUMS.txt` records the SHA-256 of each installer plus the
   source commit, for the release notes.

## Release checklist (from the QA audit)

1. Stop distributing the existing unsigned v2.1.1 asset.
2. Build from a clean checkout of current `main` (no `-SkipPublish`), with `-RequireSign`.
3. Confirm the provenance gate passed and signatures verified `Valid`.
4. Publish `SHA256SUMS.txt`, the source commit, and a release note explaining that the prior
   asset did not contain the security remediation.
5. Smoke-test the signed installer on a clean Windows profile and a representative legacy-upgrade
   database before upload.

## Why signing is not in GitHub CI

The signing certificate must not leave the maintainer's protected environment, so signing runs
in this manual release pipeline rather than a normal CI job. CI covers everything that does not
need the certificate: build, tests, coverage, formatting, dependency + locale audits, and the
Android build.
