using Exceptionless.Core;
using Exceptionless.Extensions.DevOps.Modules.SyncWithDevOpsModule;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Exceptionless.Extensions.DevOps;

public class Bootstrapper
{
    public static void RegisterServices(IServiceCollection services, AppOptions appOptions, ILoggerFactory loggerFactory)
    {
        var module = new SyncWithDevOpsModule();
        module.ConfigureServices(services, appOptions, loggerFactory);
        if (module is IStartupFilter m)
        {
            services.AddSingleton(m);
        }
    }
}
