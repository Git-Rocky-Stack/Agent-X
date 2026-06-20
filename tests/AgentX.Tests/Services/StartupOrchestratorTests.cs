using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentX.App.Services;
using AgentX.Core.Data.MigrationRunner;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// AX-QA-003 (startup): the critical startup path must AWAIT the database migration and
/// gate the REST API host and built-in connectors behind its success. If migration throws
/// (including the new <see cref="BaselineSchemaIncompleteException"/>), the orchestrator must
/// enter a recovery/failure state and start NEITHER the API NOR the connectors (fail closed).
/// On success it must start them in order: migration → API → connectors.
///
/// The orchestrator depends only on interfaces (<see cref="IMigrationRunner"/>,
/// <see cref="IApiHostLifecycleService"/>, <see cref="IBuiltinConnectorLifecycleService"/>, a
/// Serilog logger) and carries no WinUI dependency, so it is unit-testable here without the app.
/// </summary>
public class StartupOrchestratorTests
{
    private static StartupOrchestrator CreateOrchestrator(
        Mock<IMigrationRunner> runner,
        Mock<IApiHostLifecycleService> api,
        Mock<IBuiltinConnectorLifecycleService> connectors,
        IStartupGate? gate = null)
        => new(runner.Object, api.Object, connectors.Object, gate ?? new StartupGate(), Serilog.Core.Logger.None);

    private static MigrationResult OkResult() =>
        new(DatabaseCreated: false,
            AppliedMigrations: Array.Empty<string>(),
            AlreadyApplied: Array.Empty<string>(),
            DatabasePath: "<test>");

    [Fact]
    public async Task Migration_failure_enters_recovery_state_and_does_not_start_api_or_connectors()
    {
        var runner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        var api = new Mock<IApiHostLifecycleService>(MockBehavior.Strict);
        var connectors = new Mock<IBuiltinConnectorLifecycleService>(MockBehavior.Strict);

        var failure = new InvalidOperationException("migration boom");
        runner
            .Setup(r => r.RunAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var orchestrator = CreateOrchestrator(runner, api, connectors);

        var result = await orchestrator.RunCriticalStartupAsync();

        // Fail closed: a recovery/failure state is signalled and the captured cause is surfaced.
        result.MigrationSucceeded.Should().BeFalse();
        result.IsRecoveryState.Should().BeTrue();
        result.Failure.Should().BeSameAs(failure);

        // The data-backed subsystems must NEVER come up against a broken schema.
        api.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        connectors.Verify(c => c.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Baseline_schema_incomplete_failure_also_fails_closed()
    {
        // The fail-closed backstop from AX-QA-002 throws BaselineSchemaIncompleteException; the
        // orchestrator must treat it like any migration failure and start nothing.
        var runner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        var api = new Mock<IApiHostLifecycleService>(MockBehavior.Strict);
        var connectors = new Mock<IBuiltinConnectorLifecycleService>(MockBehavior.Strict);

        var failure = new BaselineSchemaIncompleteException(new[] { "memories", "oauth_credentials" });
        runner
            .Setup(r => r.RunAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var orchestrator = CreateOrchestrator(runner, api, connectors);

        var result = await orchestrator.RunCriticalStartupAsync();

        result.MigrationSucceeded.Should().BeFalse();
        result.IsRecoveryState.Should().BeTrue();
        result.Failure.Should().BeSameAs(failure);
        api.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        connectors.Verify(c => c.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Successful_migration_starts_api_then_connectors()
    {
        var runner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        var api = new Mock<IApiHostLifecycleService>(MockBehavior.Strict);
        var connectors = new Mock<IBuiltinConnectorLifecycleService>(MockBehavior.Strict);

        // Record the real invocation order so we can assert migration → API → connectors.
        var callOrder = new List<string>();

        runner
            .Setup(r => r.RunAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("migration"))
            .ReturnsAsync(OkResult());
        api
            .Setup(a => a.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("api"))
            .Returns(Task.CompletedTask);
        connectors
            .Setup(c => c.InitializeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("connectors"))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(runner, api, connectors);

        var result = await orchestrator.RunCriticalStartupAsync();

        result.MigrationSucceeded.Should().BeTrue();
        result.IsRecoveryState.Should().BeFalse();
        result.Failure.Should().BeNull();

        // Strict ordering: the migration completes before the API starts, which completes before
        // the connectors initialize.
        callOrder.Should().Equal("migration", "api", "connectors");

        runner.Verify(r => r.RunAsync(It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        connectors.Verify(c => c.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Successful_migration_opens_data_gate_before_starting_api_and_connectors()
    {
        // AX-QA-003 follow-up (dashboard race): the data-ready gate must open the instant the
        // migration succeeds — BEFORE the API and connectors — so data-backed UI can load in
        // parallel with them rather than waiting on subsystems it does not depend on.
        var runner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        var api = new Mock<IApiHostLifecycleService>(MockBehavior.Strict);
        var connectors = new Mock<IBuiltinConnectorLifecycleService>(MockBehavior.Strict);
        var gate = new Mock<IStartupGate>(MockBehavior.Strict);

        var callOrder = new List<string>();
        runner.Setup(r => r.RunAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("migration")).ReturnsAsync(OkResult());
        gate.Setup(g => g.SignalDataReady()).Callback(() => callOrder.Add("gate"));
        api.Setup(a => a.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("api")).Returns(Task.CompletedTask);
        connectors.Setup(c => c.InitializeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("connectors")).Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(runner, api, connectors, gate.Object);

        var result = await orchestrator.RunCriticalStartupAsync();

        result.MigrationSucceeded.Should().BeTrue();
        callOrder.Should().Equal("migration", "gate", "api", "connectors");
        gate.Verify(g => g.SignalDataReady(), Times.Once);
        gate.Verify(g => g.SignalStartupFailed(), Times.Never);
    }

    [Fact]
    public async Task Migration_failure_fails_the_gate_and_never_opens_it()
    {
        // On migration failure the gate must be FAILED (releasing waiters via cancellation) and must
        // never be opened — so data-backed UI skips loading instead of querying a broken schema.
        var runner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        var api = new Mock<IApiHostLifecycleService>(MockBehavior.Strict);
        var connectors = new Mock<IBuiltinConnectorLifecycleService>(MockBehavior.Strict);
        var gate = new Mock<IStartupGate>(MockBehavior.Strict);

        gate.Setup(g => g.SignalStartupFailed());
        runner.Setup(r => r.RunAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("migration boom"));

        var orchestrator = CreateOrchestrator(runner, api, connectors, gate.Object);

        var result = await orchestrator.RunCriticalStartupAsync();

        result.IsRecoveryState.Should().BeTrue();
        gate.Verify(g => g.SignalStartupFailed(), Times.Once);
        gate.Verify(g => g.SignalDataReady(), Times.Never);
        api.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        connectors.Verify(c => c.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Api_failure_does_not_block_connectors_but_is_reported()
    {
        // The API host and connectors are independent best-effort lifecycle steps AFTER the gate.
        // A failure in one must not prevent the other from being attempted — the migration gate is
        // the only fail-closed boundary. (Both ran against a VALID schema, so partial failure here
        // is a connectivity problem, not a data-integrity one.)
        var runner = new Mock<IMigrationRunner>(MockBehavior.Strict);
        var api = new Mock<IApiHostLifecycleService>(MockBehavior.Strict);
        var connectors = new Mock<IBuiltinConnectorLifecycleService>(MockBehavior.Strict);

        runner
            .Setup(r => r.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkResult());
        api
            .Setup(a => a.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("api port in use"));
        connectors
            .Setup(c => c.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(runner, api, connectors);

        var result = await orchestrator.RunCriticalStartupAsync();

        // Migration succeeded — so this is NOT a recovery state — but connectors were still tried.
        result.MigrationSucceeded.Should().BeTrue();
        result.IsRecoveryState.Should().BeFalse();
        connectors.Verify(c => c.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
