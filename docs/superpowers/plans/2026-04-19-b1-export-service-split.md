# B1: ExportService Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `ExportService.cs` (3,077 LOC) into an `IExportFormatter` interface + 8 formatter implementations + thin orchestrator, achieving ≤400 LOC per file.

**Architecture:** Introduce `IExportFormatter` with one implementation per export format. `ExportService` becomes a thin dispatcher that resolves the correct formatter via a dictionary/strategy pattern and delegates. Template engine wraps formatters with structured section injection.

**Tech Stack:** C#, .NET 8, QuestPDF (existing), DocumentFormat.OpenXml (existing), xUnit

---

### Task 1: IExportFormatter Interface + Markdown/PlainText/CSV Formatters

**Files:**
- Create: `src/AgentX.Core/Services/Export/Formatters/IExportFormatter.cs`
- Create: `src/AgentX.Core/Services/Export/Formatters/MarkdownFormatter.cs`
- Create: `src/AgentX.Core/Services/Export/Formatters/PlainTextFormatter.cs`
- Create: `src/AgentX.Core/Services/Export/Formatters/CsvFormatter.cs`
- Create: `tests/AgentX.Tests/Services/Export/Formatters/MarkdownFormatterTests.cs`
- Create: `tests/AgentX.Tests/Services/Export/Formatters/PlainTextFormatterTests.cs`
- Create: `tests/AgentX.Tests/Services/Export/Formatters/CsvFormatterTests.cs`

- [ ] **Step 1: Define IExportFormatter interface**

```csharp
// src/AgentX.Core/Services/Export/Formatters/IExportFormatter.cs
namespace AgentX.Core.Services.Export.Formatters;

public interface IExportFormatter
{
    ExportFormat Format { get; }
    string FileExtension { get; }
    string MimeType { get; }
    Task<ExportResult> ExportConversationAsync(Conversation conversation, ExportOptions options);
    Task<ExportResult> ExportConversationsAsync(IReadOnlyList<Conversation> conversations, ExportOptions options);
}
```

- [ ] **Step 2: Write failing tests for MarkdownFormatter**

Test cases: exports single conversation with messages, respects metadata flags, generates correct markdown headers, handles empty conversation, handles special characters in content.

- [ ] **Step 3: Extract Markdown formatting logic from ExportService into MarkdownFormatter**

Move the markdown generation logic (format headers, role labels, code blocks, metadata) from ExportService into MarkdownFormatter.

- [ ] **Step 4: Repeat for PlainTextFormatter and CsvFormatter**

- [ ] **Step 5: Run full export test suite**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~Export" --blame-hang-timeout 60s
```

---

### Task 2: HTML + JSON Formatters

**Files:**
- Create: `src/AgentX.Core/Services/Export/Formatters/HtmlFormatter.cs`
- Create: `src/AgentX.Core/Services/Export/Formatters/JsonFormatter.cs`
- Create: `tests/AgentX.Tests/Services/Export/Formatters/HtmlFormatterTests.cs`
- Create: `tests/AgentX.Tests/Services/Export/Formatters/JsonFormatterTests.cs`

- [ ] **Step 1: Write failing tests for HtmlFormatter**

Test cases: generates valid HTML with dark/light theme CSS, includes metadata section, handles code blocks with syntax highlighting, embeds images as base64 when configured.

- [ ] **Step 2: Extract HTML formatting logic from ExportService into HtmlFormatter**

Move HtmlExport helper usage and HTML generation logic.

- [ ] **Step 3: Write failing tests for JsonFormatter**

Test cases: produces valid JSON, includes all message fields (role, content, timestamp, citations), round-trips through deserialize, handles empty conversations.

- [ ] **Step 4: Extract JSON formatting logic from ExportService into JsonFormatter**

- [ ] **Step 5: Run full export test suite**

---

### Task 3: PDF + DOCX + PPTX Formatters

**Files:**
- Create: `src/AgentX.Core/Services/Export/Formatters/PdfFormatter.cs`
- Create: `src/AgentX.Core/Services/Export/Formatters/DocxFormatter.cs`
- Create: `src/AgentX.Core/Services/Export/Formatters/PptxFormatter.cs`
- Create: `tests/AgentX.Tests/Services/Export/Formatters/PdfFormatterTests.cs`
- Create: `tests/AgentX.Tests/Services/Export/Formatters/DocxFormatterTests.cs`
- Create: `tests/AgentX.Tests/Services/Export/Formatters/PptxFormatterTests.cs`

- [ ] **Step 1: Write failing tests for PdfFormatter**

Test cases: generates valid PDF bytes, includes branding header, applies correct font sizes, handles multi-page content, respects metadata flags.

- [ ] **Step 2: Extract QuestPDF logic from ExportService into PdfFormatter**

Move PdfExport helper usage and PDF document composition.

- [ ] **Step 3: Write failing tests for DocxFormatter**

Test cases: generates valid DOCX (OpenXML), includes paragraph formatting, handles tables for metadata.

- [ ] **Step 4: Extract OpenXML DOCX logic from ExportService into DocxFormatter**

- [ ] **Step 5: Write failing tests for PptxFormatter + extract**

- [ ] **Step 6: Run full export test suite**

---

### Task 4: Thin Orchestrator + Template Engine

**Files:**
- Modify: `src/AgentX.Core/Services/Export/ExportService.cs` (thin to ≤400 LOC)
- Create: `src/AgentX.Core/Services/Export/Templates/ExportTemplateEngine.cs`
- Create: `src/AgentX.Core/Services/Export/Templates/ExportTemplate.cs` (enum: Default, ResearchReport, ExecutiveSummary, AnnotatedBibliography)
- Modify: `tests/AgentX.Tests/Services/Export/ExportServiceTests.cs`

- [ ] **Step 1: Wire IExportFormatter implementations into ExportService via DI dictionary**

```csharp
public class ExportService
{
    private readonly Dictionary<ExportFormat, IExportFormatter> _formatters;

    public ExportService(IEnumerable<IExportFormatter> formatters, ...)
    {
        _formatters = formatters.ToDictionary(f => f.Format);
    }

    public async Task<ExportResult> ExportConversationAsync(...)
    {
        var formatter = _formatters[options.Format];
        return await formatter.ExportConversationAsync(conversation, options);
    }
}
```

- [ ] **Step 2: Add ExportTemplate enum and template engine**

Templates inject structured sections before/after formatter output (e.g., Research Report adds Executive Summary, Methodology, Findings, References sections).

- [ ] **Step 3: Update ExportOptions to include Template field**

- [ ] **Step 4: Update existing ExportServiceTests to work with new structure**

- [ ] **Step 5: Run full test suite**

```bash
dotnet test AgentX.sln --blame-hang-timeout 60s
```

---

## Verification Gate

All existing 16 export tests + new formatter tests must pass. ExportService.cs ≤ 400 LOC.

## Commit Strategy

- `refactor(export): IExportFormatter interface + Markdown/PlainText/CSV formatters`
- `refactor(export): HTML + JSON formatters`
- `refactor(export): PDF + DOCX + PPTX formatters`
- `refactor(export): thin orchestrator + template engine`
