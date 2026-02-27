# Agent-X

**Private AI Command Center for Windows**

Agent-X is a local-first AI personal intelligence hub that runs entirely on your machine. Import your documents, ask questions, search semantically, and interact with large language models -- all without any data ever leaving your computer. No cloud services, no subscriptions, no telemetry.

Built on .NET 8.0 and WinUI 3, Agent-X provides a native Windows desktop experience with enterprise-grade document processing, vector search, and retrieval-augmented generation (RAG) powered by Ollama.

---

## Table of Contents

- [Features](#features)
- [Screenshots](#screenshots)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Build, Publish, and Installer](#build-publish-and-installer)
- [Project Structure](#project-structure)
- [Technology Stack](#technology-stack)
- [Architecture](#architecture)
- [Licensing](#licensing)
- [Contributing](#contributing)

---

## Features

### AI Chat

Stream conversations with locally-running large language models through Ollama. Manage multiple conversations, configure system prompts per chat, and switch between any downloaded model on the fly. All inference runs on your hardware -- nothing is sent externally.

### Knowledge Vault

Import documents in a wide range of formats: PDF, DOCX, TXT, Markdown, source code files, and images (via OCR). Documents are automatically chunked into semantically meaningful segments, embedded into vector representations, and stored in a local SQLite-backed vector database for instant retrieval.

### Semantic Search

Search across your entire document library using natural language queries. Agent-X uses vector similarity search (powered by sqlite-vec) to find content based on meaning rather than exact keyword matching, surfacing the most relevant passages regardless of wording.

### Ask Your Files (RAG)

Retrieval-augmented generation lets you ask questions in natural language and receive AI-generated answers grounded in your own documents. Every response includes citations pointing back to the specific source passages, so you can verify the information and trace it to its origin.

### Collections

Organize your documents into hierarchical collections with support for nested grouping and tagging. Collections provide a structured way to manage large document libraries by topic, project, or any organizational scheme that fits your workflow.

### Quick Actions

AI-powered utilities that operate on your documents with a single click:

- **Summarize** -- Generate concise summaries of any document.
- **Key Points** -- Extract the most important takeaways.
- **Translate** -- Translate document content to another language.
- **Duplicate Detection** -- Identify near-duplicate documents in your vault.
- **Organization Suggestions** -- Get AI-driven recommendations for how to categorize and structure your library.

### Model Manager

Download, manage, and delete Ollama models directly from the application. The Model Manager displays model metadata (size, quantization, parameter count) and provides hardware-aware recommendations to help you select models that will perform well on your system.

### Hardware Advisor

WMI-based hardware detection profiles your GPU, CPU, RAM, and NPU capabilities, then recommends which models are appropriate for your configuration. Know before you download whether a model will run smoothly, require compromise, or exceed your hardware limits.

### Dashboard

A real-time overview of your Agent-X workspace: total documents indexed, file type distribution, recent activity, indexing status, and system health. The dashboard provides an at-a-glance summary of your entire knowledge base.

### Command Palette

Press `Ctrl+K` to open a fast, keyboard-driven command palette for navigating between pages, executing actions, and searching your workspace without touching the mouse.

### Onboarding Wizard

A guided five-step setup that walks new users through initial configuration:

1. Welcome and introduction
2. Ollama connection setup and verification
3. Model selection and download
4. License activation
5. Configuration summary and launch

### Settings

Comprehensive settings covering general preferences, inference parameters (temperature, top-p, context length), indexing behavior, appearance customization, and license management.

---

## Screenshots

*Screenshots are located in the `/screenshots` directory.*

| View | Description |
|------|-------------|
| Dashboard | Real-time workspace overview with stats and activity |
| AI Chat | Streaming conversation interface with model selection |
| Knowledge Vault | Document library with import and management controls |
| Semantic Search | Natural language search with relevance-ranked results |
| Ask Your Files | RAG interface with citations and source references |
| Model Manager | Ollama model download and management panel |
| Hardware Advisor | System hardware profile and model recommendations |
| Settings | Application configuration and license management |

---

## Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| Windows | 10 (1903+) or 11 | x64 architecture |
| .NET SDK | 8.0 | Required for building from source |
| Windows App SDK | 1.5+ | Included via NuGet on build |
| Visual Studio | 2022 (17.8+) | Workloads: ".NET Desktop Development" and "Windows App SDK C# Templates" |
| Ollama | Latest | Required for AI chat, embeddings, and RAG features |
| Inno Setup | 6 | Optional -- only needed to build the installer |

**For end users:** The published installer (`AgentX-Setup-1.0.0-x64.exe`) is fully self-contained and does not require the .NET SDK or Visual Studio. Only Ollama is needed for AI functionality.

---

## Quick Start

### 1. Clone the Repository

```bash
git clone <repository-url> Agent-X
cd Agent-X
```

### 2. Restore and Build

```bash
dotnet restore AgentX.sln
dotnet build AgentX.sln
```

### 3. Run the Application

```bash
dotnet run --project src/AgentX.App/AgentX.App.csproj
```

The onboarding wizard will launch on first run and guide you through connecting to Ollama, selecting a model, and activating your license.

### 4. Install Ollama (if not already installed)

Download Ollama from [https://ollama.com](https://ollama.com) and ensure it is running before launching Agent-X. The application connects to Ollama's local API (default: `http://localhost:11434`) for all AI operations.

---

## Build, Publish, and Installer

### Development Build

```bash
dotnet build AgentX.sln
```

### Run Unit Tests

```bash
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj
```

### Publish Self-Contained Binary

Produces a standalone executable that does not require .NET to be installed on the target machine:

```bash
dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-x64 --self-contained -o publish/win-x64
```

The output is written to `publish/win-x64/`.

### Build the Installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) to be installed:

```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/AgentX-Setup.iss
```

The compiled installer is output to `installer-output/AgentX-Setup-1.0.0-x64.exe`.

---

## Project Structure

```
Agent-X/
|
|-- AgentX.sln                          Solution file
|-- Directory.Build.props               Shared build properties (version, company, language)
|
|-- src/
|   |-- AgentX.App/                     WinUI 3 desktop application
|   |   |-- Views/                      XAML pages (Dashboard, Chat, KnowledgeVault, Search, etc.)
|   |   |-- ViewModels/                 MVVM view models (CommunityToolkit.Mvvm)
|   |   |-- Controls/                   Custom WinUI controls
|   |   |-- Converters/                 XAML value converters
|   |   |-- Styles/                     Application styles and themes
|   |   |-- Services/                   App-layer services (navigation, dialogs, etc.)
|   |   |-- Helpers/                    UI helper utilities
|   |   |-- Assets/                     Icons, images, and brand assets
|   |   |-- MainWindow.xaml             Application shell and navigation
|   |   +-- App.xaml                    Application entry point and resource configuration
|   |
|   +-- AgentX.Core/                    Core logic library (no UI dependency)
|       |-- AI/                         Ollama integration, model management, hardware detection
|       |   |-- Models/                 AI-related data models
|       |   +-- Providers/              AI provider abstractions and implementations
|       |-- Data/                       Entity Framework Core context, entities, migrations
|       |-- Documents/                  Document processing (PDF, DOCX, TXT, MD, code, OCR)
|       |-- Search/                     Semantic search, RAG pipeline, citation service
|       |   +-- Models/                 Search result and citation models
|       |-- Services/                   Core business services
|       +-- Helpers/                    Shared utility helpers
|
|-- tests/
|   +-- AgentX.Tests/                   xUnit test project
|
|-- installer/
|   +-- AgentX-Setup.iss                Inno Setup installer script
|
|-- publish/
|   +-- win-x64/                        Self-contained published binaries
|
|-- installer-output/
|   +-- AgentX-Setup-1.0.0-x64.exe      Compiled installer
|
|-- screenshots/                        Application screenshots
+-- docs/                               Documentation
```

---

## Technology Stack

| Category | Technology | Version | Purpose |
|----------|-----------|---------|---------|
| Runtime | .NET | 8.0 | Application framework |
| UI Framework | WinUI 3 (Windows App SDK) | 1.5+ | Native Windows desktop UI |
| Language | C# | 12.0 | Primary language |
| MVVM | CommunityToolkit.Mvvm | 8.3.2 | Observable properties, commands, messaging |
| Database | SQLite via EF Core | 8.0.10 | Document metadata, conversations, settings |
| Vector Storage | sqlite-vec | -- | Embedding storage and similarity search |
| AI Runtime | Ollama (via OllamaSharp) | 4.0.10 | Local LLM inference and embedding generation |
| PDF Processing | PdfSharpCore | 1.3.65 | PDF text extraction |
| DOCX Processing | DocumentFormat.OpenXml | 3.2.0 | Word document text extraction |
| Logging | Serilog + Serilog.Sinks.File | -- | Structured file logging |
| Installer | Inno Setup | 6 | Windows installer packaging |
| Testing | xUnit | -- | Unit and integration testing |

---

## Architecture

Agent-X follows a clean three-layer architecture:

```
+-----------------------------------------------------+
|                    AgentX.App                        |
|   WinUI 3 Views  |  ViewModels  |  App Services     |
+-----------------------------------------------------+
                          |
                          v
+-----------------------------------------------------+
|                   AgentX.Core                        |
|   AI Services | Document Processing | Search/RAG    |
|   Data Layer  | Hardware Detection  | Embeddings     |
+-----------------------------------------------------+
                          |
                          v
+-----------------------------------------------------+
|                External Dependencies                 |
|   Ollama (local)  |  SQLite  |  sqlite-vec          |
+-----------------------------------------------------+
```

- **AgentX.App** -- The presentation layer. Contains all XAML views, view models, value converters, custom controls, and navigation logic. Depends on AgentX.Core for all business logic.
- **AgentX.Core** -- The domain and infrastructure layer. Contains AI service abstractions and Ollama integration, document processing pipelines, semantic search and RAG, Entity Framework Core data access, and hardware detection. Has no dependency on the UI framework.
- **AgentX.Tests** -- Unit and integration tests targeting AgentX.Core.

For a detailed architecture breakdown including data flow diagrams, service dependency graphs, and extension points, see [ARCHITECTURE.md](./ARCHITECTURE.md).

---

## Licensing

Agent-X is proprietary commercial software developed by Rocky Stack / Strategia.

All rights reserved. Copyright (c) 2026 Rocky Stack.

### Pricing Tiers

| Tier | Price | Description |
|------|-------|-------------|
| Starter | $79 | Core features for individual use |
| Professional | $149 | Full feature set for power users |
| Ultimate | $249 | Complete suite with priority support |

All tiers are one-time purchases. No subscriptions, no recurring fees.

License activation is managed within the application under Settings > License Management.

---

## Contributing

Agent-X is not currently accepting external contributions. For development setup instructions, coding conventions, and internal contribution workflows, see [DEVELOPER-GUIDE.md](./DEVELOPER-GUIDE.md).

---

## Support

For bug reports, feature requests, and support inquiries, contact the development team at Rocky Stack / Strategia.

---

*Agent-X v1.0.0 -- Built by Rocky Stack / Strategia*
