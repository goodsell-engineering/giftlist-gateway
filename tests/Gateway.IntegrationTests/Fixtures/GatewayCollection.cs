namespace Gateway.IntegrationTests.Fixtures;

/// <summary>
/// One <see cref="GatewayFixture"/> — and therefore one pair of Testcontainers — per assembly
/// (CONVENTIONS.md "Testing": containers once per assembly, never per test).
/// </summary>
[CollectionDefinition(Name)]
public sealed class GatewayCollection : ICollectionFixture<GatewayFixture>
{
    public const string Name = "Gateway";
}
