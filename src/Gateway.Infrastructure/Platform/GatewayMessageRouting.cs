using GiftLists.Contracts.GiftLists;
using Identity.Contracts.Users;
using Rebus.Config;
using Rebus.Routing.TypeBased;

namespace Gateway.Infrastructure.Platform;

/// <summary>
/// Routes the commands the Gateway sends to the service that owns them (it also receives —
/// GL-23's subscription handlers and GL-71's fire-and-forget sends below both use the same
/// underlying bus, just in opposite directions). Passed as <c>AddBuildingBlocksRebus</c>'s
/// <c>configure</c> callback from <c>Gateway.Host</c>'s <c>Program.cs</c>: Host references
/// <see cref="Identity.Contracts.Users.SignUp"/>/<see cref="Login"/> and
/// <see cref="GiftLists.Contracts.GiftLists.CreateGiftList"/> and friends only indirectly, by
/// calling this method, since CONVENTIONS.md "Project reference graph" keeps Host from referencing another service's
/// Contracts directly (only Infrastructure may).
/// </summary>
public static class GatewayMessageRouting
{
    /// <summary>
    /// Identity's Rebus queue name — singular, per ARCHITECTURE.md "Data model" (the service directory is
    /// plural, <c>identity/</c>, but its queue and database are not).
    /// </summary>
    public const string IdentityQueueName = "identity";

    /// <summary>
    /// GiftLists' Rebus queue name — singular, per ARCHITECTURE.md "Data model" (the service directory is
    /// plural, <c>giftlists/</c>, but its queue and database are not — matches
    /// <c>GiftLists.Host</c>'s own <c>AddBuildingBlocksRebus(..., "giftlist")</c> call exactly).
    /// </summary>
    public const string GiftListsQueueName = "giftlist";

    public static RebusConfigurer Configure(RebusConfigurer configurer, IServiceProvider serviceProvider) =>
        configurer.Routing(r => r.TypeBased()
            .Map<SignUp>(IdentityQueueName)
            .Map<Login>(IdentityQueueName)
            .Map<CreateGiftList>(GiftListsQueueName)
            .Map<RenameGiftList>(GiftListsQueueName)
            .Map<DeleteGiftList>(GiftListsQueueName)
            .Map<AddGiftItem>(GiftListsQueueName)
            .Map<RemoveGiftItem>(GiftListsQueueName));
}
