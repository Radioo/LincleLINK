using Xunit;

namespace LincleLINK.App.Tests.TestHelpers;

/// <summary>
/// Bounded poll for a fire-and-forget view-model side effect. The view models
/// kick off analyses and refreshes with <c>_ = SomeAsync()</c>, so a test cannot
/// await them directly; a fixed <see cref="Thread.Sleep"/> races the work on a
/// loaded CI machine, while this loop keeps polling until the condition holds.
/// </summary>
public static class AsyncWaits
{
    public static async Task AwaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
