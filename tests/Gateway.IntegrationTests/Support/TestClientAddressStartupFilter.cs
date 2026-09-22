using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// GL-44: gives every request a client IP address, because <c>TestServer</c> gives it none.
///
/// The rate limiters partition on <c>Connection.RemoteIpAddress</c>
/// (<c>GatewayInfrastructureServiceCollectionExtensions.ClientIp</c>), which is always null under
/// <c>WebApplicationFactory</c> — there is no TCP peer. Without this filter every request in the
/// whole suite therefore shares ONE partition (the "unknown" fallback), and the suite passes only
/// because it happens to sit just under the auth policy's 5/minute ceiling: the sixth auth call
/// anyone adds would fail with an <c>Unavailable</c> that names no cause, minutes-dependent and
/// maddening to diagnose.
///
/// So: a request carrying <see cref="HeaderName"/> gets that address — which is how
/// <c>RateLimitingTests</c> deliberately puts several calls in ONE partition — and a request
/// without it gets a fresh unique address, so ordinary tests can never contend with each other or
/// with a rate-limit test, however many of them there are.
///
/// Registered as an <see cref="IStartupFilter"/> rather than by rebuilding the pipeline, so it
/// runs ahead of the real <c>UseRateLimiter()</c> without restating a single line of Program.cs's
/// own middleware order.
/// </summary>
public sealed class TestClientAddressStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Client-Ip";

    private int _nextAddress;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.RemoteIpAddress = context.Request.Headers.TryGetValue(HeaderName, out var requested)
                    && IPAddress.TryParse(requested.ToString(), out var address)
                        ? address
                        : UniqueAddress();
                await continuation();
            });
            next(app);
        };

    /// <summary>
    /// 198.18.0.0/15 — the benchmarking range (RFC 2544), which is neither routable nor anything a
    /// reader could mistake for a real caller. The counter is per-host, and a suite that made 131k
    /// unheadered requests would wrap into re-using one; it would take a suite two orders of
    /// magnitude larger than this one for that to matter.
    /// </summary>
    private IPAddress UniqueAddress()
    {
        var ordinal = Interlocked.Increment(ref _nextAddress);
        return new IPAddress([198, 18, (byte)(ordinal >> 8), (byte)ordinal]);
    }
}
