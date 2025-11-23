using System;

namespace MemAlerts.Shared.Models;

public sealed class AlertVideo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = "Classic";
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(5);
    public decimal Price { get; init; } = 5;
    public Uri Source { get; init; } = new("https://samplelib.com/lib/preview/mp4/sample-5s.mp4");
    public Uri Thumbnail { get; init; } = new("https://placehold.co/320x180");
    public bool IsCommunityFavorite { get; init; }
    public bool IsCustom { get; init; }
}

