using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public interface ISectorRotationQueryService
{
    Task<MarketSentimentSummaryDto?> GetLatestSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketSentimentHistoryPointDto>> GetHistoryAsync(int days, CancellationToken cancellationToken = default);
    Task<SectorRotationPageDto> GetSectorPageAsync(string boardType, int page, int pageSize, string sort, CancellationToken cancellationToken = default);
    Task<SectorRotationDetailDto?> GetSectorDetailAsync(string sectorCode, string boardType, string window, CancellationToken cancellationToken = default);
    Task<SectorRotationTrendDto?> GetSectorTrendAsync(string sectorCode, string boardType, string window, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SectorRotationLeaderDto>> GetLeadersAsync(string sectorCode, string boardType, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SectorRotationListItemDto>> GetMainlineAsync(string boardType, string window, int take, CancellationToken cancellationToken = default);
}
