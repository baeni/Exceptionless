using Exceptionless.Core;
using Exceptionless.Extension.DevOps.Modules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Exceptionless.Extension.DevOps.ExtensionMethods;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddModules(this IServiceCollection services,
        AppOptions appOptions, ILoggerFactory loggerFactory, params IModule[] modules)
    {
        foreach (var module in modules)
        {
            module.ConfigureServices(services, appOptions, loggerFactory);
            if (module is IStartupFilter m)
            {
                services.AddSingleton(m);
            }
        }

        return services;
    }
}
