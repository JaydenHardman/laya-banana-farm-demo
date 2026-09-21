namespace BananaFarm.Farm.Tests;

/// <summary>
/// The running test's cancellation token.
/// </summary>
/// <remarks>
/// Passing this rather than <c>default</c> means a call that hangs fails the test promptly
/// instead of stalling the whole run.
/// </remarks>
internal static class Cancel
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;
}
