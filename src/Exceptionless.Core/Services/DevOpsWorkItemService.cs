using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Exceptionless.Core.Repositories;
using Microsoft.AspNetCore.Mvc;

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

    public async Task<IActionResult> LinkWorkItemToStackAsync(string stackId, string workItemId)
    {
        var stack = await _stackRepository.GetByIdAsync(stackId);
        if (stack is null)
            return new NotFoundResult();

        var workItemState = await GetWorkItemStateAsync(workItemId);
        if (workItemState is null)
            return new BadRequestResult();

        stack.DevOpsWorkItemId = workItemId;
        stack.DevOpsWorkItemStateName = workItemState;

        await _stackRepository.SaveAsync(stack);

        return new OkResult();
    }

    public async Task<IActionResult> UnlinkWorkItemFromStackAsync(string stackId)
    {
        var stack = await _stackRepository.GetByIdAsync(stackId);
        if (stack is null)
            return new NotFoundResult();

        stack.DevOpsWorkItemId = null;
        stack.DevOpsWorkItemStateName = null;

        await _stackRepository.SaveAsync(stack);

        return new NoContentResult();
    }

    public async Task<IActionResult> UpdateWorkItemStateAsync(string workItemId, string newState)
    {
        var stack = await _stackRepository.GetStackByDevOpsWorkItemIdAsync(workItemId);
        if (stack is null)
            return new OkObjectResult("No Stacks affected.");

        stack.DevOpsWorkItemStateName = newState;

        await _stackRepository.SaveAsync(stack);

        return new OkResult();
    }

    public async Task<string?> GetWorkItemStateAsync(string workItemId)
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
                var stateName = firstItem.GetProperty("State").GetString();

                return stateName;
            }
        }

        return null;
    }
}

public interface IDevOpsWorkItemService
{
    Task<IActionResult> LinkWorkItemToStackAsync(string stackId, string workItemId);

    Task<IActionResult> UnlinkWorkItemFromStackAsync(string stackId);

    Task<IActionResult> UpdateWorkItemStateAsync(string workItemId, string newState);

    Task<string?> GetWorkItemStateAsync(string workItemId);
}
