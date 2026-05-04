# Chat Templates

**Pre-built prompts for common AI interactions**

---

## Overview

Chat templates provide ready-to-use prompts for common tasks:
- Document analysis
- Content generation
- Research assistance
- Technical tasks

---

## Document Analysis Templates

### Summarize Document

```
Please provide a comprehensive summary of the document {{document}}.

Include:
1. Main topic and purpose
2. Key points or arguments
3. Important conclusions or findings
4. Action items or recommendations

Keep the summary concise but comprehensive. Use bullet points where appropriate.
```

### Compare Documents

```
Compare the following documents:
{{documents}}

For each aspect, highlight:
- Similarities
- Differences
- Unique elements in each document

Provide a structured comparison with clear headings.
```

### Extract Key Information

```
From the document {{document}}, extract the following information:

1. Key dates and deadlines
2. Important people or stakeholders
3. Financial figures or metrics
4. Action items or next steps
5. Risks or concerns

Present the extracted information in a structured format.
```

### Explain Technical Document

```
Act as a technical communicator. Explain the document {{document}} to a non-technical audience.

Simplify complex concepts while maintaining accuracy. Use analogies where helpful. Organize the explanation with clear headings and subheadings.
```

---

## Content Generation Templates

### Generate Outline

```
Generate a comprehensive outline for {{topic}}.

Include:
- Main sections
- Sub-sections
- Key points to cover in each section

Organize hierarchically with appropriate numbering.
```

### Brainstorm Ideas

```
Brainstorm {{count}} ideas for {{topic}}.

For each idea, provide:
- Brief description
- Potential benefits
- Possible challenges
- Estimated complexity

Be creative and diverse in your suggestions.
```

### Create Checklist

```
Create a comprehensive checklist for {{activity}}.

Include:
- Preparation steps
- Execution steps
- Verification steps
- Completion criteria

Organize logically with clear checkboxes [ ].
```

### Write Email Draft

```
Draft an email for {{purpose}}.

Recipient: {{recipient}}
Tone: {{tone}} (Professional / Friendly / Formal / Urgent)
Key points to include: {{keyPoints}}

Include appropriate subject line and call to action.
```

---

## Research Assistance Templates

### Research Topic Overview

```
Provide a comprehensive overview of {{topic}}.

Cover:
- Definition and background
- Current state of knowledge
- Key debates or controversies
- Important researchers or works
- Future directions or open questions

Cite specific sources where available.
```

### Literature Review Structure

```
Create a structure for a literature review on {{topic}}.

Outline:
- Introduction themes
- Key categories of research
- Important works in each category
- Gaps in current research
- Suggested organization

Provide framework, not full content.
```

### Fact-Check Statements

```
Fact-check the following statements about {{topic}}:

{{#each statements}}
- {{this}}
{{/each}}

For each statement, verify:
- Accuracy
- Context or caveats
- Source reliability

Mark each as: Confirmed / Partially True / False / Needs Context.
```

### Find Related Concepts

```
From the document {{document}}, identify concepts and topics related to {{concept}}.

For each related concept, provide:
- Name of concept
- Relationship to main concept
- Brief explanation
- Potential applications
```

---

## Technical Templates

### Debug Code Issue

```
Act as a senior software engineer. Help debug the following issue:

{{code}}

Error: {{error}}

Symptoms: {{symptoms}}

Analyze:
1. Root cause
2. Why this error occurs
3. How to fix it
4. How to prevent similar issues

Provide code examples for the fix.
```

### Explain Code

```
Explain the following code:

{{code}}

Cover:
- Purpose and functionality
- How each part works
- Key techniques or patterns used
- Potential improvements or concerns

Assume intermediate programming knowledge.
```

### Generate SQL Query

```
Generate a SQL query for the following requirement:

Database schema: {{schema}}
Requirement: {{requirement}}

Include:
- Query with comments
- Explanation of approach
- Performance considerations

Use standard SQL syntax.
```

### API Integration Guide

```
Create a guide for integrating with the {{apiName}} API.

Include:
- Authentication method
- Key endpoints
- Request/response examples
- Error handling
- Rate limiting considerations
- Best practices
```

---

## Writing Assistance Templates

### Improve Writing

```
Review the following text and suggest improvements:

{{text}}

Focus on:
- Clarity and conciseness
- Grammar and mechanics
- Tone and style
- Structure and flow

Provide revised version with explanation of changes.
```

### Change Tone

```
Rewrite the following text in a {{targetTone}} tone:

{{text}}

Maintain the core meaning but adjust:
- Word choice
- Sentence structure
- Level of formality
- Emotional content
```

### Expand Content

```
Expand the following content with more detail:

{{content}}

Add:
- Supporting examples
- Deeper explanations
- Relevant context
- Anticipated questions

Maintain consistency with original style.
```

### Create Abstract

```

Create a 150-250 word abstract for the document {{document}}.

Include:
- Research question or problem
- Methodology or approach
- Key findings or results
- Implications or conclusions

Follow academic abstract conventions.
```

---

## Productivity Templates

### Create Meeting Agenda

```
Create a meeting agenda for {{meetingType}}.

Topic: {{topic}}
Duration: {{duration}}
Attendees: {{attendees}}

Include:
- Objectives
- Agenda items with time allocations
- Preparation requirements
- Expected outcomes
```

### Plan Project Timeline

```
Create a project timeline for {{project}}.

Include:
- Major phases or milestones
- Dependencies between phases
- Estimated duration for each phase
- Key deliverables
- Risk considerations

Present in tabular or Gantt format.
```

### Design Presentation Structure

```
Create a presentation structure for {{topic}}.

Target audience: {{audience}}
Duration: {{duration}}
Presentation type: {{type}} (Informative / Persuasive / Instructional)

Outline:
- Slide count and breakdown
- Key content per slide
- Visual suggestions
- Presenter notes
- Transition strategy
```

### Generate Test Cases

```
Generate test cases for {{featureOrFunction}}.

For each test case, include:
- Test case ID
- Description
- Preconditions
- Test steps
- Expected results
- Priority level

Cover both happy path and edge cases.
```

---

## Using Chat Templates

### Access Templates

1. Navigate to **AI Chat**
2. Click **[Templates]** button
3. Browse categories or search
4. Select template

### Customize Template

1. Template loads into chat input
2. Edit variables and content as needed
3. Click **Send** to execute

### Create Custom Template

1. Craft your ideal prompt in chat
2. Navigate to **Settings → Templates**
3. Click **[Save Current as Template]**
4. Provide name, description, category
5. Template saved for future use

---

## Template Variables

### Input Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `{{document}}` | Currently selected document | "Project Plan.pdf" |
| `{{documents}}` | Multiple selected documents | "3 documents" |
| `{{topic}}` | Subject or theme | "Machine Learning" |
| `{{count}}` | Number for generation | "10" |
| `{{text}}` | Selected text passage | "Selected paragraph..." |
| `{{code}}` | Selected code block | "function example() {...}" |

### Output Variables

Templates can specify desired output format:

```
Output format: Markdown with:
- H1 for main title
- H2 for sections
- Bullet points for lists
- Code blocks for examples
```

---

## Best Practices

1. **Be specific** — Detailed templates produce better results
2. **Provide context** — Include background information
3. **Set constraints** — Specify length, format, tone
4. **Iterate** — Refine templates based on results
5. **Categorize** — Organize templates for easy discovery

---

*Last updated: 2026-05-03*
