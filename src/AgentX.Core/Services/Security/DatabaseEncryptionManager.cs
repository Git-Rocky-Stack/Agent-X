using AgentX.Core.Data;
using AgentX.Core.Data.VectorDb;
using Serilog;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Default <see cref="IDatabaseEncryptionManager"/>. Keeps the encryption marker and the database
/// file in agreement: the marker is written only at the migrator's commit point, after the
/// encrypted file is installed and verified, so a failure at any step leaves a plaintext database
/// and no marker (the state the next start expects).
/// <para>
/// The file swap runs with the vector store suspended (it keeps its own connection to the file)
/// and under the shared context's database gate, so no other connection to the file is open or
/// reopened while it is exported and replaced.
/// </para>
/// </summary>
public sealed class DatabaseEncryptionManager : IDatabaseEncryptionManager
{
    private readonly AgentXDbContext _db;
    private readonly IDatabaseKeyService _keyService;
    private readonly IDatabaseEncryptionMigrator _migrator;
    private readonly IDatabaseKeyProvider _keyProvider;
    private readonly IEncryptionStateFile _stateFile;
    private readonly IVectorStore? _vectorStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DatabaseEncryptionManager(
        AgentXDbContext db,
        IDatabaseKeyService keyService,
        IDatabaseEncryptionMigrator migrator,
        IDatabaseKeyProvider keyProvider,
        IEncryptionStateFile stateFile,
        IVectorStore? vectorStore = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _keyService = keyService ?? throw new ArgumentNullException(nameof(keyService));
        _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _stateFile = stateFile ?? throw new ArgumentNullException(nameof(stateFile));
        _vectorStore = vectorStore;
    }

    /// <inheritdoc />
    public bool IsEncryptionEnabled => _stateFile.Exists();

    /// <inheritdoc />
    public KeyStorageMode? ProvisionedMode
    {
        get
        {
            try
            {
                return _stateFile.Read()?.StorageMode;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                Log.Warning(ex, "Encryption marker exists but could not be read");
                return null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> EnableEncryptionAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_stateFile.Exists())
                return false;

            if (_keyProvider is not DatabaseKeyProvider keyProvider)
                throw new InvalidOperationException("Expected the DatabaseKeyProvider implementation of IDatabaseKeyProvider.");

            ct.ThrowIfCancellationRequested();

            // DPAPI-wrapped storage is the universal, transparent mode: the key is managed
            // automatically and tied to the Windows account.
            var provisioned = await _keyService
                .CreateUncommittedKeyAsync(KeyStorageMode.DpapiWrapped)
                .ConfigureAwait(false);
            var databasePath = SharedDatabaseConnection.GetDatabasePath(_db);

            // The vector store keeps its own connection to the file, which makes the swap fail on
            // Windows. It reopens afterwards with whichever key matches the file then.
            var vectorStoreSuspended = await SharedDatabaseConnection
                .TrySuspendVectorStoreAsync(_vectorStore, ct).ConfigureAwait(false);
            try
            {
                // On the thread pool: the export can take a while, and nothing in the gated
                // section may need the calling (UI) thread, which can itself be waiting for the
                // gate.
                await Task.Run(() => MigrateUnderDatabaseGateAsync(databasePath, provisioned, keyProvider), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (vectorStoreSuspended)
                {
                    await SharedDatabaseConnection
                        .ResumeVectorStoreAsync(_vectorStore!, reloadFromDatabase: false)
                        .ConfigureAwait(false);
                }
            }

            Log.Information("Database encryption enabled (mode={Mode})", provisioned.Key.Mode);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases the shared connection, migrates the file, commits the marker and reopens the
    /// connection, all under the context's database gate. EF work from other flows (status
    /// polling, indexing, background tasks) waits instead of reopening the connection on the
    /// plaintext file while it is exported and swapped, which failed the swap on Windows or lost
    /// writes made after the export.
    /// </summary>
    private async Task MigrateUnderDatabaseGateAsync(
        string databasePath,
        ProvisionedDatabaseKey provisioned,
        DatabaseKeyProvider keyProvider)
    {
        using (_db.EnterDatabaseGate())
        {
            SharedDatabaseConnection.Release(_db);
            try
            {
                await _migrator
                    .MigrateToEncryptedAsync(databasePath, provisioned.Key, () => CommitMarkerAsync(provisioned.Marker))
                    .ConfigureAwait(false);

                // Activate the key for THIS session so every later open is keyed.
                keyProvider.Set(provisioned.Key);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Database encryption failed; the database was left unencrypted");
                throw;
            }
            finally
            {
                // Reopen with whatever matches the file on disk now: the new key after a commit,
                // no key after a rollback.
                SharedDatabaseConnection.Reacquire(_db);
            }
        }
    }

    private async Task CommitMarkerAsync(EncryptionStateInfo marker)
    {
        try
        {
            await _stateFile.WriteAsync(marker).ConfigureAwait(false);
        }
        catch
        {
            // The migrator rolls the database back to plaintext after a failed commit, so a
            // marker that did reach disk must not survive either.
            try
            {
                _stateFile.Delete();
            }
            catch (Exception cleanupEx)
            {
                Log.Warning(cleanupEx, "Could not remove a partially written encryption marker");
            }

            throw;
        }
    }
}
