# Agent-X Templates

**Pre-built templates for common tasks**

---

## Overview

Templates in Agent-X provide ready-to-use structures for:
- Document organization
- Chat prompts
- Workflows
- Searches

---

## Available Templates

### Document Templates

| Template | Description | File |
|----------|-------------|------|
| **Project Brief** | Structured project documentation | `document-templates.md` |
| **Meeting Notes** | Meeting record template | `document-templates.md` |
| **Research Summary** | Research synthesis structure | `document-templates.md` |
| **Technical Spec** | Technical specification format | `document-templates.md` |

### Chat Templates

| Template | Description | File |
|----------|-------------|------|
| **Summarize Document** | Extract key points from document | `chat-templates.md` |
| **Compare Documents** | Side-by-side comparison | `chat-templates.md` |
| **Extract Insights** | Pull insights from multiple docs | `chat-templates.md` |
| **Generate Outline** | Create document outline | `chat-templates.md` |
| **Explain Concept** | Simplify complex topics | `chat-templates.md` |

---

## Using Templates

### Document Templates

1. Navigate to **Knowledge Vault**
2. Click **[+ New from Template]**
3. Select template from dialog
4. Fill in template fields
5. Save as document

### Chat Templates

1. Navigate to **AI Chat**
2. Click **[Templates]** button
3. Select template
4. Template appears in chat input
5. Customize and send

### Workflow Templates

1. Navigate to **Workflows**
2. Click **[+ Create from Template]**
3. Select workflow template
4. Configure parameters
5. Save and run workflow

---

## Creating Custom Templates

### Document Template Format

```markdown
---
name: My Template
description: Template description
category: Custom
tags: [tag1, tag2]
---

# {{title}}

**Date:** {{date}}
**Author:** {{author}}

## Section 1

Content placeholder...

## Section 2

Content placeholder...
```

### Chat Template Format

```markdown
---
name: My Prompt Template
description: What this template does
category: Custom
---

Act as an expert in {{field}}.

Task: {{task}}

Context:
{{context}}

Please provide:
1. {{requirement_1}}
2. {{requirement_2}}
3. {{requirement_3}}
```

### Workflow Template Format

```json
{
  "name": "My Workflow",
  "description": "Workflow description",
  "category": "Custom",
  "steps": [
    {
      "type": "import",
      "parameters": {
        "source": "{{source}}",
        "autoTag": true
      }
    },
    {
      "type": "index",
      "parameters": {
        "forceReindex": true
      }
    },
    {
      "type": "chat",
      "parameters": {
        "template": "summarize",
        "context": "{{documents}}"
      }
    }
  ]
}
```

---

## Template Variables

### Built-in Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `{{title}}` | Document title | "Project Plan" |
| `{{date}}` | Current date | "2026-05-03" |
| `{{time}}` | Current time | "14:30" |
| `{{author}}` | Current user | "Rocky" |
| `{{document}}` | Selected document | "Project Plan.pdf" |
| `{{documents}}` | Selected documents | "3 documents" |
| `{{collection}}` | Collection name | "Work Projects" |

### Custom Variables

Add custom variables using `{{variableName}}` syntax:

```markdown
## Project: {{projectName}}
**Client:** {{clientName}}
**Deadline:** {{deadline}}

Budget: {{budget}}
Team: {{teamMembers}}
```

When using the template, Agent-X prompts for each variable value.

---

## Best Practices

1. **Use descriptive names**
   - Clear template names improve discoverability
   - Include purpose in name

2. **Provide examples**
   - Include sample content in templates
   - Helps users understand expected format

3. **Categorize appropriately**
   - Assign meaningful categories
   - Improves template organization

4. **Tag liberally**
   - Add relevant tags to templates
   - Enhances searchability

5. **Version important templates**
   - Include version in name for major revisions
   - Maintain backward compatibility

---

## Template Locations

Templates are stored in:

```
%LocalAppData%\AgentX\templates\
├── documents\
│   ├── project-brief.md
│   ├── meeting-notes.md
│   └── ...
├── chat\
│   ├── summarize.md
│   ├── compare.md
│   └── ...
└── workflows\
    ├── weekly-digest.json
    ├── import-and-tag.json
    └── ...
```

---

## Sharing Templates

### Export Template

1. Navigate to **Settings → Templates**
2. Select template to export
3. Click **[Export]**
4. Save to file

### Import Template

1. Navigate to **Settings → Templates**
2. Click **[Import]**
3. Select template file
4. Import completes

---

## Advanced Template Features

### Conditional Blocks

```markdown
{{#if includeBudget}}
## Budget Considerations
Total budget: {{budget}}
{{/if}}

{{#if includeTimeline}}
## Timeline
Phase 1: {{phase1Date}}
Phase 2: {{phase2Date}}
{{/if}}
```

### Repeating Sections

```markdown
## Team Members

{{#each teamMembers}}
- **{{name}}**: {{role}}
{{/each}}
```

### Nested Templates

Include one template within another:

```markdown
{{>meeting-header}}

## Main Content

Meeting notes go here...

{{>action-items}}
```

---

*Last updated: 2026-05-03*
