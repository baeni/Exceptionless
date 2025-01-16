using Exceptionless.Core;
using Exceptionless.Extension.DevOps.ExtensionMethods;
using Exceptionless.Extension.DevOps.Modules.DevOpsSyncModule;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Exceptionless.Extension.DevOps;

public class Bootstrapper
{
    public static void RegisterServices(IServiceCollection services, AppOptions appOptions, ILoggerFactory loggerFactory)
    {
        services.AddModules(appOptions, loggerFactory, new DevOpsSyncModule());
    }
}
