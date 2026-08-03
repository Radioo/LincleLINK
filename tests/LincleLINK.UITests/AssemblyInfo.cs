using Xunit;

// One app instance at a time: parallel desktop sessions steal focus from each
// other and make WinAppDriver runs flaky.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
