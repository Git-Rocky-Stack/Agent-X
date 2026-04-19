# B7: Structured Output (JSON Schema Enforcer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add JSON Schema enforcement to AI responses so structured outputs (tags, citations, summaries, analyses) are validated against schemas before being returned to callers.

**Architecture:** `IJsonSchemaEnforcer` validates AI responses against JSON schemas. Each provider gains structured output support via its native mechanism (OpenAI `response_format`, Anthropic tool_use, Ollama `format`, LocalLlm prompt wrapping). A `SchemaRegistry` holds pre-built schemas for common Agent-X outputs.

**Tech Stack:** C#, .NET 8, Microsoft.Extensions.AI v9.5.0 (already referenced), System.Text.Json, xUnit

---

### Task 1: IJsonSchemaEnforcer + JsonSchemaEnforcer + Tests

**Files:**
- Create: `src/AgentX.Core/AI/Schema/IJsonSchemaEnforcer.cs`
- Create: `src/AgentX.Core/AI/Schema/JsonSchemaEnforcer.cs`
- Create: `src/AgentX.Core/AI/Schema/ValidationResult.cs`
- Create: `tests/AgentX.Tests/AI/Schema/JsonSchemaEnforcerTests.cs`

- [ ] **Step 1: Define IJsonSchemaEnforcer interface**

```csharp
public interface IJsonSchemaEnforcer
{
    ValidationResult Validate(string response, string schemaKey);
    ValidationResult Validate<T>(string response);
    string? ExtractJsonBlock(string response);
    string BuildSchemaPrompt(string schemaKey);
}

public record ValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    JsonDocument? ParsedDocument = null);
```

- [ ] **Step 2: Write failing tests**

Tests: Validate accepts valid JSON matching schema, rejects invalid JSON, reports clear error messages, ExtractJsonBlock extracts ```json blocks, handles raw JSON responses, handles responses with surrounding text, Validate<T> deserializes to typed object, BuildSchemaPrompt generates instruction text.

- [ ] **Step 3: Implement JsonSchemaEnforcer**

Use System.Text.Json.Nodes for schema validation. Build a simple schema validator that checks required properties, types, and array constraints.

- [ ] **Step 4: Run tests**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~JsonSchemaEnforcer" --blame-hang-timeout 60s
```

---

### Task 2: Schema Registry + Common Schemas

**Files:**
- Create: `src/AgentX.Core/AI/Schema/SchemaRegistry.cs`
- Create: `src/AgentX.Core/AI/Schema/ISchemaRegistry.cs`
- Create: `src/AgentX.Core/AI/Schema/Schemas/TagSchema.cs`
- Create: `src/AgentX.Core/AI/Schema/Schemas/CitationSchema.cs`
- Create: `src/AgentX.Core/AI/Schema/Schemas/SummarySchema.cs`
- Create: `src/AgentX.Core/AI/Schema/Schemas/AnalysisSchema.cs`
- Create: `tests/AgentX.Tests/AI/Schema/SchemaRegistryTests.cs`

- [ ] **Step 1: Define ISchemaRegistry interface**

```csharp
public interface ISchemaRegistry
{
    JsonDocument GetSchema(string key);
    IEnumerable<string> GetAvailableSchemas();
    string GetSchemaAsPrompt(string key);
}
```

- [ ] **Step 2: Write failing tests**

Tests: GetSchema returns valid JSON for known keys, throws for unknown keys, GetAvailableSchemas lists all registered schemas, GetSchemaAsPrompt generates instruction text with schema embedded.

- [ ] **Step 3: Implement common schemas**

- **TagSchema**: `{ "type": "array", "items": { "type": "string" }, "maxItems": 10 }`
- **CitationSchema**: `{ "type": "array", "items": { "type": "object", "properties": { "title": {}, "url": {}, "snippet": {} } } }`
- **SummarySchema**: `{ "type": "object", "properties": { "summary": { "type": "string" }, "keyPoints": { "type": "array" } } }`
- **AnalysisSchema**: `{ "type": "object", "properties": { "analysis": { "type": "string" }, "confidence": { "type": "number" }, "sources": { "type": "array" } } }`

- [ ] **Step 4: Implement SchemaRegistry**

Pre-loads all schemas at construction. Converts schemas to prompt instructions.

- [ ] **Step 5: Run tests**

---

### Task 3: Provider Integration

**Files:**
- Modify: `src/AgentX.Core/AI/Providers/OpenAiProvider.cs` (429 LOC)
- Modify: `src/AgentX.Core/AI/Providers/AnthropicProvider.cs` (417 LOC)
- Modify: `src/AgentX.Core/AI/Providers/OllamaProvider.cs` (485 LOC)
- Modify: `src/AgentX.Core/AI/Providers/LocalLlmProvider.cs` (571 LOC)
- Create: `tests/AgentX.Tests/AI/Providers/StructuredOutputTests.cs`

- [ ] **Step 1: Add schema support to OpenAI provider**

Use `response_format: { "type": "json_object" }` when schema is requested. Prepend schema instruction to system prompt.

- [ ] **Step 2: Add schema support to Anthropic provider**

Use tool_use with JSON schema as the tool definition. Parse tool response as structured output.

- [ ] **Step 3: Add schema support to Ollama provider**

Use `format: "json"` with schema instruction in prompt. Validate response post-hoc.

- [ ] **Step 4: Add schema support to LocalLlm provider**

Prepend JSON schema instruction to prompt (no native support). Validate response post-hoc via JsonSchemaEnforcer.

- [ ] **Step 5: Write integration tests**

Tests: Each provider returns structured JSON when schema is requested, falls back gracefully when provider doesn't support structured output, validation catches malformed responses.

- [ ] **Step 6: Run tests**

---

### Task 4: ChatService Integration

**Files:**
- Modify: `src/AgentX.Core/Services/AI/ChatService.cs`
- Modify: `src/AgentX.Core/Services/AI/ChatOptions.cs` (add SchemaKey property)
- Create: `tests/AgentX.Tests/Services/AI/StructuredChatTests.cs`

- [ ] **Step 1: Add SchemaKey to ChatOptions**

```csharp
public string? SchemaKey { get; set; }
```

- [ ] **Step 2: Wire schema enforcement into ChatService**

When ChatOptions.SchemaKey is set:
1. Inject ISchemaRegistry + IJsonSchemaEnforcer
2. Pass schema to provider
3. Validate response post-hoc
4. Retry once if validation fails (with stronger prompt)

- [ ] **Step 3: Write end-to-end tests**

Tests: ChatService with SchemaKey returns validated structured output, retry on validation failure works, schema key not set returns normal text.

- [ ] **Step 4: Run full test suite**

```bash
dotnet test AgentX.sln --blame-hang-timeout 60s
```

---

## Verification Gate

All schema tests pass. Provider matrix verified. ChatService supports optional schema enforcement. No regressions in non-schema flows.

## Commit Strategy

- `feat(ai): IJsonSchemaEnforcer for structured AI output validation`
- `feat(ai): SchemaRegistry with common Agent-X schemas`
- `feat(ai): structured output support across all AI providers`
- `feat(ai): ChatService schema enforcement with retry`
