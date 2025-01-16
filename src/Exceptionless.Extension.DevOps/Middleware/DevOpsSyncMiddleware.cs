using System.Text.Json;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Models;
using Exceptionless.Core.Repositories;
using Exceptionless.Extension.DevOps.Exceptions;
using Exceptionless.Extension.DevOps.ExtensionMethods;
using Exceptionless.Extension.DevOps.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Request.Body.Peeker;
using static Exceptionless.Extension.DevOps.Modules.DevOpsSyncModule.DevOpsSyncModule;

namespace Exceptionless.Extension.DevOps.Middleware;

public class DevOpsSyncMiddleware(RequestDelegate next, IStackRepository stackRepository, IDevOpsWorkItemService devOpsWorkItemService, IOptions<DevOpsOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var pathVal = context.Request.Path.Value;

        if (pathVal == null || !pathVal.StartsWith("/api/v2/stacks") || context.Request.Method != HttpMethods.Post)
        {
            await next(context);
            return;
        }

        if (pathVal.Contains("change-status"))
        {
            await HandleChangeStatus(context, options.Value);
            return;
        }
        else if (pathVal.Contains("mark-fixed"))
        {
            await HandleChangeStatus(context, options.Value, StackStatus.Fixed);
            return;
        }
        else if (pathVal.Contains("mark-snoozed"))
        {
            await HandleChangeStatus(context, options.Value, StackStatus.Snoozed);
            return;
        }
        else if (pathVal.Contains("add-link"))
        {
            await HandleAddLink(context, options.Value);
            return;
        }
        else
        {
            await next(context);
        }
    }

    private async Task HandleChangeStatus(HttpContext context, DevOpsOptions options, StackStatus? newStackStatus = null)
    {
        var routeData = context.GetRouteData();

        var stackIds = routeData.Values["ids"] as string;
        if (stackIds is null || stackIds.Length == 0)
            return;

        if (newStackStatus is null)
        {
            var newStackStatusStr = context.Request.Query["status"].ToString();
            if (newStackStatusStr is null)
                return;

            newStackStatus = StackStatusExtensions.FromString(newStackStatusStr);
        }

        var newWorkItemStatus = newStackStatus?.ToWorkItemStatus();
        if (newWorkItemStatus is null)
            return;

        var baseUrl = $"https://dev.azure.com/{options.Organization}/{options.Project}/_workitems/edit/";
        foreach (var stackId in stackIds.Split(','))
        {
            var stack = await stackRepository.GetByIdAsync(stackId);
            if (stack is null)
                continue;

            var existing = stack.References.FirstOrDefault(r => r.StartsWith(baseUrl));
            if (existing is null)
                continue;

            var workItemId = existing.Split(baseUrl)[1];
            if (workItemId is null)
                continue;

            try
            {
                await devOpsWorkItemService.UpdateWorkItemStatus(workItemId, newWorkItemStatus.Value);
            } catch (UpdateWorkItemStatusException ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(ex.Message);
                return;
            }
        }

        await next(context);
    }

    private async Task HandleAddLink(HttpContext context, DevOpsOptions options)
    {
        var routeData = context.GetRouteData();

        var stackId = routeData.Values["id"] as string;
        if (stackId is null)
            return;

        var requestBody = context.Request.PeekBody();
        if (string.IsNullOrWhiteSpace(requestBody))
            return;

        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
        if (payload == null || !payload.TryGetValue("value", out var url) || string.IsNullOrWhiteSpace(url))
            return;

        var baseUrl = $"https://dev.azure.com/{options.Organization}/{options.Project}/_workitems/edit/";
        if (url.StartsWith(baseUrl))
        {
            var workItemId = url.Split(baseUrl)[1];
            if (workItemId.IsNullOrEmpty())
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Failed to parse work item id");
                return;
            }

            WorkItemStatus workItemStatus;
            try
            {
                workItemStatus = await devOpsWorkItemService.GetWorkItemStatus(workItemId);
            }
            catch (GetWorkItemStatusException ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(ex.Message);
                return;
            }

            var stackStatus = workItemStatus.ToStackStatus();

            var stack = await stackRepository.GetByIdAsync(stackId);
            if (stack is null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Failed to find stack to update");
                return;
            }

            await devOpsWorkItemService.UpdateStackStatus(stack, stackStatus);
        }

        await next(context);
    }
}
