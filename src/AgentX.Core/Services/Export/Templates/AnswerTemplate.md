### {{RoleLabel}}

{{#if IncludeTimestamps}}
*{{Timestamp}} UTC*
{{/if}}

{{#if IncludeModelInfo}}
{{#if ModelId}}
*Model: {{ModelId}}*
{{/if}}
{{/if}}

{{Content}}

{{#if IncludeMetadata}}
{{#if (eq Role "assistant")}}
{{#if TokenCount}}
*Tokens: {{TokenCount}}*
{{/if}}
{{#if GenerationTimeMs}}
*Generation: {{GenerationTimeMs}}ms*
{{/if}}
{{/if}}
{{/if}}

{{#if HasCitations}}
**Citations:**
{{#each Citations}}
{{IncIndex}}. {{.}}
{{/each}}
{{/if}}
