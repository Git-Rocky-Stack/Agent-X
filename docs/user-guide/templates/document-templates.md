# Document Templates

**Markdown outlines for notes you write and then import into Agent-X**

---

## How to use these outlines

Agent-X does not create documents from templates. These outlines are for your own editor:

1. Copy an outline below into a new file in any text editor.
2. Replace the parts in square brackets, delete the sections you do not need, and save the
   file with the `.md` extension.
3. Bring it into Agent-X: **Import Files** in the Knowledge Vault, or save it in a folder you
   added under **Settings > Knowledge Vault > Watch Folders**, which imports new and changed
   files while Agent-X runs.

Agent-X indexes the text of the file. Tags come from the AI model after indexing, not from the
file, so the outlines have no tag or metadata block. Using the same headings in every note of
a kind (for example **Action Items** in every set of meeting notes) makes targeted questions in
Ask Your Files, such as "List the action items from the March meetings", easier to answer.

---

## Project Brief Template

```markdown
# [Project name]

**Created:** [date]
**Author:** [name]
**Status:** [Draft / In Progress / Completed]

## Executive Summary

[Two or three sentences on what the project is and why it matters.]

## Objectives

- [Objective 1]
- [Objective 2]

## Scope

### In Scope

- [Item]

### Out of Scope

- [Item]

## Timeline

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| [Phase 1] | [Duration] | [Deliverables] |
| [Phase 2] | [Duration] | [Deliverables] |
| [Phase 3] | [Duration] | [Deliverables] |

## Resources

**Team:** [names and roles]
**Budget:** [amount]
**Tools:** [tools]

## Risks and Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| [Risk 1] | [Impact] | [Mitigation] |
| [Risk 2] | [Impact] | [Mitigation] |

## Success Criteria

- [Criterion 1]
- [Criterion 2]
```

---

## Meeting Notes Template

```markdown
# [Meeting title]

**Date:** [date]
**Time:** [start] - [end]
**Location:** [room or link]
**Attendees:** [names]

## Meeting Purpose

[Why the meeting took place.]

## Agenda Items

### [Agenda item 1]

**Discussion:** [summary]
**Decision:** [decision, if any]

### [Agenda item 2]

**Discussion:** [summary]
**Decision:** [decision, if any]

## Decisions Made

- **[Topic]:** [decision]

## Action Items

| Task | Owner | Due Date | Status |
|------|-------|----------|--------|
| [Task] | [Owner] | [Date] | [Open / Done] |
| [Task] | [Owner] | [Date] | [Open / Done] |

## Next Meeting

**Date:** [date]
**Agenda:** [topics]
```

---

## Research Summary Template

```markdown
# [Research topic]

**Research Date:** [date]
**Researcher:** [name]

## Research Question

[The question this research answers.]

## Methodology

[How the research was done.]

## Sources

- [Title] - [Author] ([Year]), [URL or location], accessed [date]
- [Title] - [Author] ([Year]), [URL or location], accessed [date]

## Key Findings

### [Finding 1]

[Description.]

**Relevance:** [why it matters]
**Confidence:** [High / Medium / Low]

### [Finding 2]

[Description.]

**Relevance:** [why it matters]
**Confidence:** [High / Medium / Low]

## Analysis

[What the findings mean together.]

## Limitations

- [Limitation]

## Conclusions

[Conclusions.]

## Recommendations

1. [Recommendation]
2. [Recommendation]

## Further Research

- [Open question]

## Related Documents

- [Document name]
```

---

## Technical Specification Template

````markdown
# [Feature name] - Technical Specification

**Version:** [version]
**Status:** [Draft / Review / Approved]
**Author:** [name]
**Reviewers:** [names]

## Overview

[What the feature does and why.]

## Requirements

### Functional Requirements

- FR1: [requirement]
- FR2: [requirement]

### Non-Functional Requirements

- NFR1: [requirement]
- NFR2: [requirement]

## Architecture

### System Context

[Where the feature sits in the system.]

### Data Flow

[How data moves through the feature.]

## Technical Approach

### [Component 1]

[Description.]

**Technologies:** [technologies]
**Dependencies:** [dependencies]

## API Specifications

### [Endpoint name]

**Endpoint:** `[path]`
**Method:** [GET / POST / PUT / DELETE]
**Authentication:** [method]

**Request:**

```json
[request example]
```

**Response:**

```json
[response example]
```

## Database Changes

- Table: [table] - [ADD / MODIFY / DROP] - [details]

## Security Considerations

[Threats and how the design handles them.]

## Performance Requirements

| Metric | Target | Measurement |
|--------|--------|-------------|
| [Metric] | [Target] | [How it is measured] |

## Testing Strategy

[Approach.]

### Test Cases

- TC1: [description] - expected: [result] - priority: [High / Medium / Low]
- TC2: [description] - expected: [result] - priority: [High / Medium / Low]

## Deployment Plan

[Steps.]

## Rollback Plan

[Steps.]

## References

- [Reference]
````

---

## Code Review Template

```markdown
# Code Review - [pull request title]

**Date:** [date]
**Reviewer:** [name]
**Author:** [name]
**Pull Request:** [number or link]

## Overview

[What the change does.]

## Files Changed

- `[path]` ([lines] lines): [what changed]
- `[path]` ([lines] lines): [what changed]

## Overall Assessment

**Recommendation:** [Approve / Request Changes / Reject]

**Summary:** [one paragraph]

## Detailed Review

### Strengths

- [Strength]

### Areas for Improvement

#### [Issue title]

**File:** [path]
**Line:** [line]
**Issue:** [description]
**Suggestion:** [suggestion]
**Priority:** [Must Fix / Should Fix / Nice to Have]

### Questions

- [Question]

## Security Concerns

- [Concern, or "None identified"]

## Performance Considerations

[Notes.]

## Testing Coverage

[What is tested and what is missing.]

## Approval Conditions

- [Condition]
```

---

## Working with the imported notes

- **Ask Your Files** answers questions across your notes and cites them; pick a collection to
  search only one kind of note.
- **Quick Actions > Summarize** and **Key Points** work on one indexed document at a time.
- The **Workflow** button on a document in the Knowledge Vault opens the Workflows page with
  the document's name, title and the start of its text as input, for example for the
  **Document Review** template. For a long document, paste its full text into **Input**
  instead.
- If you edit an imported file outside Agent-X, **Re-index** it in the Knowledge Vault (a file
  in a watch folder is picked up again while Agent-X runs).
