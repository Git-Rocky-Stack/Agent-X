using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Settings;

namespace AgentX.Core.Services.Privacy;

/// <summary>
/// Derives the application's real privacy posture from its settings so the UI can make an accurate,
/// state-aware disclosure instead of an unconditional "your data never leaves this machine" claim
/// (AX-QA-008). A feature counts against full-local only when the user has actually enabled it.
/// </summary>
public interface IPrivacyStatusService
{
    /// <summary>
    /// Pure evaluation of a settings snapshot. Returns <see cref="PrivacyStatus.FullyLocal"/> when no
    /// enabled feature transmits data off the machine; otherwise a status listing every active
    /// cloud/third-party surface.
    /// </summary>
    PrivacyStatus Evaluate(AppSettings settings);

    /// <summary>Loads the current settings and evaluates them via <see cref="Evaluate"/>.</summary>
    Task<PrivacyStatus> GetCurrentAsync(CancellationToken cancellationToken = default);
}
