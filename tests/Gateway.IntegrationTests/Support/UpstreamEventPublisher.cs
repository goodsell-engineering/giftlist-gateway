using BuildingBlocks.Messaging;
using BuildingBlocks.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Config;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Stands in for the services upstream of the Gateway on the real broker (CONVENTIONS.md
/// "Testing": "other services are not run — assert on contracts published to the real broker")
/// — publishes the exact <c>GiftLists.Contracts.GiftLists.Events.*V1</c> types GiftLists' own
/// <c>GiftListEventPublisher</c> does and, since GL-38, the
/// <c>Reservations.Contracts.Reservations.Events.GiftReservedV1</c> Reservations' does. One
/// host for both: Rebus publishes on the message's own .NET type name, so which service a
/// publish "comes from" is nothing this host knows or needs to. A bare Rebus host with no
/// handlers of its own, on a throwaway queue: it only ever calls <see cref="Bus"/>.Publish,
/// never Send/Subscribe, so it needs no input queue routing of its own beyond a unique name
/// Rebus insists on regardless.
/// </summary>
internal sealed class UpstreamEventPublisher : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _rabbitMqConnectionString;
    private readonly string _queueName;

    private UpstreamEventPublisher(IHost host, string rabbitMqConnectionString, string queueName)
    {
        _host = host;
        _rabbitMqConnectionString = rabbitMqConnectionString;
        _queueName = queueName;
    }

    public IBus Bus => _host.Services.GetRequiredService<IBus>();

    public static async Task<UpstreamEventPublisher> StartAsync(string rabbitMqConnectionString)
    {
        var queueName = $"gateway-tests-pub.{Guid.NewGuid():N}";
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RebusConfigurationExtensions.ConnectionStringConfigKey] = rabbitMqConnectionString,
        });
        builder.Services.AddBuildingBlocksRebus(builder.Configuration, queueName);
        var host = builder.Build();
        await host.StartAsync();
        return new UpstreamEventPublisher(host, rabbitMqConnectionString, queueName);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        // GL-70: Rebus declares a unique input queue for this host even though it only ever
        // publishes — left undeleted, it accumulates on a reused local broker (see QueueCleanup).
        await QueueCleanup.DeleteAsync(_rabbitMqConnectionString, _queueName);
    }
}
