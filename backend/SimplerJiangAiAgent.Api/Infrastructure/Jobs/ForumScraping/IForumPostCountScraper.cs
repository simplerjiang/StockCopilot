namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public interface IForumPostCountScraper
{
    string Platform { get; }

    /// <summary>Legacy: returns a single total count. Keep for backward compat.</summary>
    Task<int?> GetPostCountAsync(string symbol, CancellationToken ct);

    /// <summary>
    /// Scrapes up to maxPages and returns posts grouped by publish date.
    /// Key = post publish date, Value = number of posts on that date.
    /// </summary>
    Task<Dictionary<DateOnly, int>> GetDailyBreakdownAsync(string symbol, int maxPages, CancellationToken ct);
}
