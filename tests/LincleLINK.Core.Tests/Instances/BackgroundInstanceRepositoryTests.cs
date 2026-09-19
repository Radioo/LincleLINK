using FluentAssertions;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Composition;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Instances;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Instances;

/// <summary>
/// The UI never freezes (CLAUDE.md): SQLite's async calls run on the calling thread,
/// so the repository the app resolves has to move every call to the thread pool.
/// </summary>
public sealed class BackgroundInstanceRepositoryTests
{
    /// <summary>Stands in for the UI thread: work that runs "on the caller" runs with this context current.</summary>
    private sealed class CallerContext : SynchronizationContext;

    private readonly IInstanceRepository _inner = Substitute.For<IInstanceRepository>();
    private readonly System.Collections.Concurrent.ConcurrentBag<bool> _ranOnCaller = [];

    private T Record<T>(T value)
    {
        _ranOnCaller.Add(SynchronizationContext.Current is CallerContext);
        return value;
    }

    [Fact]
    public async Task Every_call_runs_off_the_callers_thread_and_passes_its_result_through()
    {
        var instance = Instance.Create("A", [new InstanceFile("a.bin", "", 1, "AA.bin")], []);
        _inner.GetNamesAsync(Arg.Any<CancellationToken>()).Returns(_ => Record<IReadOnlyList<string>>(["A"]));
        _inner.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Record<IReadOnlyList<Instance>>([instance]));
        _inner.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>()).Returns(_ => Record<IReadOnlyList<string>>(["AA.bin"]));
        _inner.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns(_ => Record<IReadOnlyList<InstanceListEntry>>([]));
        _inner.GetAsync("A", Arg.Any<CancellationToken>()).Returns(_ => Record<Instance?>(instance));
        _inner.ExistsAsync("A", Arg.Any<CancellationToken>()).Returns(_ => Record(true));
        _inner.GetUniqueSizeAsync("A", Arg.Any<CancellationToken>()).Returns(_ => Record(42L));
        _inner.SaveAsync(instance, Arg.Any<CancellationToken>()).Returns(_ => Record(Task.CompletedTask));
        _inner.DeleteAsync("A", Arg.Any<CancellationToken>()).Returns(_ => Record(true));
        _inner.BulkInsertAsync(Arg.Any<IReadOnlyList<Instance>>(), Arg.Any<CancellationToken>()).Returns(_ => Record(Task.CompletedTask));
        _inner.SetCustomLogoAsync("A", "logo", Arg.Any<CancellationToken>()).Returns(_ => Record(Task.CompletedTask));
        var repository = new BackgroundInstanceRepository(_inner);
        var ct = TestContext.Current.CancellationToken;

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CallerContext());
        try
        {
            // Started on the "UI thread"; awaited below, outside the context.
            var calls = new Task[]
            {
                repository.GetNamesAsync(ct),
                repository.GetAllAsync(ct),
                repository.GetAllHashedFileNamesAsync(ct),
                repository.GetSummariesAsync(ct),
                repository.GetAsync("A", ct),
                repository.ExistsAsync("A", ct),
                repository.GetUniqueSizeAsync("A", ct),
                repository.SaveAsync(instance, ct),
                repository.DeleteAsync("A", ct),
                repository.BulkInsertAsync([instance], ct),
                repository.SetCustomLogoAsync("A", "logo", ct),
            };
            SynchronizationContext.SetSynchronizationContext(previous);
            await Task.WhenAll(calls);

            (await (Task<Instance?>)calls[4]).Should().BeSameAs(instance);
            (await (Task<long>)calls[6]).Should().Be(42);
            (await (Task<bool>)calls[8]).Should().BeTrue();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        _ranOnCaller.Should().HaveCount(11).And.OnlyContain(onCaller => !onCaller);
    }

    [Fact]
    public void The_app_resolves_the_repository_behind_the_background_wrapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLincleLINKCore();

        var descriptor = services.Last(d => d.ServiceType == typeof(IInstanceRepository));

        // Resolving would open the database; the registration itself is what matters:
        // a factory, not the SQLite type registered directly.
        descriptor.ImplementationType.Should().BeNull("the SQLite repository must not be what callers get");
        descriptor.ImplementationFactory.Should().NotBeNull();
    }
}
