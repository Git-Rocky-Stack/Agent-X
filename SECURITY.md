# Security Policy

Agent-X is a local-first desktop application. It keeps the text it extracts from your
documents, their embeddings, your conversations and your settings on your own machine, under
`%LOCALAPPDATA%\AgentX`. By default the AI runs on your computer (the built-in model, or
Ollama if you choose it) and nothing is sent to a cloud service. Content leaves the machine
only through features you turn on or use: an OpenAI or Anthropic API key, Research Mode web
search, the calendar and email connectors, or web import. Model downloads (the built-in model,
the speech-to-text model, Ollama models) fetch files but send none of your content. The SQLite
database can be encrypted from **Settings > Database Encryption** (SQLCipher, with a key tied
to your Windows account); API keys, the local API token and OAuth client secrets are always
stored encrypted with Windows DPAPI.

That design makes the threat model unusual for a desktop app: the most valuable asset is the
local vault, and the most sensitive code paths are the ones that can read it, unlock it, or
move data off the machine.

## Supported versions

| Version | Supported |
| ------- | --------- |
| 2.2.x   | Yes       |
| 2.1.x   | No        |
| < 2.1   | No        |

Agent-X is maintained by a single author. Security fixes land on the latest release only.
If you are running an older build, upgrade before reporting. The version you are running is
shown at the bottom of the Settings page.

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Report privately through GitHub Security Advisories:

1. Go to the [Security tab](https://github.com/Git-Rocky-Stack/Agent-X/security/advisories)
2. Choose **Report a vulnerability**
3. Include the version, your OS build, reproduction steps, and the impact you believe it has

A proof of concept helps a great deal, especially for anything touching the vault, the
local REST API, the plugin host, or the mobile pairing transport.

### What to expect

This is a solo-maintained project, so please calibrate expectations accordingly:

- Acknowledgement of your report within 7 days
- An assessment, including whether it is accepted as a vulnerability, within 30 days
- Credit in the release notes when a fix ships, unless you ask otherwise

If you do not hear back within 30 days, please open a public issue saying only that you
filed a private advisory and have not had a response. Do not include details.

## Scope

In scope:

- The desktop application (`src/AgentX.App`, `src/AgentX.Core`)
- The local REST API and its authentication. It listens on `http://localhost:9846` only, is
  on by default, and every route except its health check requires the bearer token from
  **Settings > Connections**.
- The mobile companion pairing and transport (`src/AgentX.Mobile`)
- The plugin host: manifest validation, installation and loading. Plugins are not
  sandboxed: each loads into its own assembly load context, but runs inside Agent-X with your
  Windows account's rights, as the [plugin guide](docs/PLUGIN-DEVELOPMENT-GUIDE.md) states.
- The browser extension (`browser-extension/`)
- The installer and release pipeline (`installer/`, `scripts/`)

Out of scope:

- Vulnerabilities in third-party model weights or in an LLM's output content
- Issues that require an attacker to already have administrator access to the machine
- Findings against an unsupported version
- Social engineering, physical access, and denial of service against your own machine

## How releases are verified

Release artifacts are covered by a two-layer provenance model. Details, including the
`cosign verify-blob` command for checking a download, are in
[`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md).

Prior security work on this project is recorded in the [CHANGELOG](CHANGELOG.md), most
substantially in the 2.1.2 release, which closed the security audit recorded in
[`AGENTX_AUDIT_FINDINGS.md`](AGENTX_AUDIT_FINDINGS.md) and the full QA audit in
[`docs/COMPREHENSIVE-QA-AUDIT-2026-06-19.md`](docs/COMPREHENSIVE-QA-AUDIT-2026-06-19.md).
