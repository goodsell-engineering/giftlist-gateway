using Gateway.Infrastructure.Platform.Transport;
using Gateway.Infrastructure.Users.Grpc;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using Grpc.Core;

namespace Gateway.IntegrationTests.Users;

/// <summary>
/// End to end through the real grpc-web pipeline (GL-18): a real grpc-web client, over HTTP, into
/// the real ASP.NET Core host, through AuthGrpcService, across the real request/reply bridge
/// (GL-17), against a real Rebus handler standing in for Identity
/// (<see cref="FakeIdentityResponder"/>). Covers Phase 1's exit criteria this service is
/// responsible for: duplicate email and bad credentials reach the browser as a specific,
/// distinguishable gRPC status — never a generic Internal/500 — and a reply timeout degrades to
/// UNAVAILABLE instead of hanging.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class AuthGrpcServiceTests(GatewayFixture gateway)
{
    [Fact]
    public async Task SignUp_ShouldReturnAnAccessToken_WhenTheEmailIsNew()
    {
        // Arrange — the fake echoes both Email and DisplayName into the token
        // (FakeIdentityResponder.FormatAccessToken), so asserting the exact string — not just
        // "non-empty" — is what would actually catch a swapped/misassigned field in either the
        // SignUp command construction or AuthGrpcService.ToResponse (e.g. UserId <-> AccessToken).
        var email = $"{Guid.NewGuid():N}@gateway-tests.local";
        var displayName = "New User";
        var request = new SignUpRequest
        {
            Email = email,
            Password = "correct horse battery staple",
            DisplayName = displayName,
        };

        // Act
        var response = await gateway.AuthClient.SignUpAsync(request).ResponseAsync;

        // Assert
        Assert.True(Guid.TryParse(response.UserId, out _));
        Assert.Equal(FakeIdentityResponder.FormatAccessToken(email, displayName), response.AccessToken);
        Assert.True(response.AccessTokenExpiresAt.ToDateTimeOffset() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_ShouldReturnAnAccessToken_WhenCredentialsAreValid()
    {
        // Arrange — same reasoning as SignUp above: the fake echoes Email and Password into the
        // token, so the exact string is the only assertion that can catch a field swap.
        var email = $"{Guid.NewGuid():N}@gateway-tests.local";
        var password = "correct horse battery staple";
        var request = new LoginRequest
        {
            Email = email,
            Password = password,
        };

        // Act
        var response = await gateway.AuthClient.LoginAsync(request).ResponseAsync;

        // Assert
        Assert.True(Guid.TryParse(response.UserId, out _));
        Assert.Equal(FakeIdentityResponder.FormatAccessToken(email, password), response.AccessToken);
        Assert.True(response.AccessTokenExpiresAt.ToDateTimeOffset() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SignUp_ShouldFailWithAborted_WhenTheEmailIsAlreadyRegistered()
    {
        // Arrange — CONVENTIONS.md "Errors": Conflict -> ABORTED, not a generic failure.
        var request = new SignUpRequest
        {
            Email = FakeIdentityResponder.DuplicateEmail,
            Password = "correct horse battery staple",
            DisplayName = "Duplicate User",
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.AuthClient.SignUpAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.Aborted, rpcException.StatusCode);
        Assert.Equal(
            FakeIdentityResponder.EmailAlreadyRegistered.Code,
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task Login_ShouldFailWithUnauthenticated_WhenCredentialsAreInvalid()
    {
        // Arrange — CONVENTIONS.md "Errors": Unauthenticated -> UNAUTHENTICATED (401), deliberately not
        // PermissionDenied (403) — "log in again" is not "you may not do this". The dedicated cast
        // test (GrpcStatusCodeCastTests.Unauthenticated_And_PermissionDenied_ShouldCastToDifferentGrpcCoreValues)
        // is what actually guards the two never colliding; this only needs the one positive assertion.
        var request = new LoginRequest
        {
            Email = $"{Guid.NewGuid():N}@gateway-tests.local",
            Password = FakeIdentityResponder.InvalidCredentialsPassword,
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.AuthClient.LoginAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.Unauthenticated, rpcException.StatusCode);
        Assert.Equal(
            FakeIdentityResponder.InvalidCredentials.Code,
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task SignUp_ShouldFailWithUnavailable_WhenIdentityNeverReplies()
    {
        // Arrange — the reply-timeout path: FakeIdentityResponder deliberately never replies for
        // this email, so this exercises the bridge's real ~2s (GatewayFixture.ReplyTimeout)
        // budget rather than assuming the behaviour from a unit test elsewhere.
        var request = new SignUpRequest
        {
            Email = FakeIdentityResponder.NeverRepliesEmail,
            Password = "correct horse battery staple",
            DisplayName = "Nobody Home",
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.AuthClient.SignUpAsync(request).ResponseAsync);

        // Assert — the code, not just the status, is what would let GL-19 tell "still processing"
        // (messaging.reply_timeout) apart from a genuine broker outage, both of which arrive as
        // the same bare UNAVAILABLE status.
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.Unavailable, rpcException.StatusCode);
        Assert.Equal(
            "messaging.reply_timeout",
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }
}
