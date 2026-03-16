namespace SimplerJiangAiAgent.Api.Modules.Market.Services;

public interface ISectorRotationIngestionService
{
    Task SyncAsync(CancellationToken cancellationToken = default);
}
