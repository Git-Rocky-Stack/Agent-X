using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

public sealed class VoiceCoordinatorNativeLifecycleTests
{
    [Fact]
    public void RecordingStoppedCallback_DoesNotDisposeWaveInFromInsideNativeCallback()
    {
        var source = File.ReadAllText(ResolveVoiceCoordinatorSource());
        var callbackStart = source.IndexOf("private void OnRecordingStopped", StringComparison.Ordinal);
        callbackStart.Should().BeGreaterThanOrEqualTo(0);

        var cleanupStart = source.IndexOf("private void CleanupRecording", StringComparison.Ordinal);
        cleanupStart.Should().BeGreaterThan(callbackStart);

        var callbackBody = source[callbackStart..cleanupStart];
        callbackBody.Should().NotContain("_waveIn?.Dispose()");
        callbackBody.Should().NotContain("_waveIn.Dispose()");
    }

    private static string ResolveVoiceCoordinatorSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "AgentX.App",
                "ViewModels",
                "Coordinators",
                "VoiceCoordinator.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate VoiceCoordinator.cs from test output directory.");
    }
}
