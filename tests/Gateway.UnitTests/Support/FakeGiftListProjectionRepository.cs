using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.RecordGiftItemAdded;
using Gateway.Application.GiftLists.RecordGiftItemRemoved;
using Gateway.Application.GiftLists.RecordGiftListCreated;
using Gateway.Application.GiftLists.RecordGiftListDeleted;
using Gateway.Application.GiftLists.RecordGiftListRenamed;

namespace Gateway.UnitTests.Support;

/// <summary>
/// In-memory stand-in for the gift-list projection's read side, for unit tests of the read use
/// cases. Reads only: the <c>Apply*</c> members throw, because no read interactor may ever call
/// one, and a test that reached them would be testing the wrong thing. (An in-memory repository
/// is banned from the IntegrationTests suite, CONVENTIONS.md "Testing"; a unit test of an
/// interactor's branching is the gap-filling that suite leaves.)
/// </summary>
internal sealed class FakeGiftListProjectionRepository : IGiftListProjectionRepository
{
    private readonly List<GiftListProjection> _lists = [];

    public void Seed(GiftListProjection list) => _lists.Add(list);

    public Task<GiftListProjection?> FindByIdAsync(Guid listId, CancellationToken cancellationToken) =>
        Task.FromResult(_lists.SingleOrDefault(l => l.ListId == listId));

    public Task<IReadOnlyList<GiftListProjection>> FindByOwnerAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GiftListProjection>>(_lists.Where(l => l.OwnerId == ownerId).ToList());

    public Task<GiftListProjection?> FindByShareTokenAsync(string shareToken, CancellationToken cancellationToken) =>
        Task.FromResult(_lists.SingleOrDefault(l => l.ShareToken == shareToken));

    public Task ApplyListCreatedAsync(RecordGiftListCreatedRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Read-side fake.");

    public Task ApplyListRenamedAsync(RecordGiftListRenamedRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Read-side fake.");

    public Task ApplyListDeletedAsync(RecordGiftListDeletedRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Read-side fake.");

    public Task ApplyItemAddedAsync(RecordGiftItemAddedRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Read-side fake.");

    public Task ApplyItemRemovedAsync(RecordGiftItemRemovedRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Read-side fake.");
}
