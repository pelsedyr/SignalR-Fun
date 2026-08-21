namespace MessageHub;

// Mirrors ServiceBusPostTool/NotificationDto.cs. Kept as a separate copy for now since
// MessageHub and ServiceBusPostTool don't share a project — see SIGNALR-EMULATOR-PLAN.md.
public record NotificationDto(string ReceiverId, string Content)
{
    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.Now);
}
