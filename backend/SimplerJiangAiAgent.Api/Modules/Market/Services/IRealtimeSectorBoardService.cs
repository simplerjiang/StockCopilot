using SimplerJiangAiAgent.Api.Modules.Market.Models;

namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public interface IRealtimeSectorBoardService
{
    Task<RealtimeSectorBoardPageDto> GetPageAsync(string boardType, int take, string? sort = null, CancellationToken cancellationToken = default);
}