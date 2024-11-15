using System.ComponentModel;
using Exceptionless.Core.Models;

namespace Exceptionless.Extensions.DevOps;

public enum WorkItemStatus
{
    ToDo,
    Doing,
    Done
}

public static class WorkItemStatusExtensions
{
    // Used twice => DevOpsClient, SyncWithDevOpsModule
    public static WorkItemStatus? ToWorkItemStatus(this string stackStatusStr)
    {
        return stackStatusStr.ToLower() switch
        {
            "open" or "snoozed" => WorkItemStatus.ToDo,
            "doing" => WorkItemStatus.Doing,
            "fixed" or "regressed" or "ignored" or "discarded" => WorkItemStatus.Done,
            _ => null
        };
    }

    // Used once => SyncWithDevOpsModule
    public static StackStatus? ToStackStatus(this string workItemStatusStr)
    {
        return workItemStatusStr.ToLower() switch
        {
            "to do" => StackStatus.Open,
            "doing" => StackStatus.Doing,
            "done" => StackStatus.Fixed,
            _ => null
        };
    }

    // Used once => DevOpsClient
    public static string ToFriendlyString(this WorkItemStatus workItemStatus)
    {
        return workItemStatus switch
        {
            WorkItemStatus.ToDo => "to do",
            WorkItemStatus.Doing => "doing",
            WorkItemStatus.Done => "done",
            _ => throw new InvalidEnumArgumentException(nameof(workItemStatus))
        };
    }
}
