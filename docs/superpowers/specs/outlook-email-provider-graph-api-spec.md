# OutlookEmailProvider - Microsoft Graph API v1.0 Specification

**Purpose:** Authoritative reference for implementing `OutlookEmailProvider : IEmailProvider`
**Base URL:** `https://graph.microsoft.com/v1.0`
**OAuth Provider:** `microsoft` (registered in `OAuthProviderRegistry`)
**Required Scope:** `Mail.Read` (delegated) — already included in `OAuthProviderRegistry.Microsoft` scopes

---

## 1. OAuth Scopes & Permissions

| Scope | Type | Use Case |
|-------|------|----------|
| `Mail.Read` | Delegated | Read user mail (required for all endpoints below) |
| `Mail.ReadWrite` | Delegated | Read + modify/delete/mark-as-read (not needed for read-only sync) |
| `User.Read` | Delegated | Get signed-in user profile (already in registry) |

**Decision:** Use `Mail.Read` (read-only). The provider never writes back to the mailbox.
`Mail.Read` is already in the registered scopes: `"openid profile email Calendars.Read Mail.Read User.Read"`.

**Authorization header format:**
```
Authorization: Bearer {access_token}
```

The access token is obtained via `IOAuthService.GetAccessTokenAsync("microsoft")`.

---

## 2. Endpoint: List Mail Folders

### Request

```
GET https://graph.microsoft.com/v1.0/me/mailFolders
```

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `$top` | int | No | Page size. Default: 10. Max: not documented (practical limit ~999) |
| `$skip` | int | No | Number of items to skip |
| `$select` | string | No | Comma-separated properties to return |
| `$filter` | string | No | OData filter expression |
| `includeHiddenFolders` | string | No | `true` to include hidden folders |

**Recommended request for our provider:**
```
GET https://graph.microsoft.com/v1.0/me/mailFolders?$top=100&includeHiddenFolders=true
```

### Response Schema (200 OK)

```json
{
  "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('{userId}')/mailFolders",
  "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/mailFolders?$skip=10&top=100",
  "value": [
    {
      "id": "AQMkADYAAAIBXQAAAA==",
      "displayName": "Inbox",
      "parentFolderId": "AQMkADYAAAIBCAAAAA==",
      "childFolderCount": 2,
      "unreadItemCount": 42,
      "totalItemCount": 1247,
      "isHidden": false,
      "wellKnownName": "inbox"
    }
  ]
}
```

**Key Fields for EmailFolderInfo mapping:**

| Graph Field | EmailFolderInfo Property | Notes |
|-------------|------------------------|-------|
| `id` | `Id` | Stable folder ID |
| `displayName` | `Name` | User-visible folder name |
| `totalItemCount` | `TotalCount` | Total messages in folder |
| `unreadItemCount` | `UnreadCount` | Unread messages count |
| — | `SourceProvider` | Hardcoded `"microsoft"` |

### Pagination

When more results exist, the response includes `@odata.nextLink`. Follow it to get the next page.
Stop when `@odata.nextLink` is absent. Each `@odata.nextLink` URL contains a `$skip` token.

```csharp
// Pseudocode for paginated folder listing
var allFolders = new List<EmailFolderInfo>();
var url = "https://graph.microsoft.com/v1.0/me/mailFolders?$top=100&includeHiddenFolders=true";
while (url != null)
{
    var response = await httpClient.GetAsync(url, ct);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    foreach (var folder in json.GetProperty("value").EnumerateArray())
    {
        allFolders.Add(MapToEmailFolderInfo(folder));
    }
    url = json.TryGetProperty("@odata.nextLink", out var nextLink)
        ? nextLink.GetString()
        : null;
}
```

---

## 3. Endpoint: List Messages in Folder

### Request

```
GET https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `folderId` | string | Yes | The folder ID from `ListFoldersAsync` (e.g., `"inbox"`, `"sentitems"`, or a base64 ID) |

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `$top` | int | No | Page size. Default: 10. Max: 1000 (use 50-100 for sync) |
| `$skip` | int | No | Items to skip (not recommended with `$orderby`, use `$skipToken` via nextLink) |
| `$select` | string | No | Comma-separated properties. **Always use this** to reduce payload size |
| `$filter` | string | No | OData filter. Supports `isRead`, `receivedDateTime`, `hasAttachments`, `flag/flagStatus` |
| `$orderby` | string | No | Sort. Only `receivedDateTime desc` is recommended for email |

**Recommended request for initial sync:**
```
GET https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages?$top=50&$orderby=receivedDateTime desc&$select=id,subject,bodyPreview,body,from,toRecipients,ccRecipients,receivedDateTime,isRead,flag,parentFolderId,hasAttachments,internetMessageId,conversationId,webLink
```

**Recommended request for filtered sync (unread only):**
```
GET https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages?$filter=isRead eq false&$top=50&$orderby=receivedDateTime desc&$select=id,subject,bodyPreview,body,from,toRecipients,ccRecipients,receivedDateTime,isRead,flag,parentFolderId,hasAttachments
```

### Response Schema (200 OK)

```json
{
  "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('{userId}')/mailFolders('{folderId}')/messages(id,subject,...)",
  "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages?$top=50&$skipToken={skipToken}",
  "value": [
    {
      "id": "AAMkAGI2TG93AAA=",
      "createdDateTime": "2024-01-15T10:30:00Z",
      "lastModifiedDateTime": "2024-01-15T10:30:00Z",
      "changeKey": "CQAAABYAAAAiIsqMbYjsT5e/T7KzowPTAACB/CZh",
      "categories": [],
      "receivedDateTime": "2024-01-15T10:30:00Z",
      "sentDateTime": "2024-01-15T10:28:00Z",
      "hasAttachments": false,
      "internetMessageId": "<msg-id@contoso.com>",
      "subject": "Project Update - Q4 Results",
      "bodyPreview": "Hi team, Here are the Q4 results we discussed in the meeting yesterday...",
      "importance": "normal",
      "parentFolderId": "AAMkAGVmMDEzM",
      "conversationId": "AAQkAGVmMDEzE",
      "isRead": false,
      "isDraft": false,
      "webLink": "https://outlook.office365.com/owa/?ItemID=AAMkAGI2TG93AAA=&exvsurl=1&viewmodel=ReadMessageItem",
      "body": {
        "contentType": "html",
        "content": "<html><body>Hi team, Here are the Q4 results...</body></html>"
      },
      "sender": {
        "emailAddress": {
          "name": "John Smith",
          "address": "john@contoso.com"
        }
      },
      "from": {
        "emailAddress": {
          "name": "John Smith",
          "address": "john@contoso.com"
        }
      },
      "toRecipients": [
        {
          "emailAddress": {
            "name": "Megan Bowen",
            "address": "megan@contoso.com"
          }
        }
      ],
      "ccRecipients": [],
      "bccRecipients": [],
      "replyTo": [],
      "flag": {
        "flagStatus": "notFlagged"
      }
    }
  ]
}
```

### Key Fields for EmailMessage Mapping

| Graph Field | EmailMessage Property | Transformation |
|-------------|----------------------|----------------|
| `id` | `Id` | Direct string copy |
| `subject` | `Subject` | Direct string copy |
| `bodyPreview` | `BodyPreview` | First ~255 chars, plain text |
| `body.content` (where `contentType` = `"html"`) | `BodyHtml` | HTML body content |
| `body.content` (where `contentType` = `"text"`) | `BodyText` | Text body content |
| `from.emailAddress` | `From` | Map to `EmailContact` |
| `toRecipients[].emailAddress` | `To` | Map each to `EmailContact` |
| `ccRecipients[].emailAddress` | `Cc` | Map each to `EmailContact` |
| `bccRecipients[].emailAddress` | `Bcc` | Map each to `EmailContact` |
| `receivedDateTime` | `ReceivedAt` | Parse ISO 8601 to `DateTime` |
| `isRead` | `IsRead` | Direct bool copy |
| `flag.flagStatus` | `IsStarred` | `true` when `flagStatus == "flagged"` |
| `hasAttachments` | `HasAttachments` | Direct bool copy |
| — | `FolderName` | From folder metadata (not in message response) |
| `parentFolderId` | `FolderId` | Direct string copy |
| `conversationId` | `ThreadId` | Direct string copy |
| — | `SourceProvider` | Hardcoded `"microsoft"` |
| — | `AttachmentNames` | Requires separate `$expand=attachments` call (deferred) |
| `webLink` | `WebLink` | Direct string copy |

### EmailContact Mapping from Graph emailAddress Object

```csharp
// Graph format:
// { "emailAddress": { "name": "John Smith", "address": "john@contoso.com" } }

static EmailContact MapContact(JsonElement emailAddressObj)
{
    var email = emailAddressObj.GetProperty("emailAddress");
    return new EmailContact
    {
        DisplayName = email.GetProperty("name").GetString() ?? "",
        EmailAddress = email.GetProperty("address").GetString() ?? "",
        IsMe = false // Set to true by comparing with authenticated user
    };
}
```

### Pagination

Same pattern as folders: follow `@odata.nextLink` until absent.
The `$skipToken` is embedded in the nextLink URL — do NOT construct it manually.

### Body Content: HTML vs Text

**Default behavior (no Prefer header):** `body.contentType` returns `"html"` and `body.content` contains HTML.

**To get plain text body instead:**
```
Prefer: outlook.body-content-type="text"
```
This changes `body.contentType` to `"text"` and `body.content` to plain text.

**Recommended approach for OutlookEmailProvider:**
1. **First call** (list messages): Do NOT send the `Prefer` header. Get `bodyPreview` (always plain text, first ~255 chars) and `body` as HTML.
2. **Map:** `bodyPreview` → `BodyPreview`, `body.content` → `BodyHtml`, derive `BodyText` from `bodyPreview` (or do a separate single-message GET with the `Prefer: outlook.body-content-type="text"` header if full text body is needed).
3. This avoids the overhead of requesting both formats in bulk queries.

**Alternative: request text body upfront:**
```
GET .../messages?$select=...,body,bodyPreview&$top=50
Prefer: outlook.body-content-type="text"
```
Then `body.content` is plain text → map to `BodyText`, and `BodyHtml` is empty (requires separate call if needed).

---

## 4. Endpoint: Get Single Message

### Request

```
GET https://graph.microsoft.com/v1.0/me/messages/{messageId}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `messageId` | string | Yes | The message ID from a list/delta response |

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `$select` | string | No | Comma-separated properties. Recommended to always specify |

**Request Headers:**

| Header | Value | Description |
|--------|-------|-------------|
| `Prefer` | `outlook.body-content-type="text"` | Get body as plain text (omit for HTML) |

**Recommended request (full message detail, text body):**
```
GET https://graph.microsoft.com/v1.0/me/messages/{messageId}?$select=id,subject,body,bodyPreview,uniqueBody,from,toRecipients,ccRecipients,bccRecipients,receivedDateTime,sentDateTime,isRead,flag,parentFolderId,hasAttachments,internetMessageId,conversationId,webLink,importance,categories
Prefer: outlook.body-content-type="text"
```

### Response Schema (200 OK)

```json
{
  "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('{userId}')/messages(id,subject,body,bodyPreview,...)/$entity",
  "@odata.etag": "W/\"CQAAABYAAABmWdbhEgBXTophjCWt81m9AAAoZYj4\"",
  "id": "AAMkAGI1AAAoZCfHAAA=",
  "subject": "Welcome to our group!",
  "bodyPreview": "Welcome to our group, Dana! Hope you will enjoy working with us...",
  "body": {
    "contentType": "text",
    "content": "Welcome to our group, Dana! Hope you will enjoy working with us!\r\n\r\nWould you like to choose a day for our orientation..."
  },
  "uniqueBody": {
    "contentType": "text",
    "content": "Welcome to our group, Dana! Hope you will enjoy working with us!\r\nWould you like to choose a day..."
  },
  "from": {
    "emailAddress": {
      "name": "Dana Swope",
      "address": "danas@contoso.com"
    }
  },
  "toRecipients": [
    {
      "emailAddress": {
        "name": "Megan Bowen",
        "address": "meganb@contoso.com"
      }
    }
  ],
  "ccRecipients": [],
  "bccRecipients": [],
  "receivedDateTime": "2024-01-15T10:30:00Z",
  "sentDateTime": "2024-01-15T10:28:00Z",
  "isRead": true,
  "flag": {
    "flagStatus": "notFlagged"
  },
  "parentFolderId": "AAMkAGVmMDEzM",
  "hasAttachments": false,
  "conversationId": "AAQkAGVmMDEzE",
  "webLink": "https://outlook.office365.com/owa/?ItemID=AAMkAGI1AAAoZCfHAAA=&exvsurl=1&viewmodel=ReadMessageItem"
}
```

**Additional fields available on single-message GET (not in list):**

| Field | Type | Description |
|-------|------|-------------|
| `uniqueBody` | object | Body without repeated thread content (same shape as `body`) |
| `sentDateTime` | string | When the message was sent |
| `importance` | string | `"low"`, `"normal"`, `"high"` |
| `categories` | string[] | User-assigned categories |
| `inferenceClassification` | string | `"focused"` or `"other"` (Focused Inbox) |
| `isDraft` | bool | Whether the message is a draft |
| `internetMessageId` | string | RFC 2822 Message-ID header |

**`body` vs `bodyPreview` vs `uniqueBody`:**

| Property | Max Length | Content | Use Case |
|----------|-----------|---------|----------|
| `bodyPreview` | ~255 chars | First N chars, plain text | List views, triage preview |
| `body` | Full | Complete message body (HTML or text based on `Prefer` header) | Full message display, indexing |
| `uniqueBody` | Full | Body without quoted reply chain | Clean content for search indexing |

---

## 5. Endpoint: Delta Query for Incremental Sync

### Overview

Delta queries are the **primary sync mechanism** for `OutlookEmailProvider`. They return only messages that have been added, modified, or deleted since the last sync.

### Request (Initial Sync)

```
GET https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages/delta
```

### Request (Subsequent Sync)

```
GET https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages/delta?$deltatoken={deltaToken}
```

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `folderId` | string | Yes | The folder ID to track changes in |

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `$deltatoken` | string | Conditional | From previous `@odata.deltaLink`. Required for subsequent syncs |
| `$skiptoken` | string | No | From `@odata.nextLink` during pagination within a delta round |
| `$select` | string | No | Properties to return (always include `id`) |
| `$top` | int | No | Page size per response |
| `$filter` | string | No | Only `receivedDateTime ge {value}` or `receivedDateTime gt {value}` |
| `$orderby` | string | No | Only `receivedDateTime desc` supported |

**Request Headers:**

| Header | Value | Description |
|--------|-------|-------------|
| `Prefer` | `odata.maxpagesize=50` | Controls page size in delta responses |

**Recommended initial delta request:**
```
GET https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages/delta?$select=id,subject,bodyPreview,body,from,toRecipients,ccRecipients,receivedDateTime,isRead,flag,parentFolderId,hasAttachments,conversationId,webLink
Prefer: odata.maxpagesize=50
```

**Recommended subsequent delta request:**
```
GET https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages/delta?$deltatoken={deltaToken}&$select=id,subject,bodyPreview,body,from,toRecipients,ccRecipients,receivedDateTime,isRead,flag,parentFolderId,hasAttachments,conversationId,webLink
Prefer: odata.maxpagesize=50
```

### Response Schema (200 OK) — Paginated Delta

When more pages exist in the current delta round:

```json
{
  "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#Collection(message)",
  "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages/delta?$skiptoken={skipToken}",
  "value": [
    {
      "@odata.type": "#microsoft.graph.message",
      "@odata.etag": "W/\"CQAAABYAAACQ2fKdhq8oSKEDSVrdi3lRAAId0MCP\"",
      "id": "AAMkAGUwNjQ4ZjIx...",
      "subject": "Project Update",
      "bodyPreview": "Here is the latest update...",
      "body": {
        "contentType": "html",
        "content": "<html><body>Here is the latest update...</body></html>"
      },
      "from": {
        "emailAddress": {
          "name": "Patti Fernandez",
          "address": "PattiF@contoso.com"
        }
      },
      "toRecipients": [...],
      "receivedDateTime": "2024-01-15T10:30:00Z",
      "isRead": false,
      "flag": { "flagStatus": "notFlagged" },
      "parentFolderId": "AAMkAGVmMDEzM",
      "hasAttachments": false,
      "conversationId": "AAQkAGVmMDEzE",
      "webLink": "https://outlook.office365.com/..."
    }
  ]
}
```

### Response Schema (200 OK) — Delta Round Complete

When the current round of changes is fully returned:

```json
{
  "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#Collection(message)",
  "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages/delta?$deltatoken=GwcBoTmPuoGNlgXgF1nyUNMXY",
  "value": [
    {
      "@odata.type": "#microsoft.graph.message",
      "@odata.etag": "W/\"CQAAABYAAAARn2vdzPFjSbaPPxzjlzOTAAASsKZz\"",
      "id": "AAMkADNkNAAASq35xAAA=",
      "subject": "Holiday hours update",
      "isRead": true,
      "sender": {
        "emailAddress": {
          "name": "Dana Swope",
          "address": "danas@contoso.com"
        }
      }
    }
  ]
}
```

### Deleted Messages in Delta

When a message has been deleted since the last sync, the delta response includes a `@removed` annotation:

```json
{
  "@odata.type": "#microsoft.graph.message",
  "id": "AAMkADk0MGFkODE3LWE4MmYtNDRhOS0Dh_6qB-pB2Sa2pUum19a6YAAKnLuxoAAA=",
  "@removed": {
    "reason": "deleted"
  }
}
```

The `@removed.reason` field is always `"deleted"` for message deletions.

### Delta Query Flow

```
1. Initial Sync (no deltaToken):
   GET .../messages/delta?$select=...
   → Returns @odata.nextLink (more pages) OR @odata.deltaLink (sync round complete)
   
2. Pagination within sync round:
   Follow @odata.nextLink until it disappears
   Each nextLink contains $skiptoken (do NOT construct manually)
   
3. Sync round complete:
   Response contains @odata.deltaLink with $deltatoken
   PERSIST this deltaToken (per-folder) for the next sync
   
4. Subsequent Sync (with deltaToken):
   GET .../messages/delta?$deltatoken={savedToken}&$select=...
   → Returns only changes since last sync
   → Again paginate via @odata.nextLink
   → Save new @odata.deltaLink when round completes
   
5. Handle deletions:
   Messages with @removed annotation should be removed from local store
   Check @removed.reason == "deleted"
```

### Delta Token Storage

Store delta tokens per-folder in `EmailSyncSettings` or a dedicated store:

```csharp
// Conceptual storage model
Dictionary<string, string> _deltaTokens; // key = folderId, value = deltaToken

// After each completed sync round:
_deltaTokens[folderId] = ExtractDeltaTokenFromResponse(deltaLinkUrl);

// On next sync:
var deltaToken = _deltaTokens.GetValueOrDefault(folderId);
var messages = await GetDeltaChangesAsync(folderId, deltaToken, ct);
```

**Delta token expiry:** Delta tokens expire if not used within a certain period (Microsoft does not document an exact expiry, but tokens older than ~7 days may return an error). If a delta token fails with an error, fall back to a full sync (initial delta query without `$deltatoken`).

---

## 6. Implementation Blueprint

### OutlookEmailProvider Class Structure

```csharp
namespace AgentX.Core.Services.Plugins.Email;

using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins.Email.Models;
using System.Net.Http.Headers;
using System.Text.Json;

public sealed class OutlookEmailProvider : IEmailProvider
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const string BodyContentPreference = "outlook.body-content-type";

    private readonly IOAuthService _oauthService;
    private readonly HttpClient _httpClient;
    private readonly ILogger _log;

    public string ProviderId => "microsoft";

    public OutlookEmailProvider(IOAuthService oauthService, HttpClient httpClient, ILogger log)
    {
        _oauthService = oauthService;
        _httpClient = httpClient;
        _log = log;
    }

    public async Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(CancellationToken ct = default)
    {
        // GET /me/mailFolders?$top=100&includeHiddenFolders=true
        // Paginate via @odata.nextLink
        // Map each folder → EmailFolderInfo
    }

    public async Task<(IReadOnlyList<EmailMessage> Messages, string? DeltaToken)> GetMessagesAsync(
        string folderId,
        int maxResults = 50,
        string? deltaToken = null,
        CancellationToken ct = default)
    {
        // If deltaToken provided: GET .../messages/delta?$deltatoken={token}
        // Else: GET .../messages/delta (initial sync)
        // Use Prefer: odata.maxpagesize={maxResults}
        // Paginate via @odata.nextLink, collect all @removed items
        // Return messages + final @odata.deltaLink token
    }
}
```

### Key Implementation Details

1. **Authentication:** Every request calls `_oauthService.GetAccessTokenAsync("microsoft")` first. The service handles token refresh automatically. Set `Authorization: Bearer {token}` header.

2. **Pagination pattern:** All list/delta endpoints use the same `@odata.nextLink` pattern. Never construct `$skip`/`$skipToken` manually — always follow the URL from the response.

3. **Delta vs List:** Always use the delta endpoint (`/messages/delta`) for `GetMessagesAsync`, not the regular list endpoint. This ensures both initial and incremental sync work through the same code path.

4. **Body content strategy:**
   - During sync (delta/list): Get HTML body by default + `bodyPreview` for plain text preview
   - For full detail: Optionally use single-message GET with `Prefer: outlook.body-content-type="text"` to get `BodyText`

5. **Deleted message handling:** Check for `@removed` annotation on each item in delta responses. If present, the message was deleted — remove from local store.

6. **Error recovery:** If a delta token is expired or invalid, the API returns an error. Catch this and fall back to a full sync (delta query without token).

7. **Rate limiting:** Microsoft Graph API has per-app and per-tenant throttling. Respect `Retry-After` headers. Use exponential backoff on 429 responses.

### Graph API JSON Deserialization Models

```csharp
// Internal models for deserializing Graph API responses
// These are NOT the public EmailMessage/EmailFolderInfo DTOs

internal sealed class GraphMailFolderResponse
{
    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }

    [JsonPropertyName("value")]
    public List<GraphMailFolder> Value { get; init; } = [];
}

internal sealed class GraphMailFolder
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("parentFolderId")]
    public string ParentFolderId { get; init; } = string.Empty;

    [JsonPropertyName("childFolderCount")]
    public int ChildFolderCount { get; init; }

    [JsonPropertyName("unreadItemCount")]
    public int UnreadItemCount { get; init; }

    [JsonPropertyName("totalItemCount")]
    public int TotalItemCount { get; init; }

    [JsonPropertyName("isHidden")]
    public bool IsHidden { get; init; }
}

internal sealed class GraphMessageDeltaResponse
{
    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }

    [JsonPropertyName("@odata.deltaLink")]
    public string? DeltaLink { get; init; }

    [JsonPropertyName("value")]
    public List<JsonElement> Value { get; init; } = [];
    // Use JsonElement because items may be messages OR @removed annotations
}

internal sealed class GraphMessage
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("bodyPreview")]
    public string BodyPreview { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public GraphItemBody? Body { get; init; }

    [JsonPropertyName("from")]
    public GraphRecipient? From { get; init; }

    [JsonPropertyName("toRecipients")]
    public List<GraphRecipient> ToRecipients { get; init; } = [];

    [JsonPropertyName("ccRecipients")]
    public List<GraphRecipient> CcRecipients { get; init; } = [];

    [JsonPropertyName("bccRecipients")]
    public List<GraphRecipient> BccRecipients { get; init; } = [];

    [JsonPropertyName("receivedDateTime")]
    public DateTime ReceivedDateTime { get; init; }

    [JsonPropertyName("isRead")]
    public bool IsRead { get; init; }

    [JsonPropertyName("flag")]
    public GraphFlag? Flag { get; init; }

    [JsonPropertyName("parentFolderId")]
    public string ParentFolderId { get; init; } = string.Empty;

    [JsonPropertyName("hasAttachments")]
    public bool HasAttachments { get; init; }

    [JsonPropertyName("conversationId")]
    public string ConversationId { get; init; } = string.Empty;

    [JsonPropertyName("webLink")]
    public string? WebLink { get; init; }

    [JsonPropertyName("@removed")]
    public GraphRemoved? Removed { get; init; }
}

internal sealed class GraphItemBody
{
    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = string.Empty; // "html" or "text"

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

internal sealed class GraphRecipient
{
    [JsonPropertyName("emailAddress")]
    public GraphEmailAddress? EmailAddress { get; init; }
}

internal sealed class GraphEmailAddress
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;
}

internal sealed class GraphFlag
{
    [JsonPropertyName("flagStatus")]
    public string FlagStatus { get; init; } = "notFlagged";
    // Values: "notFlagged", "flagged", "complete"
}

internal sealed class GraphRemoved
{
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty; // always "deleted"
}
```

---

## 7. Well-Known Folder IDs

Microsoft Graph provides well-known IDs for default folders that can be used instead of the base64-encoded IDs:

| Well-Known Name | Folder ID | Display Name |
|-----------------|-----------|--------------|
| `inbox` | `inbox` | Inbox |
| `sentitems` | `sentitems` | Sent Items |
| `drafts` | `drafts` | Drafts |
| `deleteditems` | `deleteditems` | Deleted Items |
| `junkemail` | `junkemail` | Junk Email |
| `outbox` | `outbox` | Outbox |
| `archive` | `archive` | Archive |

These can be used directly in URLs:
```
GET https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages
GET https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta
```

Custom user-created folders will have base64-encoded IDs like `AQMkADYAAAIBXQAAAA==`.

---

## 8. Rate Limiting & Throttling

Microsoft Graph API throttles requests based on:

| Limit Type | Value | Handling |
|-----------|-------|----------|
| Per-app throttling | ~10,000 req/min (varies by endpoint) | Honor `Retry-After` header on 429 |
| Per-tenant throttling | Lower than per-app | Honor `Retry-After` header on 429 |
| Concurrent requests | Varies | Use `SemaphoreSlim` to limit concurrent requests |

**429 Too Many Requests response:**
```json
{
  "error": {
    "code": "ErrorTooManyRequests",
    "message": "Too many requests. Please retry after some time.",
    "innerError": {
      "request-id": "...",
      "date": "2024-01-15T10:30:00Z",
      "status": 429
    }
  }
}
```

**Headers on 429 response:**
- `Retry-After`: Seconds to wait before retrying
- `X-RateLimit-Limit`: Request limit
- `X-RateLimit-Remaining`: Remaining requests
- `X-RateLimit-Reset`: Seconds until limit resets

**Implementation:**
```csharp
async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken ct)
{
    const int maxRetries = 3;
    for (int attempt = 0; attempt <= maxRetries; attempt++)
    {
        var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return response;

        if (attempt == maxRetries)
            return response; // give up after max retries

        var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
        await Task.Delay(retryAfter, ct);
    }
    // Unreachable, but compiler needs it
    return await _httpClient.SendAsync(request, ct);
}
```

---

## 9. Error Response Format

All Microsoft Graph API errors follow this schema:

```json
{
  "error": {
    "code": "ErrorItemNotFound",
    "message": "The specified object was not found in the store.",
    "innerError": {
      "date": "2024-01-15T10:30:00Z",
      "request-id": "b62c10ce-...",
      "client-request-id": "..."
    }
  }
}
```

**Common error codes for email endpoints:**

| HTTP Status | Error Code | Meaning | Recovery |
|-------------|-----------|---------|----------|
| 401 | `InvalidAuthenticationToken` | Token expired or invalid | Refresh token via `IOAuthService` |
| 403 | `ErrorAccessDenied` | Insufficient scope | Re-authorize with `Mail.Read` scope |
| 404 | `ErrorItemNotFound` | Message/folder not found | Remove from local store |
| 429 | `ErrorTooManyRequests` | Throttled | Exponential backoff + `Retry-After` |
| 500 | `ErrorInternalServerError` | Transient server error | Retry with backoff |
| 503 | `ErrorServiceUnavailable` | Service temporarily down | Retry after delay |

---

## 10. Summary: Endpoint Decision Matrix

| IEmailProvider Method | Graph API Endpoint | HTTP Method | Key Params |
|----------------------|-------------------|-------------|------------|
| `ListFoldersAsync()` | `/me/mailFolders` | GET | `$top`, `includeHiddenFolders` |
| `GetMessagesAsync(folderId, maxResults, deltaToken)` | `/me/mailFolders/{folderId}/messages/delta` | GET | `$deltatoken`, `$select`, `Prefer: odata.maxpagesize` |
| (internal) Get full message detail | `/me/messages/{id}` | GET | `$select`, `Prefer: outlook.body-content-type="text"` |

**The list endpoint** (`/me/mailFolders/{folderId}/messages`) is NOT used directly. All sync goes through the delta endpoint, which works for both initial and incremental sync.

---

*Spec version: 1.0 | Created: 2026-04-15 | Sources: Microsoft Graph API v1.0 official docs via Context7*