namespace AgentX.Core.Services.Security;

public sealed class DatabaseKeyProvider : IDatabaseKeyProvider
{
    private DatabaseKeyMaterial? _current;

    public DatabaseKeyMaterial? Current => _current;

    public void Set(DatabaseKeyMaterial? material) => _current = material;
}
