using System.Reflection;
using Gateway.Infrastructure.GiftLists.Grpc;
using Google.Protobuf;

namespace Gateway.UnitTests.GiftLists;

/// <summary>
/// The security rule GL-71 carries: no message in <c>giftlists.proto</c> may expose an
/// owner/requester field, because <c>GiftListsGrpcService</c> must read that identity off the
/// caller's JWT (<c>ServerCallContextExtensions.RequireUserId</c>), never off the wire — there is
/// no downstream ownership check in GiftLists' own handlers to catch a mistake here, unlike
/// GL-23's read path. This is checked mechanically, over the actual generated
/// <see cref="IMessage"/> types Grpc.Tools produces from the <c>.proto</c> file, rather than by
/// eyeballing the source text — a future field addition anywhere in the assembly the proto
/// generates into is caught the same way a renamed contract type is caught elsewhere in this
/// codebase.
/// </summary>
public class GiftListsProtoContractTests
{
    private static readonly string[] BannedFieldNameFragments = { "owner", "requester" };

    [Fact]
    public void NoGeneratedGiftListsMessage_ShouldExposeAnOwnerOrRequesterField()
    {
        // Arrange
        var messageTypes = GetGeneratedMessageTypes();
        var offenders = new List<string>();

        // Act
        foreach (var type in messageTypes)
        {
            var bannedProperties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => BannedFieldNameFragments.Any(fragment =>
                    p.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Name);

            offenders.AddRange(bannedProperties.Select(propertyName => $"{type.Name}.{propertyName}"));
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "giftlists.proto (or a hand-written partial) added an owner/requester field the " +
            $"wire should never carry: {string.Join(", ", offenders)}. " +
            "OwnerId/RequesterId must come from the JWT via ServerCallContextExtensions.RequireUserId, never a client-supplied field.");
    }

    [Fact]
    public void GeneratedGiftListsMessageTypes_ShouldBeNonEmpty()
    {
        // Arrange — none

        // Act
        var count = GetGeneratedMessageTypes().Count;

        // Assert — guards the test above against a namespace/assembly change silently making it
        // scan zero types and pass vacuously.
        Assert.True(count > 0,
            "No generated Google.Protobuf.IMessage types were found in Gateway.Infrastructure.GiftLists.Grpc — " +
            "the reflection scan above is running against an empty set and proves nothing.");
    }

    private static List<Type> GetGeneratedMessageTypes() => typeof(GiftListsService)
        .Assembly
        .GetTypes()
        .Where(t => t.Namespace == typeof(GiftListsService).Namespace)
        .Where(t => typeof(IMessage).IsAssignableFrom(t))
        .ToList();
}
