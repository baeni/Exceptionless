using Exceptionless.Core.Models;

namespace Exceptionless.Extension.DevOps.ExtensionMethods;

public static class StackStatusExtensions
{
    public static StackStatus? FromString(string status)
    {
        return status.ToLower() switch
        {
            "to do" or "open" or "snoozed" => StackStatus.Open,
            "doing" or "in progress" => StackStatus.Doing,
            "done" or "fixed" or "regressed" or "ignored" or "discarded" => StackStatus.Fixed,
            _ => null
        };
    }

    public static WorkItemStatus ToWorkItemStatus(this StackStatus status)
    {
        return status switch
        {
            StackStatus.Open or StackStatus.Snoozed => WorkItemStatus.ToDo,
            StackStatus.Doing => WorkItemStatus.Doing,
            StackStatus.Fixed or StackStatus.Regressed or StackStatus.Ignored or StackStatus.Discarded => WorkItemStatus.Done,
            _ => WorkItemStatus.ToDo
        };
    }

    public static string ToFriendlyString(this StackStatus status)
    {
        return status switch
        {
            StackStatus.Open => "open",
            StackStatus.Doing => "doing",
            StackStatus.Fixed => "fixed",
            _ => "open"
        };
    }
}
