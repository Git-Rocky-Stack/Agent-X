using Xunit;

namespace AgentX.Tests.TestFixtures;

public sealed class SqlCipherFixture
{
    public SqlCipherFixture()
    {
        SQLitePCL.Batteries_V2.Init();
    }
}

[CollectionDefinition("SqlCipher")]
public sealed class SqlCipherCollection : ICollectionFixture<SqlCipherFixture> { }
