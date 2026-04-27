namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class UnsupportedStockSourceException : Exception
{
    public UnsupportedStockSourceException(string sourceName)
        : base($"Unsupported stock data source: {sourceName}")
    {
        SourceName = sourceName;
    }

    public string SourceName { get; }
}