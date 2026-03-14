namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class HighFrequencyQuoteOptions
{
    public const string SectionName = "HighFrequencyQuotes";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 90;
    public int MaxSymbols { get; set; } = 40;
    public int MaxConcurrentSymbols { get; set; } = 4;
}