using Microsoft.Extensions.DependencyInjection;

namespace LincleLINK.Core.Composition;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers LincleLINK.Core application services and infrastructure adapters.
    /// Populated incrementally by milestone (M1+); App is the only place that calls this.
    /// </summary>
    public static IServiceCollection AddLincleLINKCore(this IServiceCollection services)
    {
        return services;
    }
}
