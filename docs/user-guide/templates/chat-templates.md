# Chat Templates

**Prompts and questions to copy into Agent-X**

---

## Overview

Agent-X has no template picker in the chat. The prompts on this page are plain text: copy one,
replace the parts in square brackets such as `[topic]`, and paste it where it fits.

| Where to paste | Use it for | Keep in mind |
|----------------|------------|--------------|
| **Ask Your Files** | Questions about your own documents | The answer draws on the passages that match the question best (as many as **Top-K Results** in **Settings > Knowledge Vault** allows, 5 by default), not on every document in the vault. Pick a collection to narrow the search. |
| **AI Chat** | Writing, explaining, brainstorming and code | Chat does not read your Knowledge Vault. Paste the text you want the model to work on into the message. |
| A workflow step (`AiPrompt`) | Prompts you run again and again | Use `{{input}}` for the text you run the workflow on and `{{previous_output}}` for the previous step's result (see [Workflow templates](README.md#workflow-templates-workflows)). |

Some jobs have their own page, which usually works better than a prompt:

- **A summary or key points of one whole document:** Quick Actions > **Summarize** or
  **Key Points**.
- **Two or more documents side by side:** Compare Documents.
- **Translation of a passage:** Quick Actions > **Translate**, or the **Translator** system
  prompt in AI Chat.

---

## Questions for Ask Your Files

These work best when the answer is in a few passages. Each answer cites its sources as `[1]`,
`[2]` and so on.

### Extract Key Information

```
What deadlines, dates and milestones do [project name] documents mention?
```

```
Who are the people and organizations involved in [project or topic], and what is each one responsible for?
```

```
What figures do my documents give for [metric], and for which periods?
```

### Find Decisions and Action Items

```
What was decided about [subject], when, and what reasons were given?
```

```
List the open action items about [subject], with the owner and due date where the documents give them.
```

### Explain and Compare

```
How does [system or process] work, according to my documents? Explain it step by step.
```

```
Which approaches to [problem] do my documents describe, and how do they differ?
```

```
Where do my documents disagree about [topic]? Name the sources on each side.
```

### Check What You Have

```
What do my documents say about [topic]? If they say nothing, tell me.
```

```
Which of my documents discuss [concept], and what does each one say about it?
```

---

## Prompts for AI Chat

Paste the text you want to work on where the prompt says `[paste text here]`. A system prompt
from the prompt button (for example **Writing Editor** or **Code Helper**) can set the tone for
the whole conversation.

### Content Generation

**Generate Outline**

```
Generate a detailed outline for [topic].

Include:
- Main sections
- Sub-sections
- Key points to cover in each section

Organize it hierarchically with numbering.
```

**Brainstorm Ideas**

```
Brainstorm [number] ideas for [topic].

For each idea, give:
- A brief description
- Potential benefits
- Possible challenges
- Estimated complexity
```

**Create Checklist**

```
Create a checklist for [activity].

Include preparation steps, execution steps, verification steps and completion criteria.
Use checkboxes: [ ]
```

**Write Email Draft**

```
Draft an email about [purpose].

Recipient: [recipient]
Tone: [professional / friendly / formal / urgent]
Points to include: [key points]

Include a subject line and a clear call to action.
```

### Research Assistance

**Topic Overview**

```
Give an overview of [topic]: definition and background, the current state of knowledge,
key debates, and open questions. Say where you are unsure.
```

**Literature Review Structure**

```
Create a structure for a literature review on [topic]: introduction themes, categories of
research, gaps, and a suggested order. Give the framework, not the full text.
```

**Fact-Check Statements**

```
Check the following statements about [topic]:

- [statement 1]
- [statement 2]
- [statement 3]

For each one, rate it Confirmed / Partially True / False / Needs Context, and explain why.
```

For current facts, turn on Research Mode (the button next to the message box, after you enable
it in **Settings > Research Mode**) so the answer can use web search results.

### Technical

**Debug Code Issue**

```
Help me debug this issue.

Code:
[paste code here]

Error: [error message]
Symptoms: [what happens]

Explain the root cause, how to fix it, and how to prevent similar issues.
```

**Explain Code**

```
Explain this code: its purpose, how each part works, the techniques it uses, and any
concerns or improvements. Assume intermediate programming knowledge.

[paste code here]
```

**Generate SQL Query**

```
Write a SQL query for this requirement.

Schema:
[paste schema here]

Requirement: [requirement]

Comment the query and mention performance considerations.
```

### Writing Assistance

**Improve Writing**

```
Review this text and suggest improvements to clarity, grammar, tone and structure.
Then give a revised version and explain the main changes.

[paste text here]
```

**Change Tone**

```
Rewrite this text in a [target tone] tone. Keep the meaning; adjust word choice, sentence
structure and formality.

[paste text here]
```

**Create Abstract**

```
Write a 150 to 250 word abstract for the text below: the research question, the approach,
the key findings and the implications.

[paste text here]
```

### Productivity

**Meeting Agenda**

```
Create an agenda for a [duration] meeting about [topic] with [attendees].
Include objectives, agenda items with time allocations, preparation and expected outcomes.
```

**Project Timeline**

```
Create a timeline for [project]: phases, dependencies, estimated durations, deliverables and
risks. Present it as a table.
```

**Test Cases**

```
Write test cases for [feature]. For each: ID, description, preconditions, steps, expected
result and priority. Cover the happy path and edge cases.
```

---

## Prompts for Workflow Steps

In a workflow's `AiPrompt` step, the prompt runs on the workflow's input. Examples of steps you
can chain:

**Step 1: Summarize**

```
Summarize the following text in five sentences. Keep names, figures and dates.

{{input}}
```

**Step 2: Find risks**

```
Based on the summary below, list the risks and open questions, most important first.

Summary:
{{previous_output}}
```

**Step 3: Draft a reply**

```
Write a short reply to the author of the original text that thanks them, lists the risks
below, and asks the open questions.

Original text:
{{input}}

Risks and questions:
{{previous_output}}
```

The four built-in workflow templates (Summarize & Act, Research Brief, Document Review and
Content Repurpose) are complete examples of this pattern; **Use Template** copies one so you
can adapt its prompts.

---

## Best Practices

1. **Be specific.** Name the document type, the period, the audience and the format you want.
2. **Give context.** In AI Chat, paste the text; in Ask Your Files, use the words your
   documents use.
3. **Set constraints.** Ask for a length, a structure or a tone.
4. **Iterate.** Follow-up messages in the same AI Chat conversation keep its context.
5. **Check the sources.** In Ask Your Files, open the cited documents for anything that
   matters.
