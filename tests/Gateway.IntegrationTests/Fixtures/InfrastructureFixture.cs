using BuildingBlocks.Testing;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;

namespace Gateway.IntegrationTests.Fixtures;

/// <summary>
/// The two real, containerised dependencies (CONVENTIONS.md "Testing"), started once for the whole
/// assembly — never per test, never restarted between tests. Per-test isolation is
/// <see cref="GatewayFixture"/>'s job in principle, but there is currently nothing for it to
/// isolate: the Gateway writes nothing to Mongo for SignUp/Login (pure protocol translation, no
/// aggregate of its own), so there is no database state to reset between tests, and
/// <c>FakeIdentityResponder</c> is bound to the same fixed <c>"identity"</c> queue name
/// production Identity uses (see <see cref="GatewayFixture"/>), not a per-run unique one — this
/// will need revisiting the day the Gateway acquires a read model of its own to write to.
///
/// Mirrors Identity.IntegrationTests' InfrastructureFixture in every configuration property
/// except the distinguishing label below — duplicated rather than shared, same as the
/// Architecture/ suite would be were it not for the sync mechanism, because each service is its
/// own repo from Phase 2.5.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    private static readonly bool Reuse = Environment.GetEnvironmentVariable("CI") != "true";

    // GL-18 review: without this, Gateway's InfrastructureFixture is configured identically to
    // Identity.IntegrationTests' — same image, same everything Testcontainers' reuse hash looks
    // at — so `.WithReuse(true)` attaches to Identity's already-running container instead of
    // starting its own. That is not merely wasteful: Identity's IdentityFixture binds the real
    // SignUpHandler/LoginHandler to the real "identity" queue on that same broker, and
    // FakeIdentityResponder below binds to that same queue name, so RabbitMQ round-robins SignUp
    // between the real handler and the fake one across concurrent test runs — exactly the
    // scenario that flaked the error-path tests. A distinct label makes the two suites' reuse
    // hashes differ, so each gets its own container while the "identity" queue name itself stays
    // the real one (proving the Gateway routes to it correctly, not a renamed stand-in).
    //
    // GL-92: this fix was never carried to Identity-vs-GiftLists, which collided by the same
    // mechanism for seventy issues because nothing but this comment recorded the hazard.
    // SuiteLabelRuleTests in Gateway.UnitTests now derives this value from the .csproj name and
    // fails if any suite's containers go unlabelled or borrow another suite's label.
    private const string SuiteLabel = "gateway";

    // GL-93: ReliableReadiness replaces the builders' default wait strategies, which read
    // container history rather than probing the live server and so are not safe across the
    // restarts a `.WithReuse(true)` container accumulates locally — see its doc comment.
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7")
        .WithReuse(Reuse)
        .WithLabel("giftlist.suite", SuiteLabel)
        .WithReliableWaitStrategy()
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3.13-management")
        .WithReuse(Reuse)
        .WithLabel("giftlist.suite", SuiteLabel)
        .WithReliableWaitStrategy()
        .Build();

    public string MongoConnectionString => _mongo.GetConnectionString();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_mongo.StartReliablyAsync(SuiteLabel), _rabbitMq.StartReliablyAsync(SuiteLabel));
    }

    public async Task DisposeAsync()
    {
        await _mongo.DisposeAsync().AsTask();
        await _rabbitMq.DisposeAsync().AsTask();
    }
}
