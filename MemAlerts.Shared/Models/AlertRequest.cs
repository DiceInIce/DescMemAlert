using System;

namespace MemAlerts.Shared.Models;

public sealed class AlertRequest
{
    public required string Id { get; init; }
    public required AlertVideo Video { get; init; }
    public required string ViewerName { get; init; }
    public string Message { get; init; } = string.Empty;
    public decimal TipAmount { get; init; }
    public DateTimeOffset SubmittedAt { get; init; } = DateTimeOffset.UtcNow;
    public RequestStatus Status { get; set; } = RequestStatus.Queued;
}

