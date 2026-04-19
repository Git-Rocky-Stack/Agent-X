# B4: SyncService Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `SyncService.cs` (1,519 LOC) + `SyncSettingsViewModel.cs` (900 LOC) into `SyncTransport`, `SyncPackageCodec`, and `SyncConflictResolver`. SyncService becomes thin orchestrator.

**Architecture:** Extract three concerns — network I/O (transport), serialization (codec), conflict resolution — into separate services with interfaces. SyncService composes them. SyncSettingsViewModel gets its own cleanup pass.

**Tech Stack:** C#, .NET 8, System.Net.Http, System.Security.Cryptography (AES-256-GCM), xUnit

---

### Task 1: ISyncTransport + SyncTransport + Tests

**Files:**
- Create: `src/AgentX.Core/Services/Sync/Transport/ISyncTransport.cs`
- Create: `src/AgentX.Core/Services/Sync/Transport/SyncTransport.cs`
- Create: `tests/AgentX.Tests/Services/Sync/Transport/SyncTransportTests.cs`

- [ ] **Step 1: Define ISyncTransport interface**

```csharp
public interface ISyncTransport
{
    Task<SyncPackage?> FetchRemoteAsync(string endpoint, CancellationToken ct);
    Task PushLocalAsync(string endpoint, SyncPackage package, CancellationToken ct);
    Task<bool> TestConnectionAsync(string endpoint);
    Task<long> GetRemoteVersionAsync(string endpoint);
}
```

- [ ] **Step 2: Write failing tests**

Tests: FetchRemoteAsync sends GET and deserializes response, PushLocalAsync sends POST with serialized package, TestConnectionAsync returns true for reachable endpoint, handles HTTP errors gracefully, respects cancellation, uses configured timeout.

- [ ] **Step 3: Extract transport logic from SyncService (HTTP client, lines 151-504)**

Move: HttpClient setup, retry logic, endpoint URL construction, request/response handling, timeout management, error mapping.

- [ ] **Step 4: Run tests**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~SyncTransport" --blame-hang-timeout 60s
```

---

### Task 2: ISyncPackageCodec + SyncPackageCodec + Tests

**Files:**
- Create: `src/AgentX.Core/Services/Sync/Codec/ISyncPackageCodec.cs`
- Create: `src/AgentX.Core/Services/Sync/Codec/SyncPackageCodec.cs`
- Create: `tests/AgentX.Tests/Services/Sync/Codec/SyncPackageCodecTests.cs`

- [ ] **Step 1: Define ISyncPackageCodec interface**

```csharp
public interface ISyncPackageCodec
{
    byte[] Serialize(SyncPackage package);
    SyncPackage Deserialize(byte[] data);
    byte[] Encrypt(byte[] plaintext, byte[] key, byte[] nonce);
    byte[] Decrypt(byte[] ciphertext, byte[] key, byte[] nonce);
    string ComputeHash(byte[] data);
}
```

- [ ] **Step 2: Write failing tests**

Tests: Serialize/Deserialize round-trips SyncPackage, Encrypt/Decrypt round-trips data, ComputeHash produces consistent SHA-256, handles empty packages, handles large payloads, encryption uses AES-256-GCM (from AppConstants).

- [ ] **Step 3: Extract codec + encryption logic from SyncService (serialization 168-1088, encryption 345-1502)**

Move: JSON serialization, AES-256-GCM encryption/decryption (using AppConstants: AesKeyBytes, GcmNonceBytes, GcmTagBytes, PbkdfSaltBytes, Pbkdf2Iterations), hash computation, package framing.

- [ ] **Step 4: Run tests**

---

### Task 3: ISyncConflictResolver + SyncConflictResolver + Tests

**Files:**
- Create: `src/AgentX.Core/Services/Sync/ConflictResolution/ISyncConflictResolver.cs`
- Create: `src/AgentX.Core/Services/Sync/ConflictResolution/SyncConflictResolver.cs`
- Create: `tests/AgentX.Tests/Services/Sync/ConflictResolution/SyncConflictResolverTests.cs`

- [ ] **Step 1: Define ISyncConflictResolver interface**

```csharp
public interface ISyncConflictResolver
{
    IReadOnlyList<SyncConflict> DetectConflicts(SyncPackage local, SyncPackage remote);
    SyncPackage ResolveConflicts(SyncPackage local, SyncPackage remote, ConflictResolutionStrategy strategy);
}
```

- [ ] **Step 2: Write failing tests**

Tests: DetectConflicts finds timestamp collisions, ResolveConflicts with LocalWins keeps local version, RemoteWins keeps remote, Merge combines non-conflicting fields, handles empty conflict lists.

- [ ] **Step 3: Extract conflict logic from SyncService (lines 471-615)**

Move: conflict detection (timestamp comparison, field diffing), resolution strategies, conflict reporting.

- [ ] **Step 4: Run tests**

---

### Task 4: Thin SyncService + ViewModel Cleanup

**Files:**
- Modify: `src/AgentX.Core/Services/Sync/SyncService.cs` (thin to ≤400 LOC)
- Modify: `src/AgentX.Core/Services/Sync/ISyncService.cs` (unchanged interface)
- Modify: `src/AgentX.App/ViewModels/SyncSettingsViewModel.cs` (extract nested classes)
- Modify: `src/AgentX.App/App.xaml.cs` (register new services in DI)
- Create: `src/AgentX.App/ViewModels/SyncLogDisplayItem.cs` (extracted from ViewModel)
- Create: `src/AgentX.App/ViewModels/SyncHistoryItem.cs` (extracted from ViewModel)

- [ ] **Step 1: Refactor SyncService to compose extracted services**

```csharp
public class SyncService : ISyncService
{
    private readonly ISyncTransport _transport;
    private readonly ISyncPackageCodec _codec;
    private readonly ISyncConflictResolver _conflictResolver;

    public SyncService(ISyncTransport transport, ISyncPackageCodec codec, ISyncConflictResolver conflictResolver, ...)
    {
        _transport = transport;
        _codec = codec;
        _conflictResolver = conflictResolver;
    }
}
```

Methods delegate: ExportChangesAsync → _codec.Serialize → _transport.PushLocalAsync, ImportChangesAsync → _transport.FetchRemoteAsync → _codec.Deserialize, DetectConflictsAsync → _conflictResolver.DetectConflicts, etc.

- [ ] **Step 2: Extract nested classes from SyncSettingsViewModel**

Move SyncLogDisplayItem and SyncHistoryItem to separate files. SyncSettingsViewModel keeps only UI logic.

- [ ] **Step 3: Register new services in DI**

```csharp
services.AddSingleton<ISyncTransport, SyncTransport>();
services.AddSingleton<ISyncPackageCodec, SyncPackageCodec>();
services.AddSingleton<ISyncConflictResolver, SyncConflictResolver>();
```

- [ ] **Step 4: Run full test suite**

```bash
dotnet test AgentX.sln --blame-hang-timeout 60s
```

---

## Verification Gate

SyncService.cs ≤ 400 LOC. All new service tests pass. Sync functionality works identically.

## Commit Strategy

- `refactor(sync): ISyncTransport extracted from SyncService`
- `refactor(sync): ISyncPackageCodec with AES-256-GCM encryption`
- `refactor(sync): ISyncConflictResolver for conflict detection`
- `refactor(sync): thin SyncService orchestrator + ViewModel cleanup`
