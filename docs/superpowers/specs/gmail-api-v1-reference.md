# Gmail API v1 Reference — GmailProvider Implementation

Source: Official Google Gmail API v1 REST Reference (fetched 2026-04-15)
Base URL: `https://gmail.googleapis.com/gmail/v1/users/{userId}/`

All endpoints use `{userId}` as a path parameter. Use `"me"` for the authenticated user.
All requests require `Authorization: Bearer {accessToken}` header.

---

## 1. Labels (Folders) — `users.labels.list`

### Endpoint
```
GET https://gmail.googleapis.com/gmail/v1/users/{userId}/labels
```

### Path Parameters
| Param | Type | Description |
|-------|------|-------------|
| `userId` | string | Email address or `"me"` for the authenticated user |

### Query Parameters
None.

### Response JSON Schema
```json
{
  "labels": [
    {
      "id": "INBOX",                    // string — immutable label ID
      "name": "INBOX",                  // string — display name
      "messageListVisibility": "show",  // enum: "show" | "hide"
      "labelListVisibility": "labelShow",// enum: "labelShow" | "labelShowIfUnread" | "labelHide"
      "type": "system",                 // enum: "system" | "user"
      "messagesTotal": 5832,            // integer — total messages with this label
      "messagesUnread": 1247,           // integer — unread messages
      "threadsTotal": 4201,             // integer — total threads
      "threadsUnread": 890,             // integer — unread threads
      "color": {                         // object (only on user labels)
        "textColor": "#ffffff",
        "backgroundColor": "#000000"
      }
    }
  ]
}
```

### Key Label IDs (System Labels)
```
INBOX, STARRED, IMPORTANT, SENT, DRAFTS, SPAM, TRASH,
UNREAD, CATEGORY_PERSONAL, CATEGORY_SOCIAL, CATEGORY_PROMOTIONS,
CATEGORY_UPDATES, CATEGORY_FORUMS
```

### OAuth Scopes (any one required)
| Scope | Level |
|-------|-------|
| `https://mail.google.com/` | Full access |
| `https://www.googleapis.com/auth/gmail.modify` | Read + modify (no delete) |
| `https://www.googleapis.com/auth/gmail.readonly` | **Read-only (recommended)** |
| `https://www.googleapis.com/auth/gmail.labels` | Labels only |
| `https://www.googleapis.com/auth/gmail.metadata` | Metadata only (no body) |

### Pagination
No pagination — returns all labels in a single response.

---

## 2. Messages List — `users.messages.list`

### Endpoint
```
GET https://gmail.googleapis.com/gmail/v1/users/{userId}/messages
```

### Path Parameters
| Param | Type | Description |
|-------|------|-------------|
| `userId` | string | Email address or `"me"` |

### Query Parameters
| Param | Type | Required | Default | Max | Description |
|-------|------|----------|---------|-----|-------------|
| `maxResults` | uint32 | No | 100 | 500 | Max messages to return per page |
| `pageToken` | string | No | — | — | Token from previous response for next page |
| `q` | string | No | — | — | Gmail search query (e.g., `"from:user@example.com is:unread"`). Cannot be used with `gmail.metadata` scope. |
| `labelIds[]` | string[] | No | — | — | Only return messages with ALL specified label IDs |
| `includeSpamTrash` | boolean | No | false | — | Include SPAM and TRASH messages |

### Response JSON Schema
```json
{
  "messages": [
    {
      "id": "18c2f1a3b4d5e6f7",       // string — immutable message ID
      "threadId": "18c2f1a3b4d5e6f7"  // string — thread this message belongs to
    }
  ],
  "nextPageToken": "08732937654908735",
  "resultSizeEstimate": 542
}
```

**IMPORTANT:** Each message in the list response contains ONLY `id` and `threadId`.
You MUST call `messages.get` for each ID to get headers, body, etc.

### Pagination
- If `nextPageToken` is present, pass it as `pageToken` in the next request.
- If `nextPageToken` is absent, there are no more results.
- Use `resultSizeEstimate` for progress UI (it is approximate).

### OAuth Scopes
Same as labels.list above (any one required).

---

## 3. Message Get — `users.messages.get`

### Endpoint
```
GET https://gmail.googleapis.com/gmail/v1/users/{userId}/messages/{id}
```

### Path Parameters
| Param | Type | Description |
|-------|------|-------------|
| `userId` | string | Email address or `"me"` |
| `id` | string | Message ID from `messages.list` |

### Query Parameters
| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `format` | enum | No | `full` | Format to return the message in |
| `metadataHeaders[]` | string[] | No | — | When `format=metadata`, only include these headers |

### Format Enum Values
| Value | Returns | Use Case |
|-------|---------|----------|
| `minimal` | ID + labels only | Check label changes without fetching content |
| `full` | Full message with parsed body in `payload` field | **Default. Best for email body extraction.** |
| `raw` | Full message as base64url in `raw` field | For MIME parsing, .eml export |
| `metadata` | ID + labels + headers only (no body) | **Best for inbox list view — fast, lightweight** |

**Recommended for GmailProvider:**
- List view: `format=metadata&metadataHeaders=From&metadataHeaders=To&metadataHeaders=Subject&metadataHeaders=Date`
- Full view: `format=full` (when user opens an email)

### Response JSON Schema (format=metadata)
```json
{
  "id": "18c2f1a3b4d5e6f7",
  "threadId": "18c2f1a3b4d5e6f7",
  "labelIds": ["INBOX", "UNREAD", "IMPORTANT"],
  "snippet": "Hi John, here's the Q3 report you asked for...",
  "historyId": "1234567890",
  "internalDate": "1709654321000",
  "payload": {
    "partId": "",
    "mimeType": "multipart/alternative",
    "filename": "",
    "headers": [
      { "name": "From", "value": "Jane Smith <jane@example.com>" },
      { "name": "To", "value": "john@example.com" },
      { "name": "Subject", "value": "Q3 Report Attached" },
      { "name": "Date", "value": "Mon, 4 Mar 2024 09:30:00 -0500" },
      { "name": "Message-Id", "value": "<abc123@example.com>" },
      { "name": "In-Reply-To", "value": "<xyz789@example.com>" },
      { "name": "References", "value": "<ref1@example.com> <ref2@example.com>" },
      { "name": "Content-Type", "value": "multipart/alternative; boundary=..." }
    ],
    "body": {
      "size": 0
    }
  },
  "sizeEstimate": 8192
}
```

### Response JSON Schema (format=full)
```json
{
  "id": "18c2f1a3b4d5e6f7",
  "threadId": "18c2f1a3b4d5e6f7",
  "labelIds": ["INBOX", "UNREAD"],
  "snippet": "Hi John, here's the Q3 report...",
  "historyId": "1234567890",
  "internalDate": "1709654321000",
  "payload": {
    "partId": "",
    "mimeType": "multipart/alternative",
    "filename": "",
    "headers": [
      { "name": "From", "value": "Jane Smith <jane@example.com>" },
      { "name": "To", "value": "john@example.com" },
      { "name": "Subject", "value": "Q3 Report Attached" },
      { "name": "Date", "value": "Mon, 4 Mar 2024 09:30:00 -0500" }
    ],
    "body": {
      "size": 0,
      "attachmentId": ""
    },
    "parts": [
      {
        "partId": "0",
        "mimeType": "text/plain",
        "filename": "",
        "headers": [
          { "name": "Content-Type", "value": "text/plain; charset=UTF-8" }
        ],
        "body": {
          "size": 512,
          "data": "SGkgSm9obiwgaGVyZSdzIHRoZQ..."   // base64url-encoded text body
        }
      },
      {
        "partId": "1",
        "mimeType": "text/html",
        "filename": "",
        "headers": [
          { "name": "Content-Type", "value": "text/html; charset=UTF-8" }
        ],
        "body": {
          "size": 2048,
          "data": "PGh0bWw+PGJvZHk+PGgxPl..."   // base64url-encoded HTML body
        }
      },
      {
        "partId": "2",
        "mimeType": "application/pdf",
        "filename": "Q3_Report.pdf",
        "headers": [
          { "name": "Content-Type", "value": "application/pdf" },
          { "name": "Content-Disposition", "value": "attachment; filename=\"Q3_Report.pdf\"" }
        ],
        "body": {
          "size": 1048576,
          "attachmentId": "ANGjd9-2d7..."   // Fetch via attachments.get
        }
      }
    ]
  },
  "sizeEstimate": 1050000
}
```

### Header Extraction Pattern
Headers are in `payload.headers[]` as `{ "name": "Header-Name", "value": "header-value" }`.
To extract common headers, filter by name (case-insensitive matching recommended):

```csharp
// Pseudocode for header extraction from payload.headers array
string? GetHeader(MessagePayload payload, string headerName)
{
    return payload.Headers?
        .FirstOrDefault(h => string.Equals(h.Name, headerName, StringComparison.OrdinalIgnoreCase))?
        .Value;
}

// Key headers to extract:
var from    = GetHeader(payload, "From");      // "Jane Smith <jane@example.com>"
var to      = GetHeader(payload, "To");         // "john@example.com"
var subject = GetHeader(payload, "Subject");   // "Q3 Report Attached"
var date    = GetHeader(payload, "Date");       // "Mon, 4 Mar 2024 09:30:00 -0500"
var cc      = GetHeader(payload, "Cc");         // "bob@example.com"
var bcc     = GetHeader(payload, "Bcc");         // usually empty (server strips)
var msgId   = GetHeader(payload, "Message-Id"); // "<abc123@example.com>"
var inReplyTo = GetHeader(payload, "In-Reply-To"); // thread correlation
```

### Body Extraction Pattern (format=full)
For `text/plain` and `text/html` bodies, decode the `body.data` field (base64url):

```csharp
// base64url → plain text (C#)
string DecodeBase64Url(string base64Url)
{
    var padded = base64Url.PadRight(base64Url.Length + (4 - base64Url.Length % 4) % 4, '=');
    var bytes = Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
    return Encoding.UTF8.GetString(bytes);
}
```

For multipart messages, walk `payload.parts[]` recursively:
1. Find part with `mimeType == "text/plain"` for plain text body.
2. Find part with `mimeType == "text/html"` for HTML body.
3. Parts with `body.attachmentId` are attachments — fetch separately via `attachments.get`.
4. Nested `multipart/*` parts have their own `parts[]` array (recurse).

### OAuth Scopes
| Scope | Format Restrictions |
|-------|-------------------|
| `https://mail.google.com/` | All formats |
| `https://www.googleapis.com/auth/gmail.modify` | All formats |
| `https://www.googleapis.com/auth/gmail.readonly` | All formats |
| `https://www.googleapis.com/auth/gmail.metadata` | Only `minimal` and `metadata` (no `full` or `raw`) |

**For GmailProvider: use `gmail.readonly` scope.** This allows `format=full` for body extraction.

---

## 4. History (Delta Sync) — `users.history.list`

### Endpoint
```
GET https://gmail.googleapis.com/gmail/v1/users/{userId}/history
```

### Path Parameters
| Param | Type | Description |
|-------|------|-------------|
| `userId` | string | Email address or `"me"` |

### Query Parameters
| Param | Type | Required | Default | Max | Description |
|-------|------|----------|---------|-----|-------------|
| `startHistoryId` | string | **Yes** | — | — | Returns history records AFTER this ID. Obtain from a previous `history.list` response or from any message's `historyId` field. |
| `maxResults` | uint32 | No | 100 | 500 | Max history records per page |
| `pageToken` | string | No | — | — | Token for next page |
| `labelId` | string | No | — | — | Only return changes for this label |
| `historyTypes[]` | enum[] | No | — | — | Filter to specific change types |

### HistoryType Enum Values
| Value | Description |
|-------|-------------|
| `messageAdded` | New messages added to mailbox |
| `messageDeleted` | Messages deleted from mailbox |
| `labelAdded` | Labels added to messages |
| `labelRemoved` | Labels removed from messages |

### Response JSON Schema
```json
{
  "history": [
    {
      "id": "1234567891",
      "messages": [
        { "id": "18c2f1a3b4d5e6f7", "threadId": "18c2f1a3b4d5e6f7" }
      ],
      "messagesAdded": [
        {
          "message": {
            "id": "18c2f1a3b4d5e6f7",
            "threadId": "18c2f1a3b4d5e6f7",
            "labelIds": ["INBOX"],
            "snippet": "New message snippet..."
          }
        }
      ],
      "messagesDeleted": [
        {
          "message": {
            "id": "18a1b2c3d4e5f6",
            "threadId": "18a1b2c3d4e5f6"
          }
        }
      ],
      "labelsAdded": [
        {
          "message": {
            "id": "18c2f1a3b4d5e6f7",
            "threadId": "18c2f1a3b4d5e6f7"
          },
          "labelIds": ["STARRED"]
        }
      ],
      "labelsRemoved": [
        {
          "message": {
            "id": "18c2f1a3b4d5e6f7",
            "threadId": "18c2f1a3b4d5e6f7"
          },
          "labelIds": ["UNREAD"]
        }
      ]
    }
  ],
  "nextPageToken": "08732937654908735",
  "historyId": "1234567900"
}
```

### Delta Sync Algorithm
1. **Initial full sync:** Call `messages.list` + `messages.get` for all messages. Store the `historyId` from the last message or the `history.list` response.
2. **Incremental sync:** Call `history.list?startHistoryId={storedHistoryId}`.
3. **Process changes:**
   - `messagesAdded[]` → Fetch new messages via `messages.get` and index.
   - `messagesDeleted[]` → Remove from local index by `id`.
   - `labelsAdded[]` / `labelsRemoved[]` → Update local label state.
4. **Save the new `historyId`** from the response for the next sync.
5. **Paginate** if `nextPageToken` is present.
6. **Error 404:** If `startHistoryId` is too old (expired), perform a full sync again. History IDs are typically valid for at least a week.

### OAuth Scopes
Same as messages.list (any one required):
- `https://mail.google.com/`
- `https://www.googleapis.com/auth/gmail.modify`
- `https://www.googleapis.com/auth/gmail.readonly`
- `https://www.googleapis.com/auth/gmail.metadata`

---

## 5. Recommended GmailProvider Implementation Strategy

### OAuth Scope for GmailProvider
```
https://www.googleapis.com/auth/gmail.readonly
```
This is the minimum scope needed. It allows:
- `labels.list` — list all folders/labels
- `messages.list` — list message IDs
- `messages.get` with `format=full` — get full message content including body
- `history.list` — delta sync

### Recommended API Call Sequence for Initial Sync
1. `GET /users/me/labels` → Get all labels, map to `FolderInfo` DTOs
2. `GET /users/me/messages?labelIds=INBOX&maxResults=500` → Get first page of message IDs
3. Paginate with `pageToken` until all IDs collected
4. For each message ID: `GET /users/me/messages/{id}?format=metadata&metadataHeaders=From&metadataHeaders=To&metadataHeaders=Subject&metadataHeaders=Date`
5. Save the `historyId` from the last response for delta sync

### Recommended API Call Sequence for Delta Sync
1. `GET /users/me/history?startHistoryId={lastHistoryId}&historyTypes=messageAdded&historyTypes=messageDeleted`
2. Process `messagesAdded` → `messages.get` for new messages
3. Process `messagesDeleted` → remove from local store
4. Save new `historyId` from response

### Rate Limiting Notes
- Gmail API quota: 250 quota units per second per user (per project)
- `messages.list` costs 5 units, `messages.get` costs 5 units, `history.list` costs 2 units, `labels.list` costs 1 unit
- For 500 messages: 5 (list) + 500 * 5 (gets) = 2505 units → ~10 seconds at 250 qps
- Batch requests can help (up to 100 per batch), reducing overhead

### C# Model Mapping (for GmailProvider JSON deserialization)

```csharp
// Internal deserialization models — match Google's JSON naming
private sealed class GmailLabelListResponse
{
    public List<GmailLabel>? Labels { get; set; }
}

private sealed class GmailLabel
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }                // "system" or "user"
    public string? MessageListVisibility { get; set; } // "show" or "hide"
    public int? MessagesTotal { get; set; }
    public int? MessagesUnread { get; set; }
}

private sealed class GmailMessageListResponse
{
    public List<GmailMessageRef>? Messages { get; set; }
    public string? NextPageToken { get; set; }
    public int? ResultSizeEstimate { get; set; }
}

private sealed class GmailMessageRef
{
    public string? Id { get; set; }
    public string? ThreadId { get; set; }
}

private sealed class GmailMessage
{
    public string? Id { get; set; }
    public string? ThreadId { get; set; }
    public List<string>? LabelIds { get; set; }
    public string? Snippet { get; set; }
    public string? HistoryId { get; set; }
    public string? InternalDate { get; set; }  // epoch milliseconds
    public GmailMessagePart? Payload { get; set; }
    public int? SizeEstimate { get; set; }
}

private sealed class GmailMessagePart
{
    public string? PartId { get; set; }
    public string? MimeType { get; set; }
    public string? Filename { get; set; }
    public List<GmailHeader>? Headers { get; set; }
    public GmailMessagePartBody? Body { get; set; }
    public List<GmailMessagePart>? Parts { get; set; }
}

private sealed class GmailHeader
{
    public string? Name { get; set; }
    public string? Value { get; set; }
}

private sealed class GmailMessagePartBody
{
    public int? Size { get; set; }
    public string? Data { get; set; }        // base64url-encoded (for inline)
    public string? AttachmentId { get; set; } // for attachments
}

private sealed class GmailHistoryListResponse
{
    public List<GmailHistoryRecord>? History { get; set; }
    public string? NextPageToken { get; set; }
    public string? HistoryId { get; set; }
}

private sealed class GmailHistoryRecord
{
    public string? Id { get; set; }
    public List<GmailMessageAdded>? MessagesAdded { get; set; }
    public List<GmailMessageDeleted>? MessagesDeleted { get; set; }
    public List<GmailLabelChange>? LabelsAdded { get; set; }
    public List<GmailLabelChange>? LabelsRemoved { get; set; }
}

private sealed class GmailMessageAdded
{
    public GmailMessage? Message { get; set; }
}

private sealed class GmailMessageDeleted
{
    public GmailMessageRef? Message { get; set; }
}

private sealed class GmailLabelChange
{
    public GmailMessageRef? Message { get; set; }
    public List<string>? LabelIds { get; set; }
}
```