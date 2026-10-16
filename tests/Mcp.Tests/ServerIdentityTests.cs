namespace Snail.Sdk.Mcp.Tests;

public sealed class ServerIdentityTests
{
    /// <summary>A server built without Snail.Sdk.Mcp carries no command, and announces its assembly's name.</summary>
    [Fact]
    public void Server_WithoutTheCommandWritten_IsNamedByItsAssembly()
    {
        var identity = ServerIdentity.Of(typeof(ServerIdentityTests).Assembly);

        Assert.Equal(typeof(ServerIdentityTests).Assembly.GetName().Name, identity.Name);
        Assert.DoesNotContain("+", identity.Version, StringComparison.Ordinal);
    }
}
