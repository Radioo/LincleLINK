using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application;
using LincleLINK.Core.Composition;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Composition;

/// <summary>
/// Headless DI composition test: registers <c>AddLincleLINKCore</c> with the two
/// externally-provided ports (<see cref="IAppPaths"/>, <see cref="ISettingsStore"/>)
/// and resolves every registered service, so wiring errors surface at test time
/// without an Avalonia window.
/// </summary>
public sealed class CompositionTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AddLincleLINKCore_ResolvesEveryRegisteredService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths>(new AppPaths(_temp.Root));
        services.AddSingleton<ISettingsStore>(Substitute.For<ISettingsStore>());
        services.AddSingleton<IDialogService>(Substitute.For<IDialogService>());
        // Services now take ILogger<T>; register the logging infrastructure so every
        // Core service resolves (issue #17 D3).
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddLincleLINKCore();

        using var provider = services.BuildServiceProvider();
        var descriptors = services
            .Where(d => d.ServiceType != typeof(IServiceCollection))
            // Open generics (ILogger<>, IOptions<>, ...) register factories, not
            // directly resolvable types; only closed descriptors must resolve.
            .Where(d => !d.ServiceType.ContainsGenericParameters)
            .ToList();

        descriptors.Should().NotBeEmpty();
        foreach (var descriptor in descriptors)
        {
            var instance = provider.GetService(descriptor.ServiceType);
            instance.Should().NotBeNull($"{descriptor.ServiceType} should resolve");
        }
    }
}
