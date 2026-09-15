using BuildingBlocks.Messaging;
using Gateway.Infrastructure.Platform;
using GiftLists.Contracts.GiftLists;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Handlers;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Stands in for GiftLists on the real broker (CONVENTIONS.md "Testing": "other services are not run —
/// assert on contracts published to the real broker") — a bare Rebus host bound to GiftLists' own
/// queue name (<see cref="QueueName"/>, matching <c>GatewayMessageRouting.GiftListsQueueName</c>
/// exactly, so <c>GiftListsGrpcService</c>'s <c>bus.Send</c> is routed here the same way it would
/// be to the real service) whose five handlers do nothing but record onto
/// <see cref="GiftListsCommandSink"/> — never <c>bus.Reply</c>, because GiftLists' own handlers
/// never do either (ARCHITECTURE.md "Command → event flow").
/// </summary>
internal sealed class GiftListsCommandListener : IAsyncDisposable
{
    /// <summary>Reuses GatewayMessageRouting's own constant rather than re-typing the literal (GL-71 Batch 14 review, N1) — a wrong production value now fails fast by name instead of as a 15-second Eventually timeout.</summary>
    public const string QueueName = GatewayMessageRouting.GiftListsQueueName;

    private readonly IHost _host;

    private GiftListsCommandListener(IHost host)
    {
        _host = host;
    }

    public GiftListsCommandSink Sink => _host.Services.GetRequiredService<GiftListsCommandSink>();

    public static async Task<GiftListsCommandListener> StartAsync(string rabbitMqConnectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RebusConfigurationExtensions.ConnectionStringConfigKey] = rabbitMqConnectionString,
        });
        builder.Services.AddSingleton<GiftListsCommandSink>();
        builder.Services.AddBuildingBlocksRebus(builder.Configuration, QueueName);
        builder.Services.AddRebusHandler<RecordCreateGiftListHandler>();
        builder.Services.AddRebusHandler<RecordRenameGiftListHandler>();
        builder.Services.AddRebusHandler<RecordDeleteGiftListHandler>();
        builder.Services.AddRebusHandler<RecordAddGiftItemHandler>();
        builder.Services.AddRebusHandler<RecordRemoveGiftItemHandler>();

        var host = builder.Build();
        await host.StartAsync();
        return new GiftListsCommandListener(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private sealed class RecordCreateGiftListHandler(GiftListsCommandSink sink) : IHandleMessages<CreateGiftList>
    {
        public Task Handle(CreateGiftList message)
        {
            sink.Record(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordRenameGiftListHandler(GiftListsCommandSink sink) : IHandleMessages<RenameGiftList>
    {
        public Task Handle(RenameGiftList message)
        {
            sink.Record(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordDeleteGiftListHandler(GiftListsCommandSink sink) : IHandleMessages<DeleteGiftList>
    {
        public Task Handle(DeleteGiftList message)
        {
            sink.Record(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordAddGiftItemHandler(GiftListsCommandSink sink) : IHandleMessages<AddGiftItem>
    {
        public Task Handle(AddGiftItem message)
        {
            sink.Record(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordRemoveGiftItemHandler(GiftListsCommandSink sink) : IHandleMessages<RemoveGiftItem>
    {
        public Task Handle(RemoveGiftItem message)
        {
            sink.Record(message);
            return Task.CompletedTask;
        }
    }
}
