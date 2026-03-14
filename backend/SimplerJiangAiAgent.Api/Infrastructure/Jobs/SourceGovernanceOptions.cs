namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class SourceGovernanceOptions
{
    public const string SectionName = "SourceGovernance";

    public int IntervalSeconds { get; set; } = 86400;
    public int MaxConsecutiveFailures { get; set; } = 3;
    public decimal MinParseSuccessRate { get; set; } = 0.85m;
    public decimal MinTimestampCoverage { get; set; } = 0.90m;
    public int MaxFreshnessLagMinutes { get; set; } = 240;
    public decimal CandidatePromotionScore { get; set; } = 0.80m;
    public bool EnableLlmDiscovery { get; set; } = true;
    public bool EnableCrawlerAutoFix { get; set; } = true;
    public string LlmProvider { get; set; } = "active";
    public string LlmModel { get; set; } = "";
    public string RepositoryRoot { get; set; } = string.Empty;
    public int MaxDailyDiscoveryCandidates { get; set; } = 10;
    public int HttpTimeoutSeconds { get; set; } = 20;
    public int ReplayTimeoutSeconds { get; set; } = 180;
    public int RollbackGraceMinutes { get; set; } = 1440;
}