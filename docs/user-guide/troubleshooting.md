# Agent-X Troubleshooting Guide

**Solutions to common issues**

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

**Symptom:** Double-clicking the installer does nothing

**Solutions:**

1. **Verify Windows version**
   - Windows 10 build 19041+ (version 2004) required
   - Check: `Win + R` → `winver`
   - Update Windows if below minimum version

2. **Check antivirus blocking**
   - Temporarily disable antivirus
   - Add installer to antivirus exclusions
   - Re-run installer

3. **Run as administrator**
   - Right-click installer
   - Select "Run as administrator"
   - Attempt installation again

4. **Verify installer integrity**
   - Re-download the installer
   - Check file size matches expected (~100-150 MB)
   - Verify SHA-256 checksum if provided

### Installation Fails Mid-Process

**Symptom:** Installer stops or crashes during installation

**Solutions:**

1. **Free disk space**
   - Ensure at least 5 GB free space
   - Clear temporary files if needed
   - Select different installation drive

2. **Close other applications**
   - Close all Office applications
   - Close other AI/LLM applications
   - Disable backup software temporarily

3. **Check Windows Installer service**
   - Press `Win + R`, type `services.msc`
   - Find "Windows Installer"
   - Ensure it's running

4. **Clean boot**
   - Open System Configuration (`msconfig`)
   - Select "Selective startup"
   - Disable all non-Microsoft services
   - Reboot and retry installation

### Application Not in Start Menu

**Symptom:** Installation completes but Agent-X doesn't appear in Start Menu

**Solutions:**

1. **Manual shortcut**
   - Navigate to installation folder
   - Right-click `AgentX.exe`
   - Send to → Desktop (create shortcut)

2. **Rebuild search index**
   - Settings → Search → Windows Search
   - Run "Indexing troubleshooter"

3. **Reinstall**
   - Uninstall Agent-X
   - Reboot computer
   - Reinstall as administrator

---

## Startup & Launch Issues

### Application Crashes on Launch

**Symptom:** Agent-X opens then immediately closes

**Solutions:**

1. **Check for corrupt settings**
   ```
   Delete: %LocalAppData%\AgentX\settings.json
   Agent-X will recreate with defaults
   ```

2. **Verify .NET runtime**
   - Ensure .NET 8.0 Desktop Runtime is installed
   - Download from: https://dotnet.microsoft.com/download/dotnet/8.0

3. **Check Windows App SDK**
   - Windows App SDK 1.6+ required
   - Download from Microsoft Store or official website

4. **Enable detailed logging**
   ```
   Create: %LocalAppData%\AgentX\debug-mode.txt
   Add content: "true"
   Check: %LocalAppData%\AgentX\logs\ for error details
   ```

### Unlock Screen Not Accepting Passphrase

**Symptom:** Correct passphrase rejected at unlock screen

**Solutions:**

1. **Caps Lock / Num Lock**
   - Check keyboard indicator lights
   - Ensure unintended modifiers aren't active

2. **Keyboard layout**
   - Verify correct keyboard layout (EN-US vs other)
   - Check for language bar settings

3. **Recover via backup**
   - If you have a database backup, restore it
   - Location: `%LocalAppData%\AgentX\backups\`

4. **Last resort: reset database**
   ```
   ⚠️ WARNING: This deletes all data
   
   1. Close Agent-X
   2. Delete: %LocalAppData%\AgentX\data\agentx.db
   3. Delete: %LocalAppData%\AgentX\data\encryption.info.json
   4. Launch Agent-X (starts fresh)
   ```

### Application Freezes on Loading Screen

**Symptom:** Loading screen shows "initializing..." indefinitely

**Solutions:**

1. **Wait longer (first launch)**
   - First launch can take 1-2 minutes
   - Bundled model being decompressed

2. **Check for locked files**
   - Close all Office applications
   - Ensure no other Agent-X instance running
   - Reboot computer

3. **Disable startup services**
   ```
   Delete: %LocalAppData%\AgentX\services-startup.json
   Agent-X will skip optional startup services
   ```

4. **Check Windows Event Log**
   - Open Event Viewer (`eventvwr.msc`)
   - Navigate to Windows Logs → Application
   - Look for errors from "AgentX"

---

## AI & Model Issues

### "Model Not Found" Error

**Symptom:** Chat shows "Model not found" error

**Solutions:**

1. **Verify bundled model**
   - Check: `%LocalAppData%\AgentX\models\llama-3.2-3b\`
   - Folder should exist with model files
   - If missing, reinstall Agent-X

2. **Re-register bundled model**
   - Go to Settings → Model Manager
   - Click "Refresh Models"
   - Bundled model should reappear

3. **For Ollama models:**
   - Verify Ollama is running: `http://localhost:11434`
   - Check Ollama has the model: `ollama list`
   - Pull model if missing: `ollama pull llama3.2`

### AI Responses Are Garbled or Incoherent

**Symptom:** AI generates nonsensical or random text

**Solutions:**

1. **Lower temperature**
   - Go to Settings → AI Runtime
   - Set Temperature to 0.2-0.4
   - Lower = more deterministic

2. **Switch models**
   - Try a different model
   - Some models may be corrupted

3. **Clear conversation context**
   - Start a new conversation
   - Long contexts sometimes degrade quality

4. **Re-download model**
   - Delete model folder
   - Re-download from Model Manager

### AI Not Responding / Times Out

**Symptom:** Chat shows "Generating..." indefinitely

**Solutions:**

1. **Increase timeout**
   - Settings → AI Runtime → Response Timeout
   - Increase from default 60 seconds

2. **Check system resources**
   - Open Task Manager
   - Ensure CPU/memory not maxed out
   - Close other applications

3. **Reduce context window**
   - Settings → AI Runtime → Context Window
   - Smaller = faster processing

4. **For GPU: Check VRAM**
   - May be running out of GPU memory
   - Reduce offloading tier or use CPU

### Cloud Provider API Errors

**Symptom:** Errors using OpenAI or Anthropic

**Solutions:**

1. **Verify API key**
   - Check for typos in Settings → AI Providers
   - Regenerate key if compromised

2. **Check billing**
   - Ensure account has active subscription
   - Check for usage limits

3. **Test API directly**
   - Use provider's API tester
   - Confirm key works outside Agent-X

4. **Check network**
   - Ensure internet connection is active
   - Verify no firewall blocking API endpoints

---

## Import & Indexing Issues

### Import Fails with "Unsupported Format"

**Symptom:** File rejected during import

**Solutions:**

1. **Verify file type**
   - Check file extension matches supported formats
   - Rename with correct extension if misnamed

2. **Check file integrity**
   - Try opening file in original application
   - Resave if file appears corrupt

3. **Convert to supported format**
   - Use original application to export
   - PDF, TXT, MD, DOCX are safest

4. **Check file permissions**
   - Ensure file isn't read-only
   - Ensure you have read access

### Import Hangs or Is Very Slow

**Symptom:** Import progress stuck at same percentage

**Solutions:**

1. **Check file size**
   - Very large files (>100 MB) may take time
   - Consider splitting large documents

2. **Pause other activity**
   - Pause antivirus scan
   - Close other applications

3. **Reduce batch size**
   - Import fewer files at once
   - 10-20 files recommended per batch

4. **Check for password protection**
   - Remove password from PDF/Office files
   - Agent-X cannot access protected files

### Documents Not Appearing After Import

**Symptom:** Import completes but documents don't show

**Solutions:**

1. **Refresh the view**
   - Press `F5` or click refresh button
   - Close and reopen Knowledge Vault

2. **Check filters**
   - Clear all active filters
   - Reset to "All Documents" view

3. **Verify indexing completed**
   - Check document status in list view
   - "Indexing..." status means still processing

4. **Rebuild search index**
   - Settings → Advanced → Rebuild Index
   - May take several minutes for large vaults

### Auto-Generated Titles Are Wrong

**Symptom:** Document titles don't reflect content

**Solutions:**

1. **Edit manually**
   - Click document in Knowledge Vault
   - Edit title field
   - Press Enter to save

2. **Check content quality**
   - Ensure document has extractable text
   - Scanned PDFs need OCR preprocessing

3. **Disable auto-title**
   - Settings → Import
   - Uncheck "Auto-generate titles"

---

## Search Issues

### Search Returns No Results

**Symptom:** Search always shows "No results found"

**Solutions:**

1. **Verify documents are indexed**
   - Knowledge Vault shows "Indexed" status
   - Re-index if needed

2. **Try different search mode**
   - Switch between Semantic, Keyword, Hybrid
   - Each mode works differently

3. **Broaden query**
   - Use more general terms
   - Remove filters

4. **Check search history**
   - Previous search may have filters
   - Clear all filters and try again

### Search Results Are Irrelevant

**Symptom:** Results don't match what you're looking for

**Solutions:**

1. **Use natural language**
   - Semantic search works best with questions
   - Instead of "deployment", try "how to deploy"

2. **Try hybrid search**
   - Combines semantic + keyword
   - Often gives best results

3. **Use quotes for exact phrases**
   - `"project timeline"` searches for exact phrase
   - Useful for technical terms

4. **Leverage tags**
   - Filter by relevant tags
   - Reduces result space

### Search Is Slow

**Symptom:** Search takes several seconds or more

**Solutions:**

1. **Reduce vault size**
   - Archive old documents
   - Create smaller, focused collections

2. **Rebuild index**
   - Settings → Advanced → Rebuild Index
   - Optimizes index for performance

3. **Use keyword search**
   - Keyword search is faster than semantic
   - Good for exact term searches

4. **Check system resources**
   - Close other applications
   - Ensure sufficient RAM available

---

## Performance Issues

### Application Is Generally Slow

**Symptom:** All operations feel sluggish

**Solutions:**

1. **Check system resources**
   - Open Task Manager
   - Check CPU, RAM, disk usage
   - Close resource-intensive applications

2. **Disable animations**
   - Settings → Appearance
   - Disable "Reduce animations"

3. **Reduce concurrent operations**
   - Don't import and chat simultaneously
   - Wait for operations to complete

4. **Check for database bloating**
   - Large database can slow operations
   - Consider archiving old data

### High Memory Usage

**Symptom:** Agent-X using >2 GB RAM

**Solutions:**

1. **Reduce context window**
   - Settings → AI Runtime
   - Smaller context = less memory

2. **Clear conversation history**
   - Archive old conversations
   - Delete unnecessary chats

3. **Restart periodically**
   - Memory may accumulate over time
   - Restart daily if heavily used

### UI Freezes During AI Operations

**Symptom:** Interface becomes unresponsive while AI generates

**Solutions:**

1. **Enable GPU acceleration**
   - Offloads to GPU instead of CPU
   - Frees CPU for UI responsiveness

2. **Use smaller model**
   - Bundled model is most efficient
   - Larger models use more resources

3. **Reduce concurrent operations**
   - Don't run multiple chats simultaneously
   - Wait for one response to complete

---

## Database Issues

### "Database Locked" Error

**Symptom:** Operations fail with database locked message

**Solutions:**

1. **Close other instances**
   - Check Task Manager for `AgentX.exe`
   - End duplicate processes

2. **Check for backup**
   - Backup may be in progress
   - Wait for completion

3. **Restart application**
   - Graceful shutdown may clear locks
   - Reopen after a few seconds

4. **Last resort: force clear lock**
   ```
   ⚠️ Use only if application is definitely closed
   
   1. Open Process Explorer (sysinternals)
   2. Find "handle" for agentx.db
   3. Close handle
   ```

### Database Corruption Detected

**Symptom:** Application reports database corruption

**Solutions:**

1. **Restore from backup**
   - Agent-X automatically creates backups
   - Location: `%LocalAppData%\AgentX\backups\`
   - Copy backup to replace corrupt database

2. **Run integrity check**
   - Settings → Advanced → Database Integrity
   - Attempts to repair minor corruption

3. **Export data and recreate**
   - Export all documents
   - Note all conversation summaries
   - Delete database, reinstall, re-import

### Database File Is Very Large

**Symptom:** `agentx.db` is >1 GB

**Solutions:**

1. **Check for embedded documents**
   - Large documents stored in database
   - Consider using external storage for large files

2. **Vacuum database**
   - Settings → Advanced → Vacuum Database
   - Reclaims unused space

3. **Archive old conversations**
   - Export and delete old chats
   - Significantly reduces database size

---

## GPU Acceleration Issues

### GPU Not Detected

**Symptom:** GPU option grayed out or shows "Not detected"

**Solutions:**

1. **Verify NVIDIA GPU**
   - GPU acceleration requires NVIDIA with CUDA 12
   - AMD GPUs not currently supported

2. **Update GPU drivers**
   - Download latest from NVIDIA website
   - Install and restart

3. **Check CUDA installation**
   - Agent-X includes CUDA 12 runtime
   - May need separate CUDA toolkit for some GPUs

4. **Verify Windows Display Driver Model**
   - WDDM 2.0+ required
   - Check in GPU properties in Device Manager

### Out of Memory Errors (GPU)

**Symptom:** "CUDA out of memory" or similar error

**Solutions:**

1. **Reduce offloading tier**
   - Settings → AI Runtime → GPU Acceleration
   - Select lower VRAM tier

2. **Close other GPU applications**
   - Games, video editing, other AI tools
   - Free up VRAM

3. **Use smaller model**
   - Bundled 3B model uses least VRAM
   - 7B+ models require 8+ GB VRAM

4. **Fall back to CPU**
   - Disable GPU acceleration
   - Will be slower but more stable

### GPU Acceleration Not Improving Performance

**Symptom:** GPU enabled but no speedup

**Solutions:**

1. **Verify GPU is actually being used**
   - Open Task Manager → Performance → GPU
   - Should show activity during AI generation

2. **Check for bottleneck**
   - CPU may be bottleneck
   - Check system RAM usage

3. **Adjust batch size**
   - Settings → AI Runtime → Batch Size
   - Smaller batches may improve throughput

4. **Update GPU drivers**
   - Latest drivers often improve performance
   - Check NVIDIA for updates

---

## Sync & Integration Issues

### Sync Fails with Authentication Error

**Symptom:** Cloud sync shows "Authentication failed"

**Solutions:**

1. **Verify credentials**
   - Re-enter sync provider credentials
   - Check for typos

2. **Check service status**
   - Verify sync provider is operational
   - Check service status page

3. **Re-authorize**
   - Revoke Agent-X access
   - Re-authorize from scratch

4. **Check network**
   - Ensure internet connectivity
   - Verify no firewall blocking

### Browser Extension Not Working

**Symptom:** Browser extension doesn't connect to Agent-X

**Solutions:**

1. **Verify Agent-X is running**
   - Agent-X must be open for extension to work
   - Check system tray for Agent-X icon

2. **Check local server**
   - Agent-X runs HTTP server on port 9846
   - Verify not blocked by firewall

3. **Reinstall extension**
   - Uninstall and reinstall browser extension
   - Restart browser

4. **Check API key**
   - Extension may need API key
   - Configure in extension settings

### Calendar/Email Connector Not Syncing

**Symptom:** Calendar or email shows no data

**Solutions:**

1. **Verify provider configuration**
   - Check credentials in Settings → Connectors
   - Ensure account is properly linked

2. **Check permissions**
   - Agent-X needs read access
   - Re-grant permissions if revoked

3. **Manually trigger sync**
   - Settings → Connectors → Sync Now
   - Check for error messages

4. **Check service status**
   - Verify provider API is operational
   - Check provider status page

---

## Advanced Diagnostics

### Enable Debug Logging

**Enable detailed logging:**

1. Create file: `%LocalAppData%\AgentX\debug-mode.txt`
2. Add content: `true`
3. Restart Agent-X
4. Logs appear in: `%LocalAppData%\AgentX\logs\`

**Review logs:**
- Most recent log: `agentx-latest.log`
- Search for "ERROR" or "WARN"
- Include logs when reporting bugs

### Reset Application Settings

**Reset to defaults:**

```
⚠️ This resets all preferences but preserves your data

1. Close Agent-X
2. Delete: %LocalAppData%\AgentX\settings.json
3. Restart Agent-X
4. Reconfigure preferences
```

### Rebuild All Indexes

**Comprehensive index rebuild:**

1. Settings → Advanced
2. Click "Rebuild All Indexes"
3. Confirm operation
4. Wait for completion (may take several minutes)

This rebuilds:
- Document embeddings
- Full-text search index
- Knowledge graph
- Conversation memory

### Export Data for Backup

**Export all data:**

1. Settings → Advanced
2. Click "Export All Data"
3. Choose export location
4. Select format (JSON recommended)
5. Wait for export to complete

**What's exported:**
- All documents (with embeddings)
- All conversations
- All tags and collections
- Application settings

---

## Contact Support

If none of these solutions resolve your issue:

| Resource | Contact Method |
|----------|----------------|
| **GitHub Issues** | Report bugs or feature requests |
| **GitHub Discussions** | Community Q&A |
| **Email** | support@strategia-x.com (Enterprise only) |

**When reporting issues, include:**

1. Agent-X version
2. Windows version
3. Steps to reproduce
4. Expected vs actual behavior
5. Debug logs (if applicable)

---

*Last updated: 2026-05-03*
