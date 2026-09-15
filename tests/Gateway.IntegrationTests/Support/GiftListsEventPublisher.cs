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
/// Stands in for GiftLists on the real broker (CONVENTIONS.md "Testing": "other services are not run —
/// assert on contracts published to the real broker") — publishes the exact
/// <c>GiftLists.Contracts.GiftLists.Events.*V1</c> types GiftLists' own
/// <c>GiftListEventPublisher</c> does. A bare Rebus host with no handlers of its own, on a
/// throwaway queue: it only ever calls <see cref="Bus"/>.Publish, never Send/Subscribe, so it
/// needs no input queue routing of its own beyond a unique name Rebus insists on regardless.
/// </summary>
internal sealed class GiftListsEventPublisher : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _rabbitMqConnectionString;
    private readonly string _queueName;

    private GiftListsEventPublisher(IHost host, string rabbitMqConnectionString, string queueName)
    {
        _host = host;
        _rabbitMqConnectionString = rabbitMqConnectionString;
        _queueName = queueName;
    }

    public IBus Bus => _host.Services.GetRequiredService<IBus>();

    public static async Task<GiftListsEventPublisher> StartAsync(string rabbitMqConnectionString)
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
        return new GiftListsEventPublisher(host, rabbitMqConnectionString, queueName);
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
