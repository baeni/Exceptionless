namespace Exceptionless.Extension.DevOps.Clients;

public interface IDevOpsClient
{
    Task<WorkItemStatus> GetWorkItemStatus(string workItemId);
    Task UpdateWorkItemStatus(string workItemId, WorkItemStatus newStatus);
}
