using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Exceptionless.Core;
using Exceptionless.Core.Repositories;
using Exceptionless.Extension.DevOps.Clients;
using Exceptionless.Extension.DevOps.ExtensionMethods;
using Exceptionless.Extension.DevOps.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

[assembly: InternalsVisibleTo("Exceptionless.Extension.DevOps.Tests")]
namespace Exceptionless.Extension.DevOps.Modules.DevOpsSyncModule;

public class DevOpsSyncModule : IModule, IStartupFilter
{
    public void ConfigureServices(IServiceCollection services, AppOptions appOptions, ILoggerFactory loggerFactory)
    {
        services
            .AddOptions<DevOpsOptions>()
            .BindConfiguration("DevOps")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient("devops-odata", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<DevOpsOptions>>().Value;
            client.BaseAddress = new Uri($"https://analytics.dev.azure.com/{options.Organization}/{options.Project}/_odata/v2.0/");
        });
        services.AddHttpClient("devops-services", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<DevOpsOptions>>().Value;
            client.BaseAddress = new Uri($"https://dev.azure.com/{options.Organization}/{options.Project}/_apis/wit/");
        });

        services.AddSingleton<IDevOpsClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var options = sp.GetRequiredService<IOptions<DevOpsOptions>>().Value;

            return new DevOpsClient(httpClientFactory, options.Pat);
        });
        services.AddSingleton<IDevOpsWorkItemService>(sp =>
        {
            var stackRepository = sp.GetRequiredService<IStackRepository>();
            var devOpsClient = sp.GetRequiredService<IDevOpsClient>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var logger = sp.GetRequiredService<ILoggerFactory>();

            return new DevOpsWorkItemService(stackRepository, devOpsClient, timeProvider, logger);
        });
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            app.UseEndpoints(endpoints =>
            {
                var apiGroup = endpoints.MapGroup("/api/devops");
                apiGroup.MapPost("/workitem-status-changed", WorkItemStatusChanged);
            });
        };
    }

    public record DevOpsOptions
    {
        [Required]
        public string Pat { get; set; } = null!;

        [Required]
        public string Organization { get; set; } = null!;

        [Required]
        public string Project { get; set; } = null!;
    }

    internal async Task<IResult> WorkItemStatusChanged(IOptions<DevOpsOptions> options, IStackRepository stackRepository,
        IDevOpsWorkItemService devOpsWorkItemService, [FromBody] JsonObject data)
    {
        var workItemId = data["resource"]?["workItemId"]?.ToString().Trim();
        var newWorkItemStatusStr = data["resource"]?["fields"]?["System.State"]?["newValue"]?.ToString().Trim();
        if (string.IsNullOrEmpty(workItemId) || string.IsNullOrEmpty(newWorkItemStatusStr))
            return Results.BadRequest("Invalid data provided.");

        var newStackStatus = StackStatusExtensions.FromString(newWorkItemStatusStr);
        if (newStackStatus is null)
        {
            var errMsg = string.Format("StackStatus {0: newStatus} could not be found", newWorkItemStatusStr);
            return Results.BadRequest(errMsg);
        }

        var url = $"https://dev.azure.com/{options.Value.Organization}/{options.Value.Project}/_workitems/edit/{workItemId}";

        var allStacks = await stackRepository.GetAllAsync();
        var filteredStacks = allStacks.Documents.Where(s => s.References.Contains(url));

        foreach (var stack in filteredStacks)
        {
            await devOpsWorkItemService.UpdateStackStatus(stack, newStackStatus.Value);
        }
        return Results.NoContent();
    }
}
