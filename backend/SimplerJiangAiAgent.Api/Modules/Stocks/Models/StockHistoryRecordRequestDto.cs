namespace SimplerJiangAiAgent.Api.Modules.Stocks.Models;

public sealed record StockHistoryRecordRequestDto(
    string Symbol,
    string Name,
    decimal Price,
    decimal ChangePercent,
    decimal TurnoverRate,
    decimal PeRatio,
    decimal High,
    decimal Low,
    decimal Speed
);