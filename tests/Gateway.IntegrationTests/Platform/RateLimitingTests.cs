using Gateway.Infrastructure.Users.Grpc;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using Grpc.Core;

namespace Gateway.IntegrationTests.Platform;

/// <summary>
/// GL-44: the two no-JWT edges carry a per-IP rate limit
/// (<c>GatewayInfrastructureServiceCollectionExtensions.AddRateLimiting</c>,
/// <c>GatewayRateLimitPolicies</c>). Nothing else in the suite exercises them — every other test
/// gets its own client address from <see cref="TestClientAddressStartupFilter"/> precisely so it
/// never does — so this is the one place the limiter is proved to fire at all, and the one place
/// that pins what a rejected caller actually receives.
///
/// Why that second half matters: the limiter rejects with a plain HTTP 429, below grpc-web's own
/// status framing (<c>AddRateLimiting</c>'s remarks explain the layering), so what the SPA sees is
/// decided by the transport's HTTP-to-grpc status mapping rather than by anything this repo
/// writes. Pinning it here is what tells giftlist-web's error mappers which code to expect.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class RateLimitingTests(GatewayFixture gateway)
{
    [Fact]
    public async Task AuthEdge_ShouldRejectTheCallOverTheLimit_WhenOneAddressExceedsTheAuthPolicy()
    {
        // Arrange — one address for every call, which is the whole point: these six calls share a
        // partition only because they carry the same header. The limit is 5/minute, and the
        // window is a minute, so no test can be written that waits it out; the sixth call is the
        // assertion.
        var headers = ClientAddress("198.51.100.10");
        var exceptions = new List<Exception?>();

        // Act
        for (var call = 1; call <= 6; call++)
        {
            var request = new LoginRequest
            {
                Email = $"{Guid.NewGuid():N}@gateway-tests.local",
                Password = "correct horse battery staple",
            };
            exceptions.Add(await Record.ExceptionAsync(
                () => gateway.AuthClient.LoginAsync(request, headers).ResponseAsync));
        }

        // Assert — the first five succeed (asserted, so a limiter that rejected everything could
        // not pass this), the sixth is rejected, and it is rejected as Unavailable: HTTP 429 has
        // no grpc status of its own, and the transport maps it there. giftlist-web's authErrors.ts
        // already renders Unavailable as "temporarily unavailable, try again shortly", which is
        // the honest thing to tell someone who has just been throttled.
        Assert.All(exceptions.Take(5), Assert.Null);
        var rejected = Assert.IsType<RpcException>(exceptions[5]);
        Assert.Equal(StatusCode.Unavailable, rejected.StatusCode);
    }

    [Fact]
    public async Task AuthEdge_ShouldNotCountOtherCallersAgainstOneAddress_WhenTheyDiffer()
    {
        // Arrange — the same six calls as above, each from its own address. This is what proves
        // the limiter PARTITIONS rather than counting globally; without it, a partition key bug
        // (a constant, or the "unknown" fallback this suite used to run on) would satisfy the test
        // above perfectly.
        var exceptions = new List<Exception?>();

        // Act
        for (var call = 1; call <= 6; call++)
        {
            var request = new LoginRequest
            {
                Email = $"{Guid.NewGuid():N}@gateway-tests.local",
                Password = "correct horse battery staple",
            };
            exceptions.Add(await Record.ExceptionAsync(
                () => gateway.AuthClient.LoginAsync(request, ClientAddress($"198.51.100.{100 + call}")).ResponseAsync));
        }

        // Assert
        Assert.All(exceptions, Assert.Null);
    }

    private static Metadata ClientAddress(string address) =>
        new() { { TestClientAddressStartupFilter.HeaderName, address } };
}
