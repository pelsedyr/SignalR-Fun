using MessageHub.Static;
using Microsoft.Azure.Functions.Worker;

namespace MessageHub.Models;

/// <summary>
/// Isolated-worker functions can only bind one output on the method itself, so the second
/// output (SignalR) is exposed as a property on a wrapper the trigger returns instead.
/// </summary>
public class NotificationOutputs
{
    [SignalROutput(
        HubName = Strings.BindingExpressions.HubName,
        ConnectionStringSetting = Strings.BindingExpressions.SignalRConnection)]
    public SignalRMessageAction? Notification { get; set; }
}
