using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Exceptionless.Core;
using Exceptionless.Core.Models;
using Exceptionless.Core.Repositories;
using Exceptionless.Extensions.DevOps.Clients;
using Exceptionless.Extensions.DevOps.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Exceptionless.Extensions.DevOps.Modules.SyncWithDevOpsModule;

public class SyncWithDevOpsModule : IModule, IStartupFilter
{
    public void ConfigureServices(IServiceCollection services, AppOptions appOptions, ILoggerFactory loggerFactory)
    {
        services
            .AddOptions<DevOpsOptions>()
            .BindConfiguration("DevOps")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // TODO: Interpolate Orga and Proj from DevOpsOptions
        services.AddHttpClient("devops-odata", client => client.BaseAddress = new Uri("https://analytics.dev.azure.com/bsaalfeld/Exceptionless-Extension/_odata/v2.0/"));
        services.AddHttpClient("devops-services", client => client.BaseAddress = new Uri("https://dev.azure.com/bsaalfeld/Exceptionless-Extension/_apis/wit/"));

        services.AddSingleton<IDevOpsClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var pat = sp.GetRequiredService<IConfiguration>().GetSection("DevOps")["Pat"]!;

            return new DevOpsClient(httpClientFactory, pat);
        });
        services.AddSingleton<IDevOpsWorkItemService>(sp =>
        {
            var stackRepository = sp.GetRequiredService<IStackRepository>();
            var devOpsClient = sp.GetRequiredService<IDevOpsClient>();
            var logger = sp.GetRequiredService<ILoggerFactory>();

            return new DevOpsWorkItemService(stackRepository, devOpsClient, logger);
        });
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapPost("/link-devops-workitem", LinkStackToWorkItem);
                endpoints.MapPost("/unlink-devops-workitem", UnlinkStackFromWorkItem);
                endpoints.MapPost("/stack-status-changed", StackStatusChanged);
                endpoints.MapPost("/workitem-status-changed", WorkItemStatusChanged);
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

    private record LinkStackToWorkItemData
    {
        [Required(ErrorMessage = "The stack_id field is required.")]
        [JsonPropertyName("stack_id")]
        public string StackId { get; set; } = null!;

        [Required(ErrorMessage = "The workitem_id field is required.")]
        [JsonPropertyName("workitem_id")]
        public string WorkItemId { get; set; } = null!;
    }

    private async Task<IResult> LinkStackToWorkItem(
        HttpContext context,
        IDevOpsWorkItemService devOpsWorkItemService,
        [FromBody] LinkStackToWorkItemData data)
    {
        var errors = ValidateModel(data);
        if (errors != null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Results.BadRequest(errors);
        }

        return await devOpsWorkItemService.LinkStackToWorkItem(data.StackId, data.WorkItemId);
    }

    private record UnlinkStackFromWorkItemData
    {
        [Required(ErrorMessage = "The stack_id field is required.")]
        [JsonPropertyName("stack_id")]
        public string StackId { get; set; } = null!;
    }

    private async Task<IResult> UnlinkStackFromWorkItem(
        HttpContext context,
        IDevOpsWorkItemService devOpsWorkItemService,
        [FromBody] UnlinkStackFromWorkItemData data)
    {
        var errors = ValidateModel(data);
        if (errors != null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Results.BadRequest(errors);
        }

        return await devOpsWorkItemService.UnlinkStackFromWorkItem(data.StackId);
    }

    private record StackStatusChangedData
    {
        [Required(ErrorMessage = "The stack_id field is required.")]
        [JsonPropertyName("stack_id")]
        public string StackId { get; set; } = null!;

        [Required(ErrorMessage = "The new_status field is required.")]
        [JsonPropertyName("new_status")]
        public string NewStatus { get; set; } = null!;
    }

    private async Task<IResult> StackStatusChanged(
        HttpContext context,
        IOptions<DevOpsOptions> options,
        IStackRepository stackRepository,
        IDevOpsWorkItemService devOpsWorkItemService,
        [FromBody] StackStatusChangedData data)
    {
        var errors = ValidateModel(data);
        if (errors != null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Results.BadRequest(errors);
        }

        var stack = await stackRepository.GetByIdAsync(data.StackId);
        if (stack is null)
        {
            return Results.NotFound(string.Format("Could not find stack {0: stackId}", data.StackId));
        }

        var url = $"https://dev.azure.com/{options.Value.Organization}/{options.Value.Project}/_workitems/edit";
        var existing = stack.References.FirstOrDefault(r => r.StartsWith(url));
        if (existing == null) return Results.NoContent();

        var workItemId = existing.Split($"{url}/")[1];
        var newWorkItemStatus = data.NewStatus.ToWorkItemStatus();
        if (newWorkItemStatus is null)
        {
            var errMsg = string.Format("WorkItemStatus {0} could not be found", data.NewStatus);
            return Results.BadRequest(errMsg);
        }
        return await devOpsWorkItemService.UpdateWorkItemStatus(workItemId, (WorkItemStatus)newWorkItemStatus);
    }

    private async Task<IResult> WorkItemStatusChanged(
        HttpContext context,
        IOptions<DevOpsOptions> options,
        IStackRepository stackRepository,
        IDevOpsWorkItemService devOpsWorkItemService,
        [FromBody] JsonObject data)
    {
        var errors = ValidateModel(data);
        if (errors != null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Results.BadRequest(errors);
        }

        var workItemId = data["resource"]?["workItemId"]?.ToString().Trim();
        var newStatus = data["resource"]?["fields"]?["System.State"]?["newValue"]?.ToString().Trim();
        if (string.IsNullOrEmpty(workItemId) || string.IsNullOrEmpty(newStatus))
            return Results.BadRequest("Invalid data provided.");

        var url = $"https://dev.azure.com/{options.Value.Organization}/{options.Value.Project}/_workitems/edit/{workItemId}";

        var results = await stackRepository.GetAllAsync();
        var filteredStacks = results.Documents.Where(s => s.References.Contains(url));

        foreach (var stack in filteredStacks)
        {
            var newStackStatus = newStatus.ToStackStatus();
            if (newStackStatus is null)
            {
                var errMsg = string.Format("StackStatus {0: newStatus} could not be found", newStatus);
                return Results.BadRequest(errMsg);
            }
            await devOpsWorkItemService.UpdateStackStatus(stack.Id, (StackStatus)newStackStatus);
        }
        return Results.NoContent();
    }

    private List<string>? ValidateModel(object model)
    {
        var validationContext = new ValidationContext(model);
        var validationResults = new List<ValidationResult>();

        bool isValid = Validator.TryValidateObject(model, validationContext, validationResults, validateAllProperties: true);
        return isValid ? null : validationResults.Select(vr => vr.ErrorMessage).Where(e => e != null).Cast<string>().ToList();
    }
}
