using Exceptionless.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Exceptionless.Extension.DevOps.Modules;

public interface IModule
{
    void ConfigureServices(IServiceCollection services, AppOptions appOptions, ILoggerFactory loggerFactory);
}
