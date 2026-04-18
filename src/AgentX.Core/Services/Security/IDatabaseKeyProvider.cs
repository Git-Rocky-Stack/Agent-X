namespace AgentX.Core.Services.Security;

public interface IDatabaseKeyProvider
{
    DatabaseKeyMaterial? Current { get; }
}
