using System.Collections.Concurrent;
using GiftLists.Contracts.GiftLists;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Records every fire-and-forget GiftLists command <c>GiftListsGrpcService</c> sends (GL-71),
/// so a test can assert on what actually reached the real broker (CONVENTIONS.md "Testing") rather than
/// on <c>GiftListsGrpcService</c>'s return value alone — the RPC returns as soon as the command
/// is accepted onto the bus, before anything downstream (a real GiftLists, not run here) would
/// ever validate it, so this is the only place the wire-vs-JWT distinction the issue cares about
/// (OwnerId/RequesterId never coming from the client) is actually observable from this repo.
///
/// Registered as a DI singleton (<see cref="GiftListsCommandListener"/>) so it survives
/// regardless of what lifetime Rebus.ServiceProvider gives the handler instances that report to
/// it — five thin handlers, one per command, each just forwarding to this in a single line
/// (CONVENTIONS.md "Messaging": a Rebus handler is thin either way).
/// </summary>
public sealed class GiftListsCommandSink
{
    private readonly ConcurrentQueue<CreateGiftList> _createGiftLists = new();
    private readonly ConcurrentQueue<RenameGiftList> _renameGiftLists = new();
    private readonly ConcurrentQueue<DeleteGiftList> _deleteGiftLists = new();
    private readonly ConcurrentQueue<AddGiftItem> _addGiftItems = new();
    private readonly ConcurrentQueue<RemoveGiftItem> _removeGiftItems = new();

    public IReadOnlyCollection<CreateGiftList> CreateGiftLists => _createGiftLists;

    public IReadOnlyCollection<RenameGiftList> RenameGiftLists => _renameGiftLists;

    public IReadOnlyCollection<DeleteGiftList> DeleteGiftLists => _deleteGiftLists;

    public IReadOnlyCollection<AddGiftItem> AddGiftItems => _addGiftItems;

    public IReadOnlyCollection<RemoveGiftItem> RemoveGiftItems => _removeGiftItems;

    public void Record(CreateGiftList message) => _createGiftLists.Enqueue(message);

    public void Record(RenameGiftList message) => _renameGiftLists.Enqueue(message);

    public void Record(DeleteGiftList message) => _deleteGiftLists.Enqueue(message);

    public void Record(AddGiftItem message) => _addGiftItems.Enqueue(message);

    public void Record(RemoveGiftItem message) => _removeGiftItems.Enqueue(message);
}
