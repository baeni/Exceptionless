using Exceptionless.Core.Services;

namespace Exceptionless.Core.Extensions;

public static class DevOpsWorkItemStateExtensions
{
    public static string ToDevOpsString(this DevOpsWorkItemState workItemState)
    {
        return workItemState switch
        {
            DevOpsWorkItemState.ToDo => "To Do",
            DevOpsWorkItemState.Doing => "Doing",
            DevOpsWorkItemState.Done => "Done",
            _ => throw new ArgumentOutOfRangeException(nameof(workItemState), workItemState, null)
        };
    }

    public static DevOpsWorkItemState FromDevOpsString(string workItemStateStr)
    {
        return workItemStateStr switch
        {
            "To Do" => DevOpsWorkItemState.ToDo,
            "Doing" => DevOpsWorkItemState.Doing,
            "Done" => DevOpsWorkItemState.Done,
            _ => throw new ArgumentException($"Unknown work item state: {workItemStateStr}", nameof(workItemStateStr))
        };
    }
}
