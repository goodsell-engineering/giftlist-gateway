using Gateway.Infrastructure.Reservations.Grpc;
using Google.Protobuf;

namespace Gateway.UnitTests.Reservations;

/// <summary>
/// GL-37: the whole point of this RPC's shape — a share token and an item id, and nothing else —
/// checked mechanically over the actual generated <see cref="IMessage"/> type Grpc.Tools produces
/// from <c>reservations.proto</c>, rather than by eyeballing the source text. Mirrors
/// <c>GiftLists.UnitTests.GiftLists.GiftListsProtoContractTests</c>'s own reflection-over-generated-types
/// approach exactly, pinning the opposite fact: that specific RPC bans an owner/requester field on
/// every message; this one bans a list id on the request, because the whole guarantee here is
/// that a caller can only ever reserve against the one list its own share link resolves to
/// (<c>ReservationsGrpcService</c>'s own remarks), never an arbitrary id it happens to guess or
/// borrow from another surface.
/// </summary>
public class ReservationsProtoContractTests
{
    [Fact]
    public void ReserveGiftRequest_ShouldExposeExactlyShareTokenAndItemId_AndNoListIdField()
    {
        // Arrange — none

        // Act
        var propertyNames = typeof(ReserveGiftRequest)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => name != "Parser" && name != "Descriptor")
            .ToList();

        // Assert — the exact set, not merely "does not contain a list id": a field this test does
        // not know about yet (added by someone editing the .proto without reading this test) fails
        // just as loudly as a reintroduced list id would.
        Assert.Equal(["ItemId", "ShareToken"], propertyNames.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void ReserveGiftResponse_ShouldExposeExactlyReleaseSecret_AndNoReservedAtField()
    {
        // Arrange — none. ReservedAt is deliberately never returned (GL-38 drops it at the ACL;
        // this RPC keeps that rule rather than reopening a second path for it).

        // Act
        var propertyNames = typeof(ReserveGiftResponse)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => name != "Parser" && name != "Descriptor")
            .ToList();

        // Assert
        Assert.Equal(["ReleaseSecret"], propertyNames);
    }

    [Fact]
    public void NoGeneratedReservationsMessage_ShouldExposeAListIdField()
    {
        // Arrange
        var messageTypes = typeof(ReservationsService)
            .Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(ReservationsService).Namespace)
            .Where(t => typeof(IMessage).IsAssignableFrom(t))
            .ToList();
        var offenders = new List<string>();

        // Act
        foreach (var type in messageTypes)
        {
            var bannedProperties = type
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.Name.Contains("list", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name);

            offenders.AddRange(bannedProperties.Select(propertyName => $"{type.Name}.{propertyName}"));
        }

        // Assert — guards the two tests above against a namespace/assembly change silently
        // finding zero types and passing vacuously, same as GiftListsProtoContractTests' own
        // GeneratedGiftListsMessageTypes_ShouldBeNonEmpty.
        Assert.True(messageTypes.Count > 0,
            "No generated Google.Protobuf.IMessage types were found in Gateway.Infrastructure.Reservations.Grpc — " +
            "the reflection scan above is running against an empty set and proves nothing.");
        Assert.True(offenders.Count == 0,
            "reservations.proto (or a hand-written partial) added a list-id-shaped field the wire " +
            $"should never carry: {string.Join(", ", offenders)}. A share token is the whole " +
            "credential — resolve it to a list id inside ReservationsGrpcService, never accept one on the wire.");
    }
}
