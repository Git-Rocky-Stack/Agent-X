# Document Templates

**Pre-built document structures for Agent-X**

---

## Project Brief Template

```markdown
---
name: Project Brief
description: Structured project overview and planning document
category: Project Management
tags: [project, planning, brief]
---

# {{projectName}}

**Created:** {{date}}
**Author:** {{author}}
**Status:** {{status}} (Draft / In Progress / Completed)

## Executive Summary

{{executiveSummary}}

## Objectives

{{#each objectives}}
- {{this}}
{{/each}}

## Scope

### In Scope
{{#each inScope}}
- {{this}}
{{/each}}

### Out of Scope
{{#each outOfScope}}
- {{this}}
{{/each}}

## Timeline

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| {{phase1Name}} | {{phase1Duration}} | {{phase1Deliverables}} |
| {{phase2Name}} | {{phase2Duration}} | {{phase2Deliverables}} |
| {{phase3Name}} | {{phase3Duration}} | {{phase3Deliverables}} |

## Resources

**Team:** {{teamMembers}}
**Budget:** {{budget}}
**Tools:** {{tools}}

## Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| {{risk1}} | {{impact1}} | {{mitigation1}} |
| {{risk2}} | {{impact2}} | {{mitigation2}} |

## Success Criteria

{{#each successCriteria}}
- {{this}}
{{/each}}
```

---

## Meeting Notes Template

```markdown
---
name: Meeting Notes
description: Structured meeting record with action items
category: Communication
tags: [meeting, notes, action-items]
---

# {{meetingTitle}}

**Date:** {{date}}
**Time:** {{startTime}} - {{endTime}}
**Location:** {{location}}
**Attendees:** {{attendees}}

## Meeting Purpose

{{meetingPurpose}}

## Agenda Items

{{#each agendaItems}}
### {{title}}
{{#if discussion}}
**Discussion:** {{discussion}}
{{/if}}
{{#if decision}}
**Decision:** {{decision}}
{{/if}}
{{#if timebox}}
**Timebox:** {{timebox}}
{{/if}}
{{/each}}

## Discussion Summary

{{discussionSummary}}

## Decisions Made

{{#each decisions}}
- **{{topic}}:** {{decision}}
{{/each}}

## Action Items

| Task | Owner | Due Date | Status |
|------|-------|----------|--------|
| {{action1}} | {{owner1}} | {{dueDate1}} | {{status1}} |
| {{action2}} | {{owner2}} | {{dueDate2}} | {{status2}} |
| {{action3}} | {{owner3}} | {{dueDate3}} | {{status3}} |

## Next Meeting

**Date:** {{nextMeetingDate}}
**Time:** {{nextMeetingTime}}
**Agenda:** {{nextMeetingAgenda}}

## Attachments

{{#if attachments}}
- {{attachments}}
{{else}}
No attachments
{{/if}}
```

---

## Research Summary Template

```markdown
---
name: Research Summary
description: Structured research synthesis document
category: Research
tags: [research, summary, synthesis]
---

# {{researchTopic}}

**Research Date:** {{date}}
**Researcher:** {{author}}

## Research Question

{{researchQuestion}}

## Methodology

{{methodology}}

## Sources

{{#each sources}}
- {{title}} — {{author}} ({{year}})
  - URL: {{url}}
  - Access Date: {{accessDate}}
{{/each}}

## Key Findings

{{#each findings}}
### {{title}}

{{content}}

**Relevance:** {{relevance}}
**Confidence:** {{confidence}} (High / Medium / Low)
{{/each}}

## Analysis

{{analysis}}

## Limitations

{{#each limitations}}
- {{this}}
{{/each}}

## Conclusions

{{conclusions}}

## Recommendations

{{#each recommendations}}
1. {{this}}
{{/each}}

## Further Research

{{#each furtherResearch}}
- {{this}}
{{/each}}

## Related Documents

{{#if relatedDocuments}}
- {{relatedDocuments}}
{{else}}
No related documents
{{/if}}
```

---

## Technical Specification Template

```markdown
---
name: Technical Specification
description: Technical design specification document
category: Technical
tags: [specification, technical, design]
---

# {{featureName}} — Technical Specification

**Version:** {{version}}
**Status:** {{status}} (Draft / Review / Approved)
**Author:** {{author}}
**Reviewers:** {{reviewers}}

## Overview

{{overview}}

## Requirements

### Functional Requirements

{{#each functionalRequirements}}
- FR{{@index}}: {{this}}
{{/each}}

### Non-Functional Requirements

{{#each nonFunctionalRequirements}}
- NFR{{@index}}: {{this}}
{{/each}}

## Architecture

### System Context

{{systemContext}}

### Component Diagram

{{componentDiagram}}

### Data Flow

{{dataFlow}}

## Technical Approach

{{#each technicalApproach}}
### {{title}}

{{description}}

**Technologies:** {{technologies}}
**Dependencies:** {{dependencies}}
{{/each}}

## API Specifications

{{#if apiSpecs}}
### {{apiSpecs}}

**Endpoint:** `{{endpoint}}`
**Method:** {{method}}
**Authentication:** {{auth}}
**Request:**
```json
{{request}}
```
**Response:**
```json
{{response}}
```
{{/if}}

## Database Schema

{{#if databaseChanges}}
### Changes Required

{{#each databaseChanges}}
- Table: {{table}}
  - Action: {{action}} (ADD / MODIFY / DROP)
  - Details: {{details}}
{{/each}}
{{/if}}

## Security Considerations

{{securityConsiderations}}

## Performance Requirements

| Metric | Target | Measurement |
|--------|--------|-------------|
| {{metric1}} | {{target1}} | {{measurement1}} |
| {{metric2}} | {{target2}} | {{measurement2}} |

## Testing Strategy

{{testingStrategy}}

### Test Cases

{{#each testCases}}
- TC{{@index}}: {{description}}
  - Expected: {{expected}}
  - Priority: {{priority}}
{{/each}}

## Deployment Plan

{{deploymentPlan}}

## Rollback Plan

{{rollbackPlan}}

## References

{{#each references}}
- {{this}}
{{/each}}
```

---

## Code Review Template

```markdown
---
name: Code Review
description: Structured code review documentation
category: Development
tags: [code-review, development, quality]
---

# Code Review — {{pullRequestTitle}}

**Date:** {{date}}
**Reviewer:** {{author}}
**Author:** {{codeAuthor}}
**Pull Request:** {{prNumber}}

## Overview

{{overview}}

## Files Changed

{{#each changedFiles}}
- `{{path}}` ({{linesChanged}} lines)
  - Changes: {{description}}
{{/each}}

## Overall Assessment

**Recommendation:** {{recommendation}} (Approve / Request Changes / Reject)

**Summary:** {{summary}}

## Detailed Review

### Strengths

{{#each strengths}}
- {{this}}
{{/each}}

### Areas for Improvement

{{#each improvements}}
#### {{title}}

**File:** {{file}}
**Line:** {{line}}

**Issue:** {{issue}}
**Suggestion:** {{suggestion}}
**Priority:** {{priority}} (Must Fix / Should Fix / Nice to Have)
{{/each}}

### Questions

{{#each questions}}
- {{this}}
{{/each}}

## Security Concerns

{{#if securityConcerns}}
{{#each securityConcerns}}
- {{this}}
{{/each}}
{{else}}
No security concerns identified.
{{/if}}

## Performance Considerations

{{performanceConsiderations}}

## Testing Coverage

**Unit Tests:** {{unitTestCoverage}}%
**Integration Tests:** {{integrationTestCoverage}}%

**Missing Test Coverage:**
{{#each missingTests}}
- {{file}} — {{reason}}
{{/each}}

## Approval Conditions

{{#if approved}}
Approved with conditions:
{{#each conditions}}
- {{this}}
{{/each}}
{{else}}
Conditional approval. Address the "Must Fix" items above.
{{/if}}
```

---

## Using Document Templates

### Create from Template

1. Navigate to **Knowledge Vault**
2. Click **[+ New from Template]**
3. Select template from list
4. Fill prompted variables
5. Click **Create**

### Save Custom Template

1. Create document with desired structure
2. Navigate to **Settings → Templates**
3. Click **[Save as Template]**
4. Provide name and description
5. Template saved for future use

### Edit Template

1. Navigate to **Settings → Templates**
2. Select template to edit
3. Click **[Edit]**
4. Modify template content
5. Save changes

---

*Last updated: 2026-05-03*
