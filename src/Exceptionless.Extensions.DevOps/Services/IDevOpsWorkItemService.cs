using Exceptionless.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Exceptionless.Extensions.DevOps.Services
{
    public interface IDevOpsWorkItemService
    {
        Task<IResult> LinkStackToWorkItem(string stackId, string workItemId);

        Task<IResult> UnlinkStackFromWorkItem(string stackId);

        Task<IResult> UpdateStackStatus(string stackId, StackStatus newStatus);

        Task<IResult> UpdateWorkItemStatus(string workItemId, WorkItemStatus newStatus);
    }
}
