# Agent-X — Signed Release Process

Covers QA findings **AX-QA-001** (the public v2.1.1 asset predated the security remediation)
and **AX-QA-007** (distributed binaries are unsigned).

`scripts/build-installers.ps1` publishes Agent-X from current source and compiles the SLIM and
OFFLINE Inno Setup installers. It now also enforces a provenance gate, Authenticode-signs and
timestamps the artifacts, verifies the signatures, and records SHA-256 sums.

## The two signing layers

Agent-X uses **two complementary signatures**, each answering a different question:

| | Layer 1 — Authenticode | Layer 2 — Keyless provenance |
|---|---|---|
| **Answers** | "Can Windows trust this publisher?" | "Did this artifact come from the public build pipeline, unmodified?" |
| **Tool** | `signtool` (this script) | `cosign` (CI) |
| **Where it runs** | **Locally**, by the maintainer | **GitHub Actions** (`release-provenance.yml`) |
| **Key material** | A real code-signing certificate | **None** — ephemeral, via GitHub OIDC → Fulcio |
| **Visible to** | Every Windows user (removes SmartScreen "Unknown Publisher") | Anyone who runs `cosign verify-blob` |
| **Recorded in** | The signature + RFC-3161 timestamp | The public **Rekor** transparency log |

They are not substitutes. Authenticode is what suppresses the Windows install warning; cosign/Rekor
is cryptographic supply-chain provenance for the security-conscious. The rest of this document
covers Layer 1 (local); Layer 2 is described under
[CI keyless provenance](#layer-2--ci-keyless-provenance-cosign--rekor) below.

> **No cert yet?** A free Authenticode certificate for open-source projects is being pursued through
> the SignPath Foundation — see [`SIGNPATH-APPLICATION.md`](SIGNPATH-APPLICATION.md). Layer 2
> (cosign/Rekor) is already live and needs no certificate.

## Layer 1 — Authenticode (local)

### Prerequisites

- **Inno Setup 6** (`ISCC.exe`) on PATH or in its default install location.
- **Windows SDK signing tools** (`signtool.exe`).
- A **code-signing certificate** — OV, or EV for instant SmartScreen reputation. Either:
  - imported into the Windows certificate store (preferred — reference it by **thumbprint**, so
    no secret appears on the command line), or
  - a **PFX** file + password.

### Build + sign (release)

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

### What the script enforces

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

### Why Authenticode stays local

The signing **certificate must not leave the maintainer's protected environment**, so Authenticode
signing runs in this manual release pipeline rather than a normal CI job. CI covers everything that
does not need the certificate: build, tests, coverage, formatting, dependency + locale audits, the
Android build — and Layer 2 provenance below, which needs **no** secret.

## Release checklist (from the QA audit)

1. Stop distributing the existing unsigned v2.1.1 asset.
2. Build from a clean checkout of current `main` (no `-SkipPublish`), with `-RequireSign`.
3. Confirm the provenance gate passed and signatures verified `Valid`.
4. Publish `SHA256SUMS.txt`, the source commit, and a release note explaining that the prior
   asset did not contain the security remediation.
5. Smoke-test the signed installer on a clean Windows profile and a representative legacy-upgrade
   database before upload.

## Layer 2 — CI keyless provenance (cosign + Rekor)

`.github/workflows/release-provenance.yml` adds supply-chain provenance **in CI, with no secret and
no long-lived key**. When a GitHub Release is published (or via manual `workflow_dispatch` for a
tag), the workflow:

1. Downloads `SHA256SUMS.txt` from the release (written by `build-installers.ps1`).
2. Runs `cosign sign-blob` on it using GitHub's **ambient OIDC token** (`id-token: write`). Sigstore
   **Fulcio** issues an ephemeral certificate bound to the workflow identity
   (`https://github.com/Git-Rocky-Stack/Agent-X/.github/workflows/release-provenance.yml@<ref>`), and
   the signature is recorded in the public **Rekor** transparency log.
3. Self-verifies the signature, then uploads `SHA256SUMS.txt.sig` and `SHA256SUMS.txt.pem` back to
   the release as assets.

**Why sign the manifest, not each installer.** `SHA256SUMS.txt` lists the SHA-256 of both the SLIM
installer (GitHub asset) and the OFFLINE installer (~2 GB, on Cloudflare R2). Signing the one small
manifest transitively proves the integrity of **every** artifact — including the R2-hosted one CI
never downloads — and keeps the job fast. The chain is: *cosign proves the manifest is authentic →
the manifest's hash proves your download is authentic.*

This layer needs no certificate, so it runs today regardless of the Authenticode (Layer 1) status.

## Verifying a download

Anyone can prove a downloaded installer came from this repository's release pipeline, unmodified.
Requires [`cosign`](https://docs.sigstore.dev/cosign/installation/) and the GitHub CLI (`gh`):

```bash
# 1) Fetch the manifest and its signature + certificate from the release
gh release download <TAG> -R Git-Rocky-Stack/Agent-X \
  --pattern 'SHA256SUMS.txt' --pattern 'SHA256SUMS.txt.sig' --pattern 'SHA256SUMS.txt.pem'

# 2) Prove the manifest was signed by this repo's release-provenance workflow
cosign verify-blob \
  --certificate SHA256SUMS.txt.pem \
  --signature SHA256SUMS.txt.sig \
  --certificate-identity-regexp '^https://github.com/Git-Rocky-Stack/Agent-X/\.github/workflows/release-provenance\.yml@' \
  --certificate-oidc-issuer 'https://token.actions.githubusercontent.com' \
  SHA256SUMS.txt

# 3) Check your downloaded installer's hash against the now-trusted manifest
sha256sum -c SHA256SUMS.txt --ignore-missing
```

A `Verified OK` from step 2 plus an `OK` from step 3 means the installer you hold is byte-for-byte
what this repository's CI signed. On Windows without `sha256sum`, use
`Get-FileHash <installer> -Algorithm SHA256` and compare against the matching line in
`SHA256SUMS.txt`.

> **Authenticode (Layer 1) vs. this (Layer 2).** A SmartScreen-clean install comes from the
> Authenticode signature; this cosign check is the independent, public-transparency-logged proof of
> origin. Verifying either is optional for users but both are published for every release.
