using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.Services;

public sealed class MockMemAlertService : IMemAlertService
{
    private readonly List<AlertVideo> _catalog = new();
    private readonly List<AlertRequest> _requests = new();
    private readonly Random _random = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MockMemAlertService()
    {
        SeedCatalog();
    }

    public async Task<IReadOnlyList<AlertVideo>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
        return _catalog.ToList();
    }

    public async Task<IReadOnlyList<AlertRequest>> GetActiveRequestsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            AdvanceStatuses();
            return _requests
                .OrderByDescending(r => r.SubmittedAt)
                .Take(6)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlertRequest> SubmitRequestAsync(
        AlertVideo video,
        string viewerName,
        string message,
        decimal tipAmount,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);

        var request = new AlertRequest
        {
            Id = $"REQ-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Video = video,
            ViewerName = viewerName,
            Message = message,
            TipAmount = tipAmount,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = RequestStatus.Processing
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _requests.Insert(0, request);
            if (!_catalog.Any(v => v.Id == video.Id) && video.IsCustom)
            {
                _catalog.Insert(0, video);
            }
        }
        finally
        {
            _gate.Release();
        }

        return request;
    }

    private void SeedCatalog()
    {
        _catalog.AddRange(new[]
        {
            new AlertVideo
            {
                Id = "meme-gg",
                Title = "Gachi Gachi Alert",
                Description = "Культовый 5-секундный Gachi ремикс",
                Category = "Classic",
                Duration = TimeSpan.FromSeconds(5),
                Price = 5,
                Source = new Uri("https://filesamples.com/samples/video/mp4/sample_640x360.mp4"),
                Thumbnail = new Uri("https://i.ytimg.com/vi/3OadQMqW_7U/mqdefault.jpg")
            },
            new AlertVideo
            {
                Id = "meme-pog",
                Title = "PogU Zoom",
                Description = "Реакция Pog в 720p",
                Category = "Emotes",
                Duration = TimeSpan.FromSeconds(7),
                Price = 7,
                Source = new Uri("https://samplelib.com/lib/preview/mp4/sample-5s.mp4"),
                Thumbnail = new Uri("https://i.kym-cdn.com/entries/icons/medium/000/031/015/EA427636-7884-425E-9D93-802FA8327AF0.jpeg")
            },
            new AlertVideo
            {
                Id = "meme-wow",
                Title = "Critical Wow",
                Description = "Тот самый дубляж из WoW",
                Category = "Voice",
                Duration = TimeSpan.FromSeconds(9),
                Price = 10,
                Source = new Uri("https://samplelib.com/lib/preview/mp4/sample-10s.mp4"),
                Thumbnail = new Uri("https://i.ytimg.com/vi/2Jqf6G6aHBA/mqdefault.jpg")
            },
            new AlertVideo
            {
                Id = "meme-anime",
                Title = "Nani?!",
                Description = "Драматичный кадр из аниме",
                Category = "Anime",
                Duration = TimeSpan.FromSeconds(6),
                Price = 8,
                Source = new Uri("https://samplelib.com/lib/preview/mp4/sample-12s.mp4"),
                Thumbnail = new Uri("https://i.ytimg.com/vi/OP387525/mqdefault.jpg")
            }
        });
    }

    private void AdvanceStatuses()
    {
        foreach (var request in _requests)
        {
            request.Status = request.Status switch
            {
                RequestStatus.Queued => RequestStatus.Processing,
                RequestStatus.Processing => RequestStatus.Completed,
                RequestStatus.Completed => RequestStatus.Completed,
                _ => request.Status
            };
        }
    }
}
