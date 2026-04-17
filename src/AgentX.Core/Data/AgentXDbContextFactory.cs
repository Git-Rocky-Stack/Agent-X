using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentX.Core.Data;

/// <summary>
/// Design-time factory used by `dotnet ef` tooling to build a DbContext
/// without the full DI container. Points at a neutral temp SQLite path so
/// tooling never writes into the user's real AgentX database.
/// </summary>
public class AgentXDbContextFactory : IDesignTimeDbContextFactory<AgentXDbContext>
{
    public AgentXDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite("Data Source=agentx.design.db")
            .Options;

        return new AgentXDbContext(options);
    }
}
