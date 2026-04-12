# {{Title}}

{{#if IncludeMetadata}}
**Created:** {{CreatedAt}} UTC
**Updated:** {{UpdatedAt}} UTC
**Messages:** {{MessageCount}}
**Tokens Used:** {{TokensUsed}}
{{#if ModelId}}
**Model:** {{ModelId}}
{{/if}}
{{/if}}

{{#if SystemPrompt}}
## System Prompt

> {{SystemPrompt}}

{{/if}}
## Conversation

{{#each Messages}}
{{#if not (eq Role "system")}}
### {{RoleLabel}}

{{#if ../options.IncludeTimestamps}}
*{{Timestamp}} UTC*
{{/if}}

{{#if ../options.IncludeModelInfo}}
{{#if ModelId}}
*Model: {{ModelId}}*
{{/if}}
{{/if}}

{{Content}}

{{#if ../options.IncludeMetadata}}
{{#if (eq Role "assistant")}}
{{#if TokenCount}}
*Tokens: {{TokenCount}}*
{{/if}}
{{#if GenerationTimeMs}}
*Generation: {{GenerationTimeMs}}ms*
{{/if}}
{{/if}}
{{/if}}

{{/if}}
{{/each}}

{{#if HasCitations}}
## Citations

{{#each Citations}}
{{IncIndex}}. {{.}}
{{/each}}

{{/if}}
---
*Exported from Agent-X on {{ExportDate}}*
