# Agent-X Troubleshooting Guide

**Solutions to common issues**

Two places help with almost every problem below: the **log files** in `%LocalAppData%\AgentX\Logs` (see [Log files](#log-files)), and the **Operations** page (`Ctrl+Shift+O`), which lists recently imported documents with their indexing errors and a **Retry Index** button, the Smart Inbox backlog, and the state of sync, workflows and connectors.

---

## Table of Contents

1. [Installation Issues](#installation-issues)
2. [Startup & Launch Issues](#startup--launch-issues)
3. [AI & Model Issues](#ai--model-issues)
4. [Import & Indexing Issues](#import--indexing-issues)
5. [Search Issues](#search-issues)
6. [Performance Issues](#performance-issues)
7. [Database Issues](#database-issues)
8. [GPU Acceleration Issues](#gpu-acceleration-issues)
9. [Sync & Integration Issues](#sync--integration-issues)
10. [Advanced Diagnostics](#advanced-diagnostics)

---

## Installation Issues

### Installer Won't Launch

**Symptom:** Double-clicking the installer does nothing, or Windows blocks it

**Solutions:**

1. **Allow it past SmartScreen**
   - The installers are not code-signed, so SmartScreen may show "Windows protected your PC"
   - Choose **More info**, check that the file came from the Agent-X release page, then **Run anyway**

2. **Verify Windows version**
   - Windows 10 build 19041 (version 2004) or later is required, on x64
   - Check: `Win + R`, then `winver`

3. **Check antivirus blocking**
   - Look in your antivirus quarantine for the installer
   - Allow it and run it again

4. **Verify the download**
   - Re-download the installer
   - The v2.1.1 files are 228 MiB (SLIM, `AgentX-Setup-2.1.1-x64.exe`) and 2.07 GiB (OFFLINE, `AgentX-Setup-2.1.1-x64-offline.exe`)
   - Compare `Get-FileHash <file> -Algorithm SHA256` with the checksum published with the release, when there is one

### Installation Fails Mid-Process

**Symptom:** Installer stops or reports an error during installation

**Solutions:**

1. **Free disk space**
   - The OFFLINE installer writes the 2 GB built-in model to `%LocalAppData%\AgentX\Models`
   - Clear temporary files or choose another installation drive for the program files

2. **Let the installer close Agent-X**
   - A running Agent-X (also one hidden in the notification area) is closed by the installer
   - If a file stays locked, use **Exit** in Agent-X's tray menu and run the installer again

3. **Check antivirus**
   - Some antivirus tools lock new executables while scanning them; retry after the scan

### Application Not in Start Menu

**Symptom:** Installation completes but Agent-X doesn't appear in Start Menu search

**Solutions:**

1. **Look for the Agent-X entry**
   - The installer always adds an "Agent-X" entry to the Start Menu; Windows search can take a moment to index it

2. **Manual shortcut**
   - Open the installation folder (`%LocalAppData%\Programs\Agent-X` for a per-user install)
   - Right-click `AgentX.App.exe`
   - Choose Send to > Desktop (create shortcut)

3. **Reinstall**
   - Uninstall Agent-X, then run the installer again

---

## Startup & Launch Issues

### Nothing Happens When I Start Agent-X

**Symptom:** Starting Agent-X shows no window

**Solutions:**

1. **Look in the notification area**
   - Closing the Agent-X window hides it in the notification area instead of quitting
   - Double-click the tray icon, or right-click it and choose **Open Agent-X**

2. **Check for a second copy**
   - Nothing stops a second copy from starting; if Task Manager shows more than one `AgentX.App.exe`, end the extra one and use the first
   - To quit Agent-X for real, use **Exit** in the tray menu

### Application Crashes on Launch

**Symptom:** Agent-X opens then immediately closes

**Solutions:**

1. **Read the log**
   - Open the newest `agentx-YYYYMMDD.log` in `%LocalAppData%\AgentX\Logs` and look for `[ERR]` and `[FTL]` lines

2. **Check the settings file**
   - An unreadable `settings.json` does not stop Agent-X: the file is copied to `settings.json.corrupt-<time>`, the problem is logged, and the session runs on default settings
   - To start from defaults yourself, see [Reset Application Settings](#reset-application-settings)

3. **Reinstall**
   - The installer contains everything the app needs (.NET and the Windows App SDK are bundled); a reinstall replaces damaged program files and keeps your data

4. **Check Windows Event Log**
   - Open Event Viewer (`eventvwr.msc`), go to Windows Logs > Application, and look for errors from `AgentX.App.exe`

### "Agent-X could not start"

**Symptom:** A dialog says the database schema is incomplete or could not be upgraded to the latest version

To protect your data, Agent-X stopped before loading any feature. Backups can only be restored from inside Agent-X, so do this instead:

1. Copy the whole `%LocalAppData%\AgentX` folder somewhere safe
2. Report the problem with the log files from `%LocalAppData%\AgentX\Logs`
3. To go back to a backup by hand: with Agent-X closed, delete `agentx.db-wal` and `agentx.db-shm` from `%LocalAppData%\AgentX` if they exist, then replace `agentx.db` with the `database\agentx.db` file from a `.agentxbak` backup made without a password (the backup is a ZIP archive). Backups made with a password can only be opened by Agent-X.

### Unlock Prompt Does Not Accept the Passphrase

**Symptom:** "Unlock your Agent-X database" appears at startup and says "Incorrect passphrase"

This prompt appears only for a database that an earlier version encrypted with a passphrase. Encryption turned on in current versions uses a key tied to your Windows account and asks for nothing.

1. **Caps Lock / keyboard layout**
   - Check Caps Lock, and that the keyboard layout is the one you used when you set the passphrase

2. **No recovery**
   - Agent-X cannot open the database, or restore a backup of it, without the correct passphrase. Choose **Exit app** to stop

3. **Last resort: start with an empty database**
   ```
   WARNING: this removes access to all your data

   1. Close Agent-X (Exit in the tray menu)
   2. Move %LocalAppData%\AgentX\agentx.db, agentx.db-wal and agentx.db-shm
      (if present) and %LocalAppData%\AgentX\encryption.info.json to another folder
   3. Start Agent-X (it creates a new, empty database)
   ```

### The Status Strip Stays on "Initializing..."

**Symptom:** The model readout in the status strip keeps showing "Initializing..." after launch

**Solutions:**

1. **Wait a little**
   - Startup unlocks and upgrades the database, then starts the AI provider (with the built-in model active, the 2 GB model file is loaded), the indexing pipeline and the watch folders. The strip is updated a few seconds after the window opens and every 30 seconds after that

2. **Check the log**
   - Look for warnings about the AI service, the indexing pipeline or the migration in the day's log file

---

## AI & Model Issues

### "Built-in LLM not available"

**Symptom:** Chat answers "Unable to generate a response: Built-in LLM is not available" or the status strip says the built-in model is not available

**Solutions:**

1. **Check that the model is installed**
   - The file is `%LocalAppData%\AgentX\Models\llama-3.2-3b-instruct-q4_k_m.gguf` (about 2 GB)
   - To download it, open the Model Manager with **Built-in LLM (Local)** active, type `llama-3.2-3b-instruct-q4_k_m.gguf` under Pull New Model and click Pull Model; or reopen the wizard with `Ctrl+P` > Onboarding and use **Download built-in model**

2. **Free memory**
   - The model has to be loaded into memory; close memory-heavy programs and try again

3. **Or choose another provider**
   - Settings > AI Providers > **Active Provider**, then **Save Settings**

### "Ollama not available"

**Symptom:** Ollama is the active provider and chat or the Model Manager cannot reach it

**Solutions:**

1. **Check that Ollama is running**
   - In a terminal, `ollama list` answers when the server is up; `ollama serve` starts it

2. **Check the endpoint**
   - Settings > AI Providers > Ollama (Local) > **Endpoint** (`http://localhost:11434` by default), then **Test Connection**
   - "Invalid endpoint (use http://host:port)" means the address is not a full `http://` or `https://` URL

3. **Check the model**
   - `ollama list` must show the **Default Chat Model**; pull it with `ollama pull llama3.2` or on the Model Manager page
   - For Ollama on another computer, set `OLLAMA_HOST` there and allow its port through the firewall

### AI Responses Are Garbled or Incoherent

**Symptom:** AI generates nonsensical or random text

**Solutions:**

1. **Lower temperature**
   - Settings > Inference > **Temperature** (0.7 by default); try 0.2 to 0.4 and click **Save Settings**
   - Some models do not take a temperature: Anthropic receives at most 1, and newer models such as Claude Sonnet 5 and OpenAI's reasoning models get none

2. **Switch models**
   - Try another model in the model box at the top of AI Chat

3. **Start a new conversation**
   - `Ctrl+N`; long conversations are summarized to fit the context window

4. **Download the model again**
   - Delete it on the Model Manager page and pull it again (Delete does not ask for confirmation)

### AI Not Responding / Times Out

**Symptom:** A reply never finishes

**Solutions:**

1. **Stop and retry**
   - **Stop generation** ends the reply and keeps what was written; there is no timeout setting

2. **Check system resources**
   - Open Task Manager and make sure CPU and memory are not maxed out

3. **Reduce the context**
   - Lower **Context Window** or **Max Tokens** under Settings > Inference

4. **For GPU: check video memory**
   - See [Out of Memory Errors (GPU)](#out-of-memory-errors-gpu)

### Cloud Provider API Errors

**Symptom:** Errors using OpenAI or Anthropic

**Solutions:**

1. **Verify the API key**
   - Settings > AI Providers > OpenAI or Anthropic Claude > **API Key**, then **Test Connection**
   - "Authentication failed" means the provider rejected the key; "API key required" means the field is empty

2. **Check the model name**
   - **Default Model** must be a model your key can use (`gpt-4o-mini` and `claude-sonnet-5` by default)

3. **Check billing and limits**
   - Make sure the provider account has credit and has not hit a rate limit; the day's log records the provider's error message

4. **Check network**
   - Make sure the computer can reach the provider's **Endpoint** (for example through a proxy or firewall)

### Research Mode Adds No Web Sources

**Symptom:** Chat answers without Web sources although Research Mode is on

**Solutions:**

1. **Check both switches**
   - **Enable Research Mode** under Settings > Research Mode, and the globe button beside the chat's message box, must both be on
   - The chat says once why it did not search

2. **Check the provider**
   - Choose the **Search Provider** and fill **API Key or Instance URL**: the API key for Brave or Serper, the instance address (for example `http://localhost:8080`) for SearXNG. Only the selected provider is used

3. **Timeouts and cache**
   - A search gives up after 15 seconds; repeated questions reuse cached results for **Cache Duration (minutes)** (60 by default)

---

## Import & Indexing Issues

### A File Is Marked Failed

**Symptom:** A document shows **Failed**; the row and the Document Preview (WHY INDEXING FAILED) show the reason

**Common reasons:**

| Reason | Solution |
|--------|----------|
| No extractable text layer (scanned PDF) | Run the PDF through OCR software first; Agent-X has no OCR for PDFs |
| Fonts whose encoding cannot be decoded (PDF) | Print or export the PDF again from its source application |
| Encrypted or damaged file | Open it in its own application and save an unprotected copy |
| No OCR language installed (image) | Add a Windows language that includes optical character recognition |
| The speech-to-text model is not installed (audio) | Install it on the Model Manager page; the file is transcribed afterwards |
| Source file no longer exists | The original file was moved or deleted; import it again from its new place |

Fix the cause, then click **Re-index** on the row, or **Retry Index** on the Operations page.

### "No Processor Found" / File Not Offered

**Symptom:** A file type is rejected, or does not appear in the Import Files picker

**Solutions:**

1. **Check the format**
   - See the supported formats in the [FAQ](faq.md#what-file-formats-does-agent-x-support); legacy `.doc`, `.rtf`, Excel, PowerPoint and EPUB files are not supported

2. **Use drag and drop for other formats**
   - The Import Files picker lists document, data and code formats only; drag images, audio and `.url` files onto the Knowledge Vault page, or use Import Folder

### Documents Stay Pending or Processing

**Symptom:** New documents never reach **Indexed**

**Solutions:**

1. **Check the embedding model**
   - Indexing needs embeddings. With the default **Embedding Model** (`all-minilm`), Agent-X uses the built-in model when it is installed, and otherwise Ollama, which must be running with `all-minilm` pulled (`ollama pull all-minilm`)
   - Anthropic has no embeddings, so an Anthropic-only setup still needs the built-in model or Ollama

2. **Check the Operations page**
   - Recent imported documents shows errors; **Retry Index** queues a document again

3. **Check the log**
   - Search the day's log for "Indexing pipeline" and "Failed to index"

### Import Is Very Slow

**Symptom:** Import progress stays on the same file

**Solutions:**

1. **Large files and audio take time**
   - Audio is transcribed while it is imported, so a long recording takes a while
   - Very large documents produce many chunks to embed

2. **Import fewer files at once**
   - Indexing runs in the background after the import; the vault's **In Queue** figure shows what is left

### Documents Not Appearing After Import

**Symptom:** Import completes but documents don't show

**Solutions:**

1. **Refresh the view**
   - Press `F5` on the Knowledge Vault page, or click the refresh button

2. **Clear filters**
   - Click **Clear All**; check the file type, status, tag, Collection and date filters
   - The search box filters by file name and tag only, not by content

3. **Check the import summary**
   - The banner says how many files were imported, skipped as duplicates or not imported, with the first reason

### Watch Folder Files Are Not Imported

**Symptom:** Files added to a watch folder do not show up

**Solutions:**

1. **Check the switch**
   - Settings > Knowledge Vault > **Auto-index watch folders** must be on, and saved with **Save Settings**

2. **Check the folder**
   - The folder must be listed under **Watch Folders** and still exist; files in subfolders are imported only when **Include subfolders** was checked when you added it

3. **Look in the vault, not the inbox**
   - Watched files go straight into the Knowledge Vault; the Smart Inbox is for browser clips

### Auto-Generated Titles Are Wrong

**Symptom:** Document titles don't reflect content

**Solutions:**

1. **Understand where titles come from**
   - At import the title is taken from the document itself (its properties, first heading or first declaration)
   - **AI Title** on a row, or **Generate Title** in the Document Preview, asks the model for a better one; the file is not renamed

2. **Check content quality**
   - A document without extractable text gets no useful title

---

## Search Issues

### Search Returns No Results

**Symptom:** Search shows "No Results Found"

**Solutions:**

1. **Verify documents are indexed**
   - The Knowledge Vault shows **Indexed** for searchable documents

2. **Try a different search mode**
   - Switch between Semantic, Keyword and Hybrid under SEARCH MODE

3. **Relax the filters**
   - Lower **MIN RELEVANCE** under Advanced (30% by default), clear the Collection and file type filters, or click **Clear Filters**

4. **Re-index after changing the embedding model**
   - Chunks embedded with another model are left out of semantic search; select the documents in the Knowledge Vault and click **Re-index**

### Search Results Are Irrelevant

**Symptom:** Results don't match what you're looking for

**Solutions:**

1. **Use natural language**
   - Semantic search works best with questions: instead of "deployment", try "how do we deploy the service"

2. **Try hybrid search**
   - Combines semantic and keyword ranking

3. **Use Keyword mode for exact terms**
   - Names, IDs and code match best in Keyword mode; the words of a query are matched separately, and common words are ignored

4. **Scope the search**
   - Pick a Collection or a file type

### Search Is Slow

**Symptom:** Search takes several seconds or more

**Solutions:**

1. **Large vaults**
   - Up to about 10,000 chunks are scanned exactly; above that an HNSW index is built in memory. With database encryption on, that index is rebuilt at every start

2. **Use Keyword mode**
   - Keyword search does not compute embeddings for the query

3. **Check system resources**
   - Indexing and chat compete for the CPU and GPU; results are cached for five minutes

---

## Performance Issues

### Application Is Generally Slow

**Symptom:** All operations feel sluggish

**Solutions:**

1. **Check what runs in the background**
   - Indexing new documents (embedding every chunk), a model download, a web import or a sync pass all use CPU, disk or network
   - The status strip shows the indexing queue

2. **Check system resources**
   - Open Task Manager and check CPU, memory and disk use

3. **Avoid overlapping heavy work**
   - Let a large import finish before a long chat or an Ask Your Files question

### High Memory Usage

**Symptom:** Agent-X uses a lot of memory

**Solutions:**

1. **Use a smaller model**
   - The built-in model's file is about 2 GB and is kept in memory while it is active; a smaller Ollama model or the Llama 3.2 1B file needs less

2. **Reduce the context**
   - Lower **Context Window** under Settings > Inference

3. **Web Import**
   - Rendering a script-built page starts a headless browser for a moment, which uses extra memory

4. **Restart after large jobs**
   - Exit from the tray menu and start Agent-X again

### UI Freezes During AI Operations

**Symptom:** Interface becomes unresponsive while AI generates

**Solutions:**

1. **Offload the built-in model to an NVIDIA GPU**
   - Install the NVIDIA CUDA 12 Toolkit and keep **Automatic GPU layers** on (Settings > AI Providers > Built-in LLM (Local)); see [GPU Acceleration Issues](#gpu-acceleration-issues)

2. **Use a smaller model**
   - Larger models use more resources

3. **Wait for one response to complete**
   - Regenerate and Save & Resend are refused while a response is generating

---

## Database Issues

### "Database Locked" or "Database in Use"

**Symptom:** Operations fail with a locked database, or a restore reports that the database is in use

**Solutions:**

1. **Close other copies**
   - Check Task Manager for more than one `AgentX.App.exe` and exit the extra copy

2. **Close other programs**
   - A backup, sync or antivirus tool may hold `agentx.db`; close it and try again. A refused restore changes nothing

3. **Restart**
   - Exit Agent-X from the tray menu, wait a few seconds and start it again; restart Windows if the lock remains

### Database Corruption Detected

**Symptom:** Agent-X reports that the database is damaged or cannot be upgraded

**Solutions:**

1. **Restore from backup**
   - Agent-X makes backups on its own only when scheduled backups are on (Backup & Restore > Scheduled Backups > **Back up automatically**); they go to the folder set there, by default `%LocalAppData%\AgentX`
   - Restore the `.agentxbak` file on the Backup & Restore page (**Restore from Backup**). Restore checks the backup first and puts the previous database back if anything fails; restart Agent-X when it says so
   - If Agent-X cannot start at all, see ["Agent-X could not start"](#agent-x-could-not-start)

2. **Keep a copy first**
   - Copy the whole `%LocalAppData%\AgentX` folder before trying anything else

3. **Start over**
   - Export what you can (conversations with Export conversation, annotations with Export as Markdown), then move `agentx.db` away and let Agent-X create a new one; import your documents again

### Database File Is Very Large

**Symptom:** `agentx.db` is several GB

**Solutions:**

1. **Know what takes the space**
   - The database holds every document's extracted text and chunks and one embedding per chunk: 1,536 bytes with `all-minilm`, 12,288 bytes with the built-in model

2. **Choose a smaller embedding model**
   - With the default **Embedding Model** (`all-minilm`) and the built-in model installed, embeddings come from the built-in model (3,072 dimensions). To use Ollama's `all-minilm` (384 dimensions) instead, enter `all-minilm:latest`, pull it in Ollama, click **Save Settings**, and re-index the documents

3. **Delete what you do not need**
   - Delete documents in the Knowledge Vault and old conversations in AI Chat. Agent-X has no command to compact the database, so the file does not shrink, but the space is reused

---

## GPU Acceleration Issues

### GPU Not Detected

**Symptom:** The onboarding wizard or the log says the model runs on the CPU

**Solutions:**

1. **Verify an NVIDIA GPU**
   - The built-in model offloads only to NVIDIA GPUs; AMD and Intel GPUs are not used
   - The Hardware Advisor page shows the GPU and video memory Windows reports

2. **Install the NVIDIA CUDA 12 Toolkit**
   - Agent-X ships the CUDA 12 build of llama.cpp but not the CUDA runtime it loads; without the toolkit (with `CUDA_PATH` pointing at it) the model stays on the CPU

3. **Check video memory**
   - With **Automatic GPU layers**, a GPU with under 2 GB of video memory gets no layers

4. **Update GPU drivers**
   - Install a current NVIDIA driver that supports CUDA 12

### Out of Memory Errors (GPU)

**Symptom:** The built-in model fails to load, or the log reports a CUDA memory error

**Solutions:**

1. **Lower the layer count**
   - Settings > AI Providers > Built-in LLM (Local): turn **Automatic GPU layers** off and enter a lower **GPU Layers** value, then **Save Settings** (the model reloads without a restart)

2. **Close other GPU applications**
   - Games, video editing and other AI tools use video memory too

3. **Use a smaller model**
   - The Llama 3.2 1B file needs less video memory than the 3B model

4. **Fall back to CPU**
   - Set **GPU Layers** to 0; it is slower but needs no video memory

### GPU Acceleration Not Improving Performance

**Symptom:** GPU offload is set up but replies are not faster

**Solutions:**

1. **Verify GPU is actually being used**
   - Open Task Manager > Performance > GPU; it should show activity while the model writes
   - The log line "Loading local LLM from ... (GPU layers: N)" shows the count the model was loaded with

2. **Check that the toolkit is found**
   - Without the CUDA 12 Toolkit the CPU backend runs whatever the setting says

3. **Update GPU drivers**
   - Check NVIDIA for current drivers

---

## Sync & Integration Issues

### Collaborative Sync Does Not Bring Changes Over

**Symptom:** Sync Now reports problems, or changes from another computer do not arrive

**Solutions:**

1. **Check the configuration**
   - Every installation must use the same **Sync Folder** (reachable from each computer) and the same **Encryption Key**; save with **Save Configuration**

2. **Read the result**
   - **Sync Now** exports this computer's changes and imports the other computers' files, then reports what was exported, imported, retried or unreadable; Sync History lists every pass. A file that cannot be decrypted usually means a different Encryption Key

3. **Know what sync carries**
   - Documents arrive as records: one whose file is not at the same path on this computer arrives as failed. Chat messages and settings are not synced (see [KNOWN-ISSUES](../KNOWN-ISSUES.md))

### Browser Extension Not Working

**Symptom:** The AgentX Web Clipper popup shows **Offline** or **Not paired**

**Solutions:**

1. **Verify Agent-X is running**
   - Agent-X must be running (it can be hidden in the notification area)

2. **Check the local API**
   - Agent-X runs its local API on `http://localhost:9846` (this computer only)
   - **Enable Local API** must be on under Settings > Connections; turning it on or off takes effect when you click **Save Settings**

3. **Check the API token**
   - Copy the token from Settings > Connections (**Copy**), paste it into the popup and click Save; the popup says "Paired with AgentX."
   - **Not paired** means Agent-X answered but rejected the token; after **Regenerate**, every client must be given the new token
   - When paired, the popup header shows the Agent-X version

4. **Reload the extension**
   - Remove and load the extension again in the browser's extensions page, then restart the browser

### Calendar/Email Connector Not Syncing

**Symptom:** Calendar or email shows no data, or Connect fails

**Solutions:**

1. **Enter your OAuth app credentials**
   - "Google sign-in is not set up" or "Microsoft sign-in is not set up" means no client is saved. Agent-X ships no OAuth credentials: fill in **OAuth App Credentials** on the Calendar or Email page and click **Save Credentials** (it applies at once, without a restart)
   - Google: create an OAuth client ID of type Desktop app in the Google Cloud Console (APIs & Services > Credentials), enable the Gmail and Google Calendar APIs, add your account as a test user, then enter the **Client ID** (ending in `.apps.googleusercontent.com`) and **Client secret**
   - Microsoft: register an app in the Microsoft Entra admin center for accounts in any organizational directory and personal Microsoft accounts, add the "Mobile and desktop applications" platform with the **Redirect URI** the page shows (`http://localhost:8401/oauth/callback` by default), and enter the **Application (client) ID**. No secret is needed
   - To change a client ID while an account is connected, click **Disconnect** under Connected Accounts first

2. **Finish the sign-in**
   - Connect opens your browser; complete the consent there within five minutes
   - "Reconnect required" on a Microsoft account: click **Disconnect**, then **Connect** again

3. **Turn sync on**
   - **Calendar sync** or **Email sync** must be **On**; click **Save Settings**. **Sync Now** turns the switch on, saves and syncs at once
   - Email: under **Folders to sync**, check at least one folder (**Refresh folders** lists them); with none checked, no mail is synced

4. **Look in the right place**
   - Synced events and messages are added to the Knowledge Vault as documents, and listed in the Smart Inbox under the accepted status

---

## Advanced Diagnostics

### Log Files

Agent-X always writes detailed logs; there is nothing to switch on.

1. Open `%LocalAppData%\AgentX\Logs`
2. The current file is `agentx-YYYYMMDD.log` (one per day, kept for seven days)
3. Search for `[ERR]`, `[FTL]` or `[WRN]`
4. Include the relevant log when you report a bug, after checking it for anything you do not want to share (file paths, document names)

### Reset Application Settings

**Reset to defaults:**

- **Reset to Defaults** at the bottom of the Settings page asks first, then resets the page's provider, model, inference, Knowledge Vault, routing, Research Mode and local API switches and saves them at once. It also clears the OpenAI, Anthropic and web search keys; it keeps the local API token, the watch folders, the theme and the language.
- To reset everything in `settings.json`:

```
WARNING: API keys, OAuth app credentials, the local API token and the
backup schedule are lost; your data is kept

1. Close Agent-X (Exit in the tray menu)
2. Rename %LocalAppData%\AgentX\settings.json (for example to settings.json.old)
3. Start Agent-X; it creates a new settings.json with defaults
4. Enter your settings again, and paste the new API token into the browser
   extension and the Android app
```

### Re-index Documents

There is no single "rebuild all indexes" command:

1. Open the Knowledge Vault
2. Turn on multi-select, click **Select All**, and click **Re-index**
3. Each document's chunks, embeddings and keyword entries are rebuilt from its original file (files that were moved or deleted are marked Failed)

Documents embedded before embedding models were recorded are re-embedded automatically in the background.

### Back Up and Export Your Data

- **Everything in the database**: Backup & Restore > **Create Backup** (optionally with a password and **Include indexed documents**)
- **Conversations**: **Export conversation** at the top of AI Chat (Markdown, HTML, PDF, JSON, plain text, CSV, Word or PowerPoint), or Export all conversations at the bottom of the conversation list
- **Annotations**: **Export as Markdown** on the Annotations page
- **Collections**: Export on a Collection saves a ZIP with a list of its documents (their details, not the files) to `%LocalAppData%\AgentX\Exports`

---

## Contact Support

If none of these solutions resolve your issue:

| Resource | Contact Method |
|----------|----------------|
| **GitHub Issues** | Report bugs or request features on the project repository |
| **Security problems** | Report privately as described in [SECURITY.md](../../SECURITY.md) |
| **Email** | support@strategia-x.com |

**When reporting issues, include:**

1. Agent-X version (shown at the right end of the status strip and at the bottom of the Settings page)
2. Windows version
3. Steps to reproduce
4. Expected vs actual behavior
5. The day's log file (if applicable)

---

*Last updated: 2026-09-27*
