namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public interface IForumPostCountScraper
{
    string Platform { get; }
    Task<int?> GetPostCountAsync(string symbol, CancellationToken ct);
}
