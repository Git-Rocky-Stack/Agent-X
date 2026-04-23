namespace AgentX.Core.AI.Context;

public interface IContextAssemblyService
{
    Task<ContextAssemblyResult> AssembleAsync(
        ContextAssemblyRequest request,
        CancellationToken ct = default);
}
