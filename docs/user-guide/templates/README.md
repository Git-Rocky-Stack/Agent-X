# Agent-X Templates

**Built-in prompts, workflows and export layouts, and text you can reuse**

---

## Overview

Agent-X ships four kinds of reusable structures. All of them are built into the app or kept
in its database; there is no templates folder to edit and no Settings page for templates.

| Kind | Where you use it | What is built in | Can you add your own? |
|------|------------------|------------------|-----------------------|
| [System prompts](#system-prompts-ai-chat) | AI Chat, prompt button in the top bar | 10 prompts | No |
| [Workflow templates](#workflow-templates-workflows) | Workflows page | 4 templates | Yes: copy a template or build a workflow, and share it as JSON |
| [Export templates](#export-templates-conversation-export) | AI Chat, **Export conversation** | 3 layouts | No |
| [Saved filters](#saved-filters-semantic-search) | Semantic Search, **Save** | none | Yes |

This folder also holds text you can copy into the app yourself:

| File | Contents |
|------|----------|
| [`chat-templates.md`](chat-templates.md) | Questions for Ask Your Files, prompts for AI Chat, and prompts for workflow steps |
| [`document-templates.md`](document-templates.md) | Markdown outlines for notes you write in your own editor and import into the Knowledge Vault |

Agent-X does not fill in placeholders for you, except `{{input}}` and `{{previous_output}}` in
workflow steps. In the prompts and outlines in this folder, text in square brackets such as
`[topic]` marks what you replace before sending or saving.

---

## System prompts (AI Chat)

A system prompt tells the model how to behave for the messages you send. In **AI Chat**, click
the prompt button in the top bar (its tooltip reads "System prompt"). The **System Prompt**
panel lists **No system prompt** and the ten built-in prompts; pick one and it applies to the
messages you send in this conversation. A new conversation starts without one.

| Name | Category | What it tells the model |
|------|----------|-------------------------|
| General Assistant | General | "You are a helpful, friendly assistant. Provide clear, concise answers." |
| Code Helper | Code | "You are an expert programmer. Write clean, well-documented code. Explain your approach." |
| Writing Editor | Writing | "You are a professional editor. Improve clarity, grammar, and style while preserving the author's voice." |
| Research Analyst | Analysis | "You are a thorough research analyst. Provide balanced, evidence-based analysis with clear citations." |
| Creative Writer | Creative | "You are a creative writer. Craft engaging, vivid prose with attention to narrative and style." |
| Data Analyzer | Analysis | "You are a data analyst. Extract insights from data, identify patterns, and present findings clearly." |
| Summarizer | General | "You are a summarization expert. Create concise, accurate summaries that capture key points." |
| Translator | General | "You are a professional translator. Provide accurate, natural translations while preserving meaning and tone." |
| Technical Explainer | General | "You are a technical communicator. Explain complex concepts in simple, accessible language with examples." |
| Socratic Teacher | General | "You are a Socratic teacher. Guide learning through thoughtful questions rather than direct answers." |

The prompts are in English. You cannot add or edit system prompts in the app; to steer a
single conversation another way, say what you want in your first message.

---

## Workflow templates (Workflows)

A workflow runs a fixed sequence of AI steps on text you give it; each step's output feeds
the next. Open **Workflows** from the rail or with `Ctrl+Shift+W`.

### The built-in templates

| Template | Steps | What you get |
|----------|-------|--------------|
| **Summarize & Act** | Summarize, Extract Key Points, Generate Action Items | A summary, a numbered list of key points and a prioritized checklist of action items |
| **Research Brief** | Topic Analysis, Identify Key Arguments, Generate Structured Brief | A brief with Executive Summary, Background, Key Findings, Opposing Perspectives, and Conclusions & Recommendations |
| **Document Review** | Document Summary, Strengths & Weaknesses, Improvement Suggestions | A review with suggestions ranked High, Medium and Low |
| **Content Repurpose** | Extract Core Message, Tweet Thread, Professional Email, Blog Post | The same content as a 5 to 8 post thread, an email and a 500 to 800 word blog post |

Templates are marked **Template** in the workflow list.

### Running and customizing a template

1. Select a template: from the list, or with **Select Template** under
   **START WITH A TEMPLATE** when no workflow is selected. The **Template Guide** says what it
   is best for, what you will get and what to try it with.
2. Paste or type your text in **Input** and click **Run**. The step outputs and the
   **Final Output** appear on the page; **Copy to Clipboard**, **Save as Document** (adds the
   result to the Knowledge Vault) and **Export Result** keep it. **Recent Runs** stores earlier
   results you can reopen without running again.
3. To change a template, click **Use Template**. Agent-X copies it into a workflow of your own
   and opens the **Workflow Editor**, where you can rename it, add, reorder and remove steps,
   and **Save Workflow**. The built-in templates themselves cannot be edited or deleted.

**New Workflow** (or **Create Blank Workflow**) starts an empty one. Workflows run only when
you click **Run**; there are no schedules or triggers.

### Step types and placeholders

Each step has a **Step Type**, a **Prompt Template** and, for some types, **Step settings
(JSON)**:

| Step type | What it does | Settings |
|-----------|--------------|----------|
| `AiPrompt` | Sends the prompt to the active AI model | none |
| `DocumentLookup` | Asks your Knowledge Vault the prompt as a question, like Ask Your Files, and passes the answer on | Optional, e.g. `{"collectionId": 3}` to search one collection |
| `TextTransform` | Changes the text without AI: `uppercase`, `lowercase`, `titlecase`, `trim`, `extract_lines`, `word_count`, `char_count`, `reverse_lines`, `deduplicate_lines`, `sort_lines`, `number_lines` | e.g. `{"transform": "lowercase"}` |
| `ConditionalBranch` | Chooses between two texts by a test on the previous output: `contains`, `not_contains`, `starts_with`, `ends_with`, `equals`, `matches`, `length_greater_than` | Required, e.g. `{"condition": "contains", "value": "urgent", "trueBranch": "Urgent: {{previous_output}}", "falseBranch": "{{previous_output}}"}` |
| `OutputFormat` | Reformats the text as `json`, `markdown`, `html`, `bullet_list` or `numbered_list`, with an optional prefix and suffix | e.g. `{"format": "bullet_list"}` |

In a prompt, `{{input}}` stands for the text you entered and `{{previous_output}}` for the
output of the step before. The editor checks the settings as you type and says what is wrong.

### Sharing a workflow

**Export JSON** copies the selected workflow to the clipboard as JSON; **Import Workflow**
opens a box to paste such JSON (it is pre-filled from the clipboard). The JSON carries the
workflow's `name`, `description`, `icon` and `category`, and its `steps`, each with
`stepOrder`, `name`, `stepType`, `promptTemplate`, `configJson` and optional model,
temperature and token overrides. A shortened example:

```json
{
  "name": "Meeting to Actions",
  "description": "Turn meeting notes into decisions and action items.",
  "category": "Productivity",
  "steps": [
    {
      "stepOrder": 0,
      "name": "Decisions",
      "stepType": "AiPrompt",
      "promptTemplate": "List the decisions made in these meeting notes.\n\n{{input}}"
    },
    {
      "stepOrder": 1,
      "name": "Action Items",
      "stepType": "AiPrompt",
      "promptTemplate": "From the notes and the decisions below, list the action items with owners.\n\nNotes:\n{{input}}\n\nDecisions:\n{{previous_output}}"
    },
    {
      "stepOrder": 2,
      "name": "Checklist",
      "stepType": "OutputFormat",
      "configJson": "{\"format\": \"bullet_list\"}"
    }
  ],
  "version": "1.0"
}
```

---

## Export templates (conversation export)

In **AI Chat**, **Export conversation** in the top bar opens the export dialog. With the
**Format** set to **Markdown (.md)**, **Template (optional)** arranges the conversation into a
document layout; for the other formats (HTML, PDF, JSON, plain text, CSV, Word, PowerPoint)
the template list is unavailable.

| Template | Sections |
|----------|----------|
| **Research report** | Introduction, Methodology, Findings, Discussion, Conclusion, References |
| **Executive summary** | Executive Summary, Key Findings, Recommendations |
| **Annotated bibliography** | Overview, Sources |

The templates place the messages of the conversation into these sections; they do not ask the
AI to rewrite anything.

---

## Saved filters (Semantic Search)

On **Semantic Search**, **Save** stores the current query with its search mode, advanced
filters and sort order. It then appears under **Saved Filters**, where you can apply it again
or remove it.

---

## Best Practices

1. **Pick the right page for the job.** Questions about your documents go to Ask Your Files;
   a whole-document summary to Quick Actions; a side-by-side comparison to Compare Documents;
   repeatable multi-step processing to Workflows.
2. **Copy a template before changing it.** **Use Template** leaves the built-in version intact.
3. **Keep workflow steps small.** One task per step makes a failing step easy to spot in the
   step outputs.
4. **Share workflows as JSON.** Export JSON and Import Workflow move a workflow between
   installations.
