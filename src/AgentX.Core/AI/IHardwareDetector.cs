using AgentX.Core.AI.Models;

namespace AgentX.Core.AI;

/// <summary>
/// Detects system hardware capabilities relevant to local AI inference —
/// GPU, VRAM, NPU, CPU cores, and available system memory.
/// </summary>
public interface IHardwareDetector
{
    Task<HardwareCapability> DetectAsync(CancellationToken ct = default);
}
