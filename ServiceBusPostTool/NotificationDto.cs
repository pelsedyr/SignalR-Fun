public record NotificationDto(string ReceiverId, string Content)
{
    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.Now);
}
