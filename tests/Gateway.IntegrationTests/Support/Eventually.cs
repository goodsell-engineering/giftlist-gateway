namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Polls an async operation until a predicate is satisfied or a timeout elapses — needed because
/// the projection this suite tests is built asynchronously, off a Rebus handler on the real
/// broker (CONVENTIONS.md "Testing"), not synchronously inside the request that triggered it.
/// </summary>
internal static class Eventually
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<T> Async<T>(Func<Task<T>> poll, Func<T, bool> isReady, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = await poll();

        while (!isReady(last))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met within {timeout}.");
            }

            await Task.Delay(PollInterval);
            last = await poll();
        }

        return last;
    }
}
