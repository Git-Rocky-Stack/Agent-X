using AgentX.Core.Data.MigrationRunner;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Outcome of the critical, ordered startup sequence.
/// </summary>
/// <param name="MigrationSucceeded">
/// True when the database migration completed without throwing. Only when this is true may any
/// data-backed subsystem (REST API, connectors, FTS, data pages) be brought up.
/// </param>
/// <param name="IsRecoveryState">
/// True when migration failed and the app must enter a blocking recovery/error state instead of
/// continuing startup. Always the logical negation of <see cref="MigrationSucceeded"/>; surfaced as
/// its own flag so callers read intent at the call site rather than inverting a boolean.
/// </param>
/// <param name="MigrationResult">The migration result on success; null on failure.</param>
/// <param name="Failure">The captured exception on failure (e.g. <see cref="BaselineSchemaIncompleteException"/>); null on success.</param>
public sealed record StartupResult(
    bool MigrationSucceeded,
    bool IsRecoveryState,
    MigrationResult? MigrationResult,
    System.Exception? Failure);

/// <summary>
/// Owns the CRITICAL, ordered startup path so it is awaitable and unit-testable independent of WinUI.
/// </summary>
public interface IStartupOrchestrator
{
    /// <summary>
    /// Runs the critical startup sequence: AWAIT the database migration, then — only if it
    /// succeeds — start the REST API host and initialize built-in connectors, in that order. If the
    /// migration throws, returns a recovery-state result and starts NEITHER the API NOR the
    /// connectors (fail closed), so nothing runs against a broken schema.
    /// </summary>
    System.Threading.Tasks.Task<StartupResult> RunCriticalStartupAsync(
        System.Threading.CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IStartupOrchestrator"/>. Depends only on the migration runner and the two
/// lifecycle interfaces plus a logger — no WinUI types — so the ordering/fail-closed contract for
/// AX-QA-003 can be verified with mocks in AgentX.Tests.
///
/// Fail-closed boundary: the database migration is the single gate. If it throws, the data-backed
/// REST API and connectors are never started. The API and connectors themselves run against a known
/// VALID schema, so a failure in one of them is a connectivity problem (logged, best effort) and
/// does not block the other or re-enter the recovery state.
/// </summary>
public sealed class StartupOrchestrator : IStartupOrchestrator
{
    private readonly IMigrationRunner _migrationRunner;
    private readonly IApiHostLifecycleService _apiHostLifecycle;
    private readonly IBuiltinConnectorLifecycleService _connectorLifecycle;
    private readonly IStartupGate _startupGate;
    private readonly ILogger _log;

    public StartupOrchestrator(
        IMigrationRunner migrationRunner,
        IApiHostLifecycleService apiHostLifecycle,
        IBuiltinConnectorLifecycleService connectorLifecycle,
        IStartupGate startupGate,
        ILogger logger)
    {
        _migrationRunner = migrationRunner ?? throw new System.ArgumentNullException(nameof(migrationRunner));
        _apiHostLifecycle = apiHostLifecycle ?? throw new System.ArgumentNullException(nameof(apiHostLifecycle));
        _connectorLifecycle = connectorLifecycle ?? throw new System.ArgumentNullException(nameof(connectorLifecycle));
        _startupGate = startupGate ?? throw new System.ArgumentNullException(nameof(startupGate));
        _log = (logger ?? throw new System.ArgumentNullException(nameof(logger))).ForContext<StartupOrchestrator>();
    }

    public async System.Threading.Tasks.Task<StartupResult> RunCriticalStartupAsync(
        System.Threading.CancellationToken cancellationToken = default)
    {
        // ── Gate: the database migration MUST complete before anything data-backed comes up. ──
        MigrationResult migrationResult;
        try
        {
            migrationResult = await _migrationRunner.RunAsync(cancellationToken).ConfigureAwait(false);
            _log.Information(
                "Migration runner: db={DbPath} created={Created} applied={Applied} alreadyApplied={AlreadyApplied}",
                migrationResult.DatabasePath,
                migrationResult.DatabaseCreated,
                string.Join(",", migrationResult.AppliedMigrations),
                string.Join(",", migrationResult.AlreadyApplied));
        }
        catch (System.Exception ex)
        {
            // Fail closed: do NOT start the API, connectors, or any data-backed feature. Release any
            // data-ready waiters (e.g. the dashboard) via cancellation so they skip loading instead
            // of awaiting forever, then have the caller enter a blocking recovery/error state.
            _startupGate.SignalStartupFailed();
            _log.Error(
                ex,
                "Database migration failed — entering recovery state. REST API, built-in connectors, "
                + "and data-backed features will NOT be started to avoid running against a broken schema");
            return new StartupResult(
                MigrationSucceeded: false,
                IsRecoveryState: true,
                MigrationResult: null,
                Failure: ex);
        }

        // ── Past the gate: schema is valid. Open the data-ready gate IMMEDIATELY so data-backed UI
        //    (e.g. the dashboard, already showing its shell) can load in parallel with the API and
        //    connectors — those read no schema the dashboard needs, so there is no reason to make UI
        //    reads wait on them (AX-QA-003 follow-up: dashboard-load-vs-migration race). ──
        _startupGate.SignalDataReady();

        // ── Start data-backed lifecycle steps in defined order. ──

        // 1) Local REST API used by the browser extension and mobile companion.
        try
        {
            await _apiHostLifecycle.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            // Independent best-effort step against a valid schema — log and continue to connectors.
            _log.Error(
                ex,
                "REST API startup failed — browser extension and mobile companion connectivity will be unavailable");
        }

        // 2) First-party calendar/email connectors (now that the database is ready).
        try
        {
            await _connectorLifecycle.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            _log.Error(ex, "Built-in connector initialization failed");
        }

        return new StartupResult(
            MigrationSucceeded: true,
            IsRecoveryState: false,
            MigrationResult: migrationResult,
            Failure: null);
    }
}
