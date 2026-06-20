using System;
using System.Threading;
using System.Threading.Tasks;
using AgentX.App.Services;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// AX-QA-003 follow-up (dashboard race): the data-ready gate that data-backed UI awaits before its
/// first database read. It must stay closed until startup explicitly opens it, release immediately
/// once open, and — when startup fails — release waiters via cancellation rather than hang forever.
/// </summary>
public class StartupGateTests
{
    [Fact]
    public void IsDataReady_is_false_until_signaled()
    {
        var gate = new StartupGate();
        gate.IsDataReady.Should().BeFalse();

        gate.SignalDataReady();

        gate.IsDataReady.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForDataReadyAsync_blocks_until_SignalDataReady()
    {
        var gate = new StartupGate();

        var wait = gate.WaitForDataReadyAsync();
        wait.IsCompleted.Should().BeFalse("the gate has not been opened yet");

        gate.SignalDataReady();

        // Completes promptly once opened.
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForDataReadyAsync_returns_immediately_when_already_open()
    {
        var gate = new StartupGate();
        gate.SignalDataReady();

        var wait = gate.WaitForDataReadyAsync();

        wait.IsCompletedSuccessfully.Should().BeTrue();
        await wait; // no throw
    }

    [Fact]
    public async Task WaitForDataReadyAsync_is_cancelled_when_startup_fails()
    {
        var gate = new StartupGate();

        var wait = gate.WaitForDataReadyAsync();
        gate.SignalStartupFailed();

        var act = async () => await wait;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "a failed startup must release waiters rather than hang the dashboard load forever");
        gate.IsDataReady.Should().BeFalse();
    }

    [Fact]
    public async Task WaitForDataReadyAsync_honors_caller_cancellation_without_opening_the_gate()
    {
        var gate = new StartupGate();
        using var cts = new CancellationTokenSource();

        var wait = gate.WaitForDataReadyAsync(cts.Token);
        cts.Cancel();

        var act = async () => await wait;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Caller cancellation must not have opened the shared gate for everyone else.
        gate.IsDataReady.Should().BeFalse();
    }

    [Fact]
    public void SignalDataReady_is_idempotent()
    {
        var gate = new StartupGate();

        gate.SignalDataReady();
        var act = () => gate.SignalDataReady();

        act.Should().NotThrow();
        gate.IsDataReady.Should().BeTrue();
    }

    [Fact]
    public async Task SignalDataReady_wins_when_it_precedes_SignalStartupFailed()
    {
        var gate = new StartupGate();

        gate.SignalDataReady();
        gate.SignalStartupFailed(); // must be ignored once already open

        gate.IsDataReady.Should().BeTrue();
        await gate.WaitForDataReadyAsync(); // no throw
    }
}
