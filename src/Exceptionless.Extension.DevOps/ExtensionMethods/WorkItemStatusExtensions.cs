using Exceptionless.Core.Models;

namespace Exceptionless.Extension.DevOps.ExtensionMethods;

public static class WorkItemStatusExtensions
{
    public static WorkItemStatus? FromString(string status)
    {
        return status.ToLower() switch
        {
            "to do" or "open" or "snoozed" => WorkItemStatus.ToDo,
            "doing" or "in progress" => WorkItemStatus.Doing,
            "done" or "fixed" or "regressed" or "ignored" or "discarded" => WorkItemStatus.Done,
            _ => null
        };
    }

    public static StackStatus ToStackStatus(this WorkItemStatus status)
    {
        return status switch
        {
            WorkItemStatus.ToDo => StackStatus.Open,
            WorkItemStatus.Doing => StackStatus.Doing,
            WorkItemStatus.Done => StackStatus.Fixed,
            _ => StackStatus.Open
        };
    }

    public static string ToFriendlyString(this WorkItemStatus status)
    {
        return status switch
        {
            WorkItemStatus.ToDo => "to do",
            WorkItemStatus.Doing => "doing",
            WorkItemStatus.Done => "done",
            _ => "to do"
        };
    }
}
