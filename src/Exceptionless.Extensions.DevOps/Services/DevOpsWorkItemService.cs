using Exceptionless.Core.Models;
using Exceptionless.Core.Repositories;
using Exceptionless.Extensions.DevOps.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Exceptionless.Extensions.DevOps.Services
{
    public class DevOpsWorkItemService : IDevOpsWorkItemService
    {
        private readonly IStackRepository _stackRepository;
        private readonly IDevOpsClient _devOpsClient;
        private readonly ILogger<DevOpsWorkItemService> _logger;

        public DevOpsWorkItemService(IStackRepository stackRepository, IDevOpsClient devOpsClient, ILoggerFactory loggerFactory)
        {
            _stackRepository = stackRepository;
            _devOpsClient = devOpsClient;
            _logger = loggerFactory.CreateLogger<DevOpsWorkItemService>();
        }

        public async Task<IResult> LinkStackToWorkItem(string stackId, string workItemId)
        {
            var stack = await _stackRepository.GetByIdAsync(stackId);
            if (stack is null)
            {
                var errMsg = string.Format("Stack {0: stackID} could not be found", stackId);
                _logger.LogError(errMsg);
                return Results.NotFound(errMsg);
            }

            var baseUrl = $"https://dev.azure.com/bsaalfeld/Exceptionless-Extension/_workitems/edit";
            var existing = stack.References.FirstOrDefault(r => r.StartsWith(baseUrl));
            if (existing != null)
            {
                _logger.LogInformation("Skipped linking stack {stackId} with work item {workItemId}, " +
                                       "already exists at {existing}", stackId, workItemId, existing);
                return Results.NoContent();
            }

            var url = $"{baseUrl}/{workItemId}";

            stack.References.Add(url);
            await _stackRepository.SaveAsync(stack);

            _logger.LogInformation("Linked stack {stackId} with work item {workItemId}: {url}", stackId, workItemId, url);
            return Results.Ok(url); // Maybe fetch current Work Item state here as well, and pass url + current state?
        }

        public async Task<IResult> UnlinkStackFromWorkItem(string stackId)
        {
            var stack = await _stackRepository.GetByIdAsync(stackId);
            if (stack is null)
            {
                var errMsg = string.Format("Stack {0: stackID} could not be found", stackId);
                _logger.LogError(errMsg);
                return Results.NotFound(errMsg);
            }

            var baseUrl = $"https://dev.azure.com/bsaalfeld/Exceptionless-Extension/_workitems/edit";
            var existing = stack.References.FirstOrDefault(r => r.StartsWith(baseUrl));
            if (existing is null)
            {
                _logger.LogInformation("Skipped unlinking stack {stackId} from work item, " +
                                       "no work item linked", stackId);
                return Results.NoContent();
            }

            stack.References.Remove(existing);
            await _stackRepository.SaveAsync(stack);

            _logger.LogInformation("Unlinked stack {stackId} from work item", stackId);
            return Results.NoContent();
        }

        public async Task<IResult> UpdateStackStatus(string stackId, StackStatus newStatus)
        {
            var stack = await _stackRepository.GetByIdAsync(stackId);
            if (stack is null)
            {
                var errMsg = string.Format("Stack {0: stackID} could not be found", stackId);
                _logger.LogError(errMsg);
                return Results.NotFound(errMsg);
            }

            stack.Status = newStatus;
            await _stackRepository.SaveAsync(stack);

            _logger.LogInformation("Updated status of stack {stackId} to {newStatus}", stackId, newStatus);
            return Results.NoContent();
        }

        public async Task<IResult> UpdateWorkItemStatus(string workItemId, WorkItemStatus newStatus)
        {
            await _devOpsClient.UpdateWorkItemStatus(workItemId, newStatus);
            return Results.NoContent();
        }
    }
}
