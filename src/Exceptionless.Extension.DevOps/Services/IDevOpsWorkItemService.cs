using Exceptionless.Core.Models;

namespace Exceptionless.Extension.DevOps.Services;

public interface IDevOpsWorkItemService
{
    Task UpdateStackStatus(Stack stack, StackStatus newStatus);

    Task UpdateWorkItemStatus(string workItemId, WorkItemStatus newStatus);

    Task<WorkItemStatus> GetWorkItemStatus(string workItemId);
}
