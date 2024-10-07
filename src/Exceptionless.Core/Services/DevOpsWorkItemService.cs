using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Exceptionless.Core.Repositories;
using Microsoft.AspNetCore.Mvc;
using Exceptionless.Core.Extensions;

using Exceptionless.Core.Models;

namespace Exceptionless.Core.Services;

public class DevOpsWorkItemService : IDevOpsWorkItemService
{
    private readonly IStackRepository _stackRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _pat;
    private readonly ILogger<DevOpsWorkItemService> _logger;

    public DevOpsWorkItemService(IStackRepository stackRepository, IHttpClientFactory httpClientFactory, string pat, ILoggerFactory loggerFactory)
    {
        _stackRepository = stackRepository;
        _httpClientFactory = httpClientFactory;
        _pat = pat;
        _logger = loggerFactory.CreateLogger<DevOpsWorkItemService>();
    }

    public async Task<IActionResult> LinkWorkItemToStack(string stackId, string workItemId, TimeProvider timeProvider)
    {
        var stack = await _stackRepository.GetByIdAsync(stackId);
        if (stack is null)
            return new NotFoundObjectResult($"Stack with Id {stackId} could not be found.");

        var workItemState = await FetchWorkItemState(workItemId);
        if (workItemState is null)
            return new NotFoundObjectResult($"Work Item with Id {workItemId} could not be found in Azure DevOps.");

        switch (workItemState)
        {
            case DevOpsWorkItemState.ToDo:
                stack.MarkOpen();
                break;
            case DevOpsWorkItemState.Doing:
                stack.MarkOpen();
                break;
            case DevOpsWorkItemState.Done:
                stack.MarkFixed(null, timeProvider);
                break;
        }

        stack.DevOpsWorkItemId = workItemId;
        stack.DevOpsWorkItemState = workItemState;

        await _stackRepository.SaveAsync(stack);

        return new NoContentResult();
    }

    public async Task<IActionResult> UnlinkWorkItemFromStack(string stackId)
    {
        var stack = await _stackRepository.GetByIdAsync(stackId);
        if (stack is null)
            return new NotFoundObjectResult($"Stack with Id {stackId} could not be found.");

        stack.DevOpsWorkItemId = null;
        stack.DevOpsWorkItemState = null;

        await _stackRepository.SaveAsync(stack);

        return new NoContentResult();
    }

    public async Task<IActionResult> UpdateRemoteWorkItemStateIfLinked(string stackId, StackStatus newStatus)
    {
        var stack = await _stackRepository.GetByIdAsync(stackId);
        if (string.IsNullOrWhiteSpace(stack.DevOpsWorkItemId))
            return new BadRequestObjectResult($"Stack with Id {stackId} is not linked to an Azure DevOps Work Item.");

        var workItemId = stack.DevOpsWorkItemId;
        DevOpsWorkItemState newWorkItemState;

        switch (newStatus)
        {
            case StackStatus.Open:
                newWorkItemState = DevOpsWorkItemState.ToDo;
                break;
            case StackStatus.Doing:
                newWorkItemState = DevOpsWorkItemState.Doing;
                break;
            case StackStatus.Snoozed:
                newWorkItemState = DevOpsWorkItemState.ToDo;
                break;
            case StackStatus.Fixed:
            case StackStatus.Regressed:
            case StackStatus.Ignored:
            case StackStatus.Discarded:
                newWorkItemState = DevOpsWorkItemState.Done;
                break;
            default:
                newWorkItemState = DevOpsWorkItemState.ToDo;
                break;
        }

        var httpClient = _httpClientFactory.CreateClient("devops");

        var encodedPat = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_pat}"));
        var request = new HttpRequestMessage(HttpMethod.Patch, $"WorkItems/{workItemId}?api-version=7.1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedPat);
        request.Content = new StringContent(
            JsonSerializer.Serialize(

                new[]
                {
                    new { op = "add", path = "/fields/System.State", value = newWorkItemState.ToDevOpsString() }
                }),
            Encoding.UTF8, "application/json-patch+json");

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return new NoContentResult();
    }

    public async Task<IActionResult> UpdateLocalWorkItemState(string workItemId, DevOpsWorkItemState newWorkItemState, TimeProvider timeProvider)
    {
        var stack = await _stackRepository.GetStackByDevOpsWorkItemIdAsync(workItemId);
        if (stack is null)
            return new NoContentResult();

        switch (newWorkItemState)
        {
            case DevOpsWorkItemState.ToDo:
                stack.MarkOpen();
                break;
            case DevOpsWorkItemState.Doing:
                stack.MarkDoing();
                break;
            case DevOpsWorkItemState.Done:
                stack.MarkFixed(null, timeProvider);
                break;
        }

        stack.DevOpsWorkItemState = newWorkItemState;

        await _stackRepository.SaveAsync(stack);

        return new NoContentResult();
    }

    private async Task<DevOpsWorkItemState?> FetchWorkItemState(string workItemId)
    {
        var httpClient = _httpClientFactory.CreateClient("devopsanalytics");

        var encodedPat = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_pat}"));
        var request = new HttpRequestMessage(HttpMethod.Get, $"WorkItems?$filter=WorkItemId eq {workItemId}&$select=State");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedPat);

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();

        using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
        {
            var root = doc.RootElement;
            var valueArray = root.GetProperty("value");

            if (valueArray.GetArrayLength() > 0)
            {
                var firstItem = valueArray[0];
                var workItemStateStr = firstItem.GetProperty("State").GetString();

                if (!string.IsNullOrEmpty(workItemStateStr))
                {
                    var workItemState = DevOpsWorkItemStateExtensions.FromDevOpsString(workItemStateStr);
                    return workItemState;
                }
            }
        }

        return null;
    }
}

public interface IDevOpsWorkItemService
{
    Task<IActionResult> LinkWorkItemToStack(string stackId, string workItemId, TimeProvider timeProvider);

    Task<IActionResult> UnlinkWorkItemFromStack(string stackId);

    Task<IActionResult> UpdateRemoteWorkItemStateIfLinked(string stackId, StackStatus newStatus);

    Task<IActionResult> UpdateLocalWorkItemState(string workItemId, DevOpsWorkItemState newWorkItemState, TimeProvider timeProvider);
}

public enum DevOpsWorkItemState
{
    ToDo,
    Doing,
    Done
}
