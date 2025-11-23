using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.Services;

public interface IMemAlertService
{
    Task<IReadOnlyList<AlertVideo>> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlertRequest>> GetActiveRequestsAsync(CancellationToken cancellationToken = default);
    Task<AlertRequest> SubmitRequestAsync(
        AlertVideo video,
        string viewerName,
        string message,
        decimal tipAmount,
        CancellationToken cancellationToken = default);
}

