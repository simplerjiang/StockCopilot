using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public interface IEastmoneySectorRotationClient
{
    Task<IReadOnlyList<EastmoneySectorBoardRow>> GetBoardRankingsAsync(string boardType, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EastmoneySectorLeaderRow>> GetSectorLeadersAsync(string sectorCode, int take, CancellationToken cancellationToken = default);
    Task<EastmoneyMarketBreadthSnapshot> GetMarketBreadthAsync(int take, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalMarketTurnoverAsync(CancellationToken cancellationToken = default);
    Task<int> GetLimitUpCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);
    Task<int> GetLimitDownCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);
    Task<int> GetBrokenBoardCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);
    Task<int> GetMaxLimitUpStreakAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);
}
