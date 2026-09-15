using BuildingBlocks.Transport;

namespace ArchitectureTests;

/// <summary>
/// Pins the one cast <c>BuildingBlocks</c> cannot test itself — <see cref="GrpcStatusCode"/>'s
/// remarks explain why (it may not reference <c>Grpc.Core</c>) and name GL-18 as the owner of
/// this test. <c>ErrorToRpcExceptionMapper</c> (Gateway.Infrastructure) relies on
/// <c>(Grpc.Core.StatusCode)(int)someGrpcStatusCode</c> being a value-preserving cast for every
/// member <c>ErrorKindTransportMapping.ToGrpcStatus()</c> can produce; if a future version of
/// either enum ever renumbers a member, this fails here instead of silently mis-mapping
/// "duplicate email" or "bad credentials" into the wrong wire status.
///
/// Not in the shared Architecture/ sync set (see <c>ArchitectureTestSyncTests</c>) — like
/// <c>BuildingBlocks.UnitTests/Architecture/ErrorKindTransportMappingTests.cs</c>, this only
/// makes sense where <c>Grpc.Core</c> is actually reachable, i.e. the Gateway.
/// </summary>
public class GrpcStatusCodeCastTests
{
    /// <summary>
    /// Every member <see cref="GrpcStatusCode"/> actually declares, checked by name against
    /// <see cref="Grpc.Core.StatusCode"/> — pins the value-preserving cast independently of
    /// which subset <c>ErrorKindTransportMapping.ToGrpcStatus()</c> currently uses, so a member
    /// added to <see cref="GrpcStatusCode"/> without ever being wired into that mapping is still
    /// covered.
    /// </summary>
    [Theory]
    [InlineData(GrpcStatusCode.Ok, Grpc.Core.StatusCode.OK)]
    [InlineData(GrpcStatusCode.Cancelled, Grpc.Core.StatusCode.Cancelled)]
    [InlineData(GrpcStatusCode.Unknown, Grpc.Core.StatusCode.Unknown)]
    [InlineData(GrpcStatusCode.InvalidArgument, Grpc.Core.StatusCode.InvalidArgument)]
    [InlineData(GrpcStatusCode.DeadlineExceeded, Grpc.Core.StatusCode.DeadlineExceeded)]
    [InlineData(GrpcStatusCode.NotFound, Grpc.Core.StatusCode.NotFound)]
    [InlineData(GrpcStatusCode.AlreadyExists, Grpc.Core.StatusCode.AlreadyExists)]
    [InlineData(GrpcStatusCode.PermissionDenied, Grpc.Core.StatusCode.PermissionDenied)]
    [InlineData(GrpcStatusCode.ResourceExhausted, Grpc.Core.StatusCode.ResourceExhausted)]
    [InlineData(GrpcStatusCode.FailedPrecondition, Grpc.Core.StatusCode.FailedPrecondition)]
    [InlineData(GrpcStatusCode.Aborted, Grpc.Core.StatusCode.Aborted)]
    [InlineData(GrpcStatusCode.OutOfRange, Grpc.Core.StatusCode.OutOfRange)]
    [InlineData(GrpcStatusCode.Unimplemented, Grpc.Core.StatusCode.Unimplemented)]
    [InlineData(GrpcStatusCode.Internal, Grpc.Core.StatusCode.Internal)]
    [InlineData(GrpcStatusCode.Unavailable, Grpc.Core.StatusCode.Unavailable)]
    [InlineData(GrpcStatusCode.DataLoss, Grpc.Core.StatusCode.DataLoss)]
    [InlineData(GrpcStatusCode.Unauthenticated, Grpc.Core.StatusCode.Unauthenticated)]
    public void GrpcStatusCode_ShouldCastToTheSameNumericValue_AsGrpcCoreStatusCode(
        GrpcStatusCode ours, Grpc.Core.StatusCode theirs)
    {
        // Arrange — parameters above are the pinned pairing, named independently on each side
        // (BuildingBlocks.Transport.GrpcStatusCode.Ok / Grpc.Core.StatusCode.OK spell the same
        // value differently — see GrpcStatusCode's remarks — so this test cannot pass by
        // comparing names).

        // Act
        var castToGrpcCore = (Grpc.Core.StatusCode)(int)ours;

        // Assert
        Assert.Equal(theirs, castToGrpcCore);
        Assert.Equal((int)theirs, (int)ours);
    }

    /// <summary>
    /// GL-18's task text calls this out by name: a failed login must reach the browser as
    /// UNAUTHENTICATED (401), never PERMISSION_DENIED (403) — "log in again" and "you may not do
    /// this" are deliberately distinct outcomes (CONVENTIONS.md "Errors"). The full pairing above
    /// already proves each casts correctly on its own; this is the one comparison that would
    /// catch the two being swapped for each other, which same-value pinning cannot.
    /// </summary>
    [Fact]
    public void Unauthenticated_And_PermissionDenied_ShouldCastToDifferentGrpcCoreValues()
    {
        // Arrange — none

        // Act
        var unauthenticated = (Grpc.Core.StatusCode)(int)GrpcStatusCode.Unauthenticated;
        var permissionDenied = (Grpc.Core.StatusCode)(int)GrpcStatusCode.PermissionDenied;

        // Assert
        Assert.NotEqual(permissionDenied, unauthenticated);
        Assert.Equal(Grpc.Core.StatusCode.Unauthenticated, unauthenticated);
        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, permissionDenied);
    }
}
