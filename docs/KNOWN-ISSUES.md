# Known Issues

Current limitations of Agent-X, each checked against the source code of version 2.2.0 and the
unreleased changes on top of it. Problems that have been fixed are recorded in the
[CHANGELOG](../CHANGELOG.md), not here. Issues numbered #2 to #9 in earlier versions of this
page, which a few code comments still cite, were fixed in June 2026; the CHANGELOG has the
details.

_Last reviewed: 2026-09-27._

---

## Installation and distribution

### Installers are not code-signed
The installers published with the v2.1.1 release are not Authenticode-signed, so Windows
SmartScreen shows an "unknown publisher" warning when you download and run them. Installers
for later versions have not been published yet; they wait for a code-signing certificate.
`scripts/build-installers.ps1` can already sign, timestamp and verify the app and both
installers (`-CertificateThumbprint` or `-CertificatePath`, and `-RequireSign` to refuse an
unsigned build). See [RELEASE-SIGNING.md](RELEASE-SIGNING.md).

### A second copy of Agent-X can start
Closing the main window hides Agent-X in the notification area instead of quitting it; Exit in
the tray icon's menu quits. Nothing stops a second copy from starting while the first one runs
in the tray: it opens on the same database, and its local API (port 9846) and the Win+Shift+A
Quick Chat hotkey do not start, because the first copy holds them. Reopen the running copy from
its tray icon instead.

---

## AI models

### GPU offload of the built-in model needs the NVIDIA CUDA 12 Toolkit
Agent-X ships the CUDA 12 build of llama.cpp (the `LLamaSharp.Backend.Cuda12` package) but not
the CUDA runtime libraries it loads (`cudart64_12.dll` and `cublas64_12.dll`). The CUDA backend
is used only when the NVIDIA CUDA 12 Toolkit is installed (`CUDA_PATH` points at it). Without the
toolkit the built-in model runs on the CPU, whatever **Automatic GPU layers** or **GPU Layers**
says under Settings > AI Providers > Built-in LLM (Local). AMD and Intel GPUs are not used by the
built-in model; Ollama decides its own GPU use.

### Model downloads are checked for size and format, not against pinned hashes
The built-in model (the Llama 3.2 3B or 1B GGUF file) and the speech-to-text model (Whisper base)
are downloaded from Hugging Face addresses that follow each publisher's `main` branch. A
download is accepted when it is complete (its size matches the one the server announced, when it
announces one) and plausible: a built-in model file must be at least 1.7 GB (3B) or 700 MB (1B), and a Whisper file
must start with the GGML header that whisper.cpp needs. No SHA-256 is pinned, so a file replaced
upstream would be accepted. `scripts/download-model.ps1`, which fetches the model for the
OFFLINE installer, prints the SHA-256 of what it downloaded but checks none.

### Speech-to-text uses one model, on the CPU
Audio import and voice input always use the Whisper base model, and the bundled Whisper runtime
runs on the CPU. There is no model choice, no setting to force the spoken language (it is
detected), and no speaker labels. OGG and WebM files decode only where Windows has a codec for
them. Nothing downloads the model on its own: install it with **Download** under
Speech-to-Text Model on the Model Manager page. Audio imported without it is marked Failed with
that reason and is queued again after the download.

### The built-in provider keeps its model choice for one session
With **Built-in LLM (Local)** active, Set Active on another `.gguf` file in the Model Manager
applies until Agent-X closes, because the configured file is also the embedding model. To change
the built-in model for good, set `localModelFileName` in `settings.json` while Agent-X is closed.

---

## Privacy

### Embeddings sent to OpenAI are not shown in the privacy line
When **Embedding Model** (Settings > AI Providers > Ollama (Local)) is an OpenAI embedding model
(a name that starts with `text-embedding-`), Agent-X sends the text of every chunk it indexes,
and every search query, to OpenAI. The Dashboard's privacy line and the LOCAL/NET lamp in the
status strip do not report this; they list only the active AI provider, a remote Ollama server,
model routing, the web search provider and the calendar and email connectors.

### Plugins are not sandboxed
A plugin runs inside Agent-X with your Windows account's rights. Loading it into its own
assembly context keeps its dependencies apart from the app's and lets it be unloaded, but it
does not limit what the plugin can read or where it can connect. The permissions a plugin's
manifest lists are informational: nothing asks you to approve them and nothing enforces them.
Install only plugins you trust.

### The Collaborative Sync key is stored in the database
The sync folder and the **Encryption Key** you enter on the Collaborative Sync page are saved in
the database (`user_settings` table) as plain JSON. They are protected at rest only when database
encryption is on, and every backup contains the database.

---

## Knowledge Vault and documents

### Scanned PDFs are not read
PDF text is read from the file's text layer. A scanned PDF without one, or a PDF whose fonts have
no Unicode mapping, is recorded as Failed with the reason; there is no OCR for PDFs (images are
read with Windows OCR). Legacy `.doc`, RTF, Excel, PowerPoint and EPUB files are not supported.

### Import Files does not list every format
The Import Files picker offers document, data and code formats only. Images, audio, `.url` and
`.webloc` shortcuts and formats added by plugins are imported by dragging them onto the Knowledge
Vault page or with Import Folder.

### Re-indexing needs the original file
The vault keeps a document's extracted text and the path to its file; the file itself is not
copied (pages saved by Web Import are the exception). Re-index reads the file again, so when the
file was moved or deleted the document is marked Failed with "Source file no longer exists".

### Annotations are not highlighted in the text
Annotations are made in the Knowledge Vault's Document Preview, one passage at a time, and are
saved in yellow (the color can be changed on the Annotations page). The passage text does not
show existing annotations; they are listed under ANNOTATIONS below it. Deleting an annotation,
in the preview or on the Annotations page, does not ask for confirmation.

### Script-built web pages may import with little text
When a fetched page is only a script shell, Web Import tries to render it in a headless Chromium
through Playwright. Agent-X does not install that browser, so where it is missing the page is
imported from the plain HTML, which can contain little or no article text.

---

## Encryption and moving data

### Database encryption is one-way and tied to this installation
Database encryption (Settings > Database Encryption) cannot be turned off once it is on. The key
is a random 256-bit key wrapped with Windows DPAPI for your account and kept in
`%LOCALAPPDATA%\AgentX\encryption.info.json`. The database inside a backup stays encrypted with
that key, so such a backup can be restored only by this installation under the same Windows
account. Keep `encryption.info.json` safe: without it the database cannot be opened.

### The vector index is rebuilt at every start when the database is encrypted
With an unencrypted database the HNSW vector index is saved next to it. With encryption on it is
kept in memory only (its files would hold the vectors unencrypted) and rebuilt from the database
each time Agent-X starts, which takes longer for large vaults.

### Secrets in settings.json belong to one Windows account
API keys, the web search key or SearXNG address, the local API token, OAuth client secrets and the
scheduled-backup password are encrypted in `settings.json` with DPAPI for the current Windows
user. Copied to another account or computer, they cannot be decrypted: Agent-X clears them, keeps
the original file as `settings.json.undecryptable-<time>`, and they must be entered again.

---

## Connectors and sync

### You supply the OAuth app for Google and Microsoft
Agent-X ships no OAuth client credentials. Connecting Gmail, Google Calendar or Outlook needs your
own client, entered under OAuth App Credentials on the Calendar or Email page: a Google OAuth
client of type Desktop app (with the Gmail and Google Calendar APIs enabled and your account added
as a test user), or a Microsoft Entra app registration with the "Mobile and desktop applications"
platform. The Microsoft tenant (`common` by default) and the redirect addresses can only be
changed in `settings.json`, with Agent-X closed. The connectors only read, and only Google and
Microsoft are supported (no IMAP, CalDAV or Exchange Web Services).

### The Calendar page's Conflict resolution choice has no effect
The value is saved, but the calendar sync never reads it: the connector only reads events and
never writes them back, so there is nothing to resolve.

### Collaborative Sync carries records, not files or messages
A sync package carries document records, Collections, tags, conversations, annotations and system
prompts. It does not carry document files, chunks or embeddings, chat messages or settings (the
page's "Sync not configured" hint says settings are synced; they are not). A document arrives
pending when its file exists at the same path on the receiving computer, where it is then indexed
again, and failed otherwise; a conversation arrives without its messages.

### The Android companion works over USB or the emulator only
The desktop's local API listens on `http://localhost:9846` and serves plain HTTP. The Android app
reaches it through the emulator (`http://10.0.2.2:9846`) or over USB after
`adb reverse tcp:9846 tcp:9846`; it refuses plain HTTP to any other host, and Wi-Fi or remote
connections are not supported. The app only reads (document and conversation lists, search), and
there is no iOS version. See [MOBILE-TRANSPORT.md](MOBILE-TRANSPORT.md).

---

## Temporal Identity

### Belief tracking understands English phrasing only
Beliefs are taken only from sentences in your chat messages that contain "I think", "I believe" or
"I feel" followed by "that", and their positivity is scored with fixed English word lists, so
views stated in other languages are not recorded. Insights in AI replies are detected with
English marker words (or an exclamation mark). Topic lookups on the Past Self page ignore case
for the letters A to Z only.

### Reading time is recorded but not shown
Agent-X records how long a conversation stays open in AI Chat and a document stays open in the
Knowledge Vault preview. No page displays these times yet.

---

## Workspace Profiles are records only
A profile stores a name, description, Ollama model name, Collection IDs and free-form settings.
Selecting a profile or marking it as the default changes nothing: no model, scope or setting is
applied, and nothing loads a profile at startup.

---

## Text that is not translated yet
The resource strings are translated in all six languages, but some text is still built in code in
English whatever the language: several AI Chat notifications (for example "Message deleted" and
"Only the latest response can be regenerated"), the export notifications on the AI Chat and
Collections pages, the shortcuts that pages add to the cheatsheet and the Command Palette, the page
names and labels in Jump To, the Theme choices and the connection-test and encryption status texts
in Settings, the description above the local API switch, the Dashboard's privacy line, and the
annotation color names.

---

## Smaller gaps
- Only the latest answer in a conversation can be regenerated; for an earlier one, use Branch from
  here. Conversations cannot be renamed.
- Model Manager deletes a model at once, without asking.
- Ask Your Files can be limited to one Collection, not to single documents.
- The Knowledge Graph zooms but cannot be panned.
- Workflows run only when you start them; there are no schedules or triggers.
- Web Import reads a feed once, when you import it, and takes up to 100 pages from a sitemap.
- The Weekly Digest covers a fixed seven-day window and is built only when you click Generate New
  Digest.

---

## Development and testing
- The WinUI pages are compiled in CI but not driven by automated UI tests. View models and
  services are unit-tested; `scripts/uia-nav-smoke.ps1` walks the navigation rail through UI
  Automation on a Windows desktop, and [a2-smoke-test-checklist.md](a2-smoke-test-checklist.md)
  is the manual keyboard checklist.
- CI runs the test suite with `--blame-hang-timeout 5m`, so a test host that does not exit after
  the tests finish is stopped with a dump instead of blocking the job.
