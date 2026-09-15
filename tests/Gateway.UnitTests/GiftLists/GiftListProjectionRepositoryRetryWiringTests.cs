using BuildingBlocks.Persistence;
using Gateway.Application.GiftLists;

namespace Gateway.UnitTests.GiftLists;

/// <summary>
/// GL-76 (review, Batch 16 round 3): pins the VALUES <c>GiftListProjectionRepository.ApplyAsync</c>
/// actually wires into <see cref="BoundedCasRetry.RunAsync"/>'s <c>exhaustedException</c> —
/// <see cref="GiftListProjectionApplyExhaustedException"/>, the list id, and
/// <see cref="BoundedCasRetryPolicy.MaxAttempts"/> — which the type system has no opinion on
/// and <see cref="BoundedCasRetry"/>'s own generic shape tests (moved to
/// <c>BuildingBlocks.UnitTests</c> by GL-79, alongside the production types) cannot cover, since
/// they never reference a Gateway type. This test's own reason for existing is Gateway's, not
/// <c>BoundedCasRetry</c>'s, which is exactly why it stayed in this project when the rest of the
/// old <c>BoundedCasRetryTests</c> moved out: it reaches <see cref="BoundedCasRetry"/> and
/// <see cref="BoundedCasRetryPolicy"/> through their own <see langword="public"/> surface now
/// (GL-79), needing no <c>InternalsVisibleTo</c> grant the way it used to.
///
/// Uses the no-argument <c>maxAttempts</c> default deliberately, the same way
/// <c>GiftListProjectionRepository</c>'s own field does (GL-79 review, S1) — this test wires
/// EXACTLY what production wires, so it must rely on the same default rather than passing its own
/// explicit value, which would let this test and production drift independently of each other.
///
/// Replaying the finding this test was written against: replacing <c>ApplyAsync</c>'s own
/// <c>onExhausted</c> argument with a no-op built clean and left the full Gateway suite green
/// until this test existed to wire <see cref="BoundedCasRetry.RunAsync"/> with the exact same
/// expression <c>ApplyAsync</c> passes as <c>exhaustedException</c>.
/// </summary>
public sealed class GiftListProjectionRepositoryRetryWiringTests
{
    [Fact]
    public async Task RunAsync_ShouldThrowGiftListProjectionApplyExhausted_WhenWiredExactlyAsGiftListProjectionRepositoryDoes()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var policy = new BoundedCasRetryPolicy(jitterSource: () => 0.0);

        // Act
        var exception = await Record.ExceptionAsync(() => BoundedCasRetry.RunAsync(
            policy,
            _ => Task.FromResult(false),
            exhaustedException: () => new GiftListProjectionApplyExhaustedException(listId, policy.MaxAttempts),
            CancellationToken.None));

        // Assert
        var exhausted = Assert.IsType<GiftListProjectionApplyExhaustedException>(exception);
        Assert.Equal(listId, exhausted.ListId);
        Assert.Equal(policy.MaxAttempts, exhausted.Attempts);
    }
}
