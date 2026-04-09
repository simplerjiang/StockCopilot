using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IResearchSessionService
{
    Task<ResearchActiveSessionDto?> GetActiveSessionAsync(string symbol, CancellationToken cancellationToken = default);
    Task<ResearchSessionDetailDto?> GetSessionDetailAsync(long sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResearchSessionSummaryDto>> ListSessionsAsync(string symbol, int limit = 20, CancellationToken cancellationToken = default);
    Task<ResearchTurnSubmitResponseDto> SubmitTurnAsync(ResearchTurnSubmitRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> CancelActiveTurnAsync(long sessionId, CancellationToken cancellationToken = default);
}
