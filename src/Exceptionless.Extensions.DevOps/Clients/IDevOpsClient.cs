using Microsoft.AspNetCore.Http;

namespace Exceptionless.Extensions.DevOps.Clients;

public interface IDevOpsClient
{
    Task<WorkItemStatus?> GetWorkItemStatus(string workItemId);
    Task<IResult> UpdateWorkItemStatus(string workItemId, WorkItemStatus newStatus);
}
