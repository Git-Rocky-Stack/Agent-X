# Agent-X

Local-first AI document intelligence for Windows. Native .NET 8 / WinUI 3 desktop app (`src/AgentX.App`), core library (`src/AgentX.Core`), tests (`src/AgentX.Tests`), MAUI Android companion (`src/AgentX.Mobile`).

Build: `dotnet build -p:Platform=x64` (a bare `dotnet build` fails with win-anycpu). Tests live in `AgentX.Tests`; after App-project edits, never trust `--no-build` results.

## Design System

Always read `DESIGN.md` before making any visual or UI decisions.
All font choices, colors, spacing, depth recipes, lamp semantics, and aesthetic direction are defined there (Command Console, Carbon Pro chassis, armed red `#AA2024`).
Do not deviate without explicit user approval.
In QA mode, flag any code that does not match `DESIGN.md`.
HighContrast theme is exempt from the hardware skin and must stay bound to `SystemColor*` tokens.
Writing style for docs and code comments in this repo: no em dashes, no decorative glyphs, plain ASCII.
