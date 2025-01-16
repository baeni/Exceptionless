using Exceptionless.Core.Models;
using Exceptionless.Core.Repositories;
using Exceptionless.Extension.DevOps.Clients;
using Microsoft.Extensions.Logging;

namespace Exceptionless.Extension.DevOps.Services;

public class DevOpsWorkItemService : IDevOpsWorkItemService
{
    private readonly IStackRepository _stackRepository;
    private readonly IDevOpsClient _devOpsClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DevOpsWorkItemService> _logger;

    public DevOpsWorkItemService(IStackRepository stackRepository, IDevOpsClient devOpsClient, TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        _stackRepository = stackRepository;
        _devOpsClient = devOpsClient;
        _timeProvider = timeProvider;
        _logger = loggerFactory.CreateLogger<DevOpsWorkItemService>();
    }

    public async Task UpdateStackStatus(Stack stack, StackStatus newStackStatus)
    {
        stack.Status = newStackStatus;
        if (newStackStatus == StackStatus.Fixed)
        {
            stack.DateFixed = _timeProvider.GetUtcNow().UtcDateTime;
        }
        else
        {
            stack.DateFixed = null;
            stack.FixedInVersion = null;
        }

        if (newStackStatus != StackStatus.Snoozed)
            stack.SnoozeUntilUtc = null;

        await _stackRepository.SaveAsync(stack);
        _logger.LogInformation($"Updated status of stack {stack.Id} to {nameof(newStackStatus)}");
    }

    public async Task UpdateWorkItemStatus(string workItemId, WorkItemStatus newWorkItemStatus)
    {
        await _devOpsClient.UpdateWorkItemStatus(workItemId, newWorkItemStatus);
        _logger.LogInformation($"Updated status of work item {workItemId} to {nameof(newWorkItemStatus)}");
    }

    public async Task<WorkItemStatus> GetWorkItemStatus(string workItemId)
    {
        var workItemStatus = await _devOpsClient.GetWorkItemStatus(workItemId);
        _logger.LogInformation($"Got status for work item {workItemId}");

        return workItemStatus;
    }
}
