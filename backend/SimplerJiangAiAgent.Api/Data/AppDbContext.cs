using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<StockQuoteSnapshot> StockQuoteSnapshots => Set<StockQuoteSnapshot>();
    public DbSet<MarketIndexSnapshot> MarketIndexSnapshots => Set<MarketIndexSnapshot>();
    public DbSet<KLinePointEntity> KLinePoints => Set<KLinePointEntity>();
    public DbSet<MinuteLinePointEntity> MinuteLinePoints => Set<MinuteLinePointEntity>();
    public DbSet<IntradayMessageEntity> IntradayMessages => Set<IntradayMessageEntity>();
    public DbSet<LocalStockNews> LocalStockNews => Set<LocalStockNews>();
    public DbSet<LocalSectorReport> LocalSectorReports => Set<LocalSectorReport>();
    public DbSet<StockQueryHistory> StockQueryHistories => Set<StockQueryHistory>();
    public DbSet<StockAgentAnalysisHistory> StockAgentAnalysisHistories => Set<StockAgentAnalysisHistory>();
    public DbSet<StockChatSession> StockChatSessions => Set<StockChatSession>();
    public DbSet<StockChatMessage> StockChatMessages => Set<StockChatMessage>();
    public DbSet<NewsSourceRegistry> NewsSourceRegistries => Set<NewsSourceRegistry>();
    public DbSet<NewsSourceHealthDaily> NewsSourceHealthDailies => Set<NewsSourceHealthDaily>();
    public DbSet<NewsSourceCandidate> NewsSourceCandidates => Set<NewsSourceCandidate>();
    public DbSet<NewsSourceVerificationRun> NewsSourceVerificationRuns => Set<NewsSourceVerificationRun>();
    public DbSet<CrawlerChangeQueue> CrawlerChangeQueues => Set<CrawlerChangeQueue>();
    public DbSet<CrawlerChangeRun> CrawlerChangeRuns => Set<CrawlerChangeRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockQuoteSnapshot>()
            .HasIndex(x => new { x.Symbol, x.Timestamp });

        modelBuilder.Entity<MarketIndexSnapshot>()
            .HasIndex(x => new { x.Symbol, x.Timestamp });

        modelBuilder.Entity<KLinePointEntity>()
            .HasIndex(x => new { x.Symbol, x.Interval, x.Date });

        modelBuilder.Entity<MinuteLinePointEntity>()
            .HasIndex(x => new { x.Symbol, x.Date, x.Time });

        modelBuilder.Entity<IntradayMessageEntity>()
            .HasIndex(x => new { x.Symbol, x.PublishedAt });

        modelBuilder.Entity<LocalStockNews>()
            .HasIndex(x => new { x.Symbol, x.PublishTime });

        modelBuilder.Entity<LocalStockNews>()
            .HasIndex(x => new { x.Symbol, x.SourceTag });

        modelBuilder.Entity<LocalStockNews>()
            .HasIndex(x => new { x.IsAiProcessed, x.Symbol, x.PublishTime });

        modelBuilder.Entity<LocalSectorReport>()
            .HasIndex(x => new { x.Symbol, x.Level, x.PublishTime });

        modelBuilder.Entity<LocalSectorReport>()
            .HasIndex(x => new { x.Level, x.PublishTime });

        modelBuilder.Entity<LocalSectorReport>()
            .HasIndex(x => new { x.IsAiProcessed, x.Level, x.Symbol, x.PublishTime });

        modelBuilder.Entity<StockQueryHistory>()
            .HasIndex(x => x.Symbol)
            .IsUnique();

        modelBuilder.Entity<StockAgentAnalysisHistory>()
            .HasIndex(x => new { x.Symbol, x.CreatedAt });

        modelBuilder.Entity<StockChatSession>()
            .HasIndex(x => x.SessionKey)
            .IsUnique();

        modelBuilder.Entity<StockChatSession>()
            .HasIndex(x => new { x.Symbol, x.UpdatedAt });

        modelBuilder.Entity<StockChatMessage>()
            .HasIndex(x => x.SessionId);

        modelBuilder.Entity<StockChatMessage>()
            .HasOne(x => x.Session)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.SessionId);

        modelBuilder.Entity<NewsSourceRegistry>()
            .HasIndex(x => x.Domain)
            .IsUnique();

        modelBuilder.Entity<NewsSourceRegistry>()
            .HasIndex(x => new { x.Status, x.Tier });

        modelBuilder.Entity<NewsSourceHealthDaily>()
            .HasIndex(x => new { x.SourceId, x.HealthDate })
            .IsUnique();

        modelBuilder.Entity<NewsSourceCandidate>()
            .HasIndex(x => new { x.Domain, x.Status });

        modelBuilder.Entity<NewsSourceVerificationRun>()
            .HasIndex(x => new { x.Domain, x.ExecutedAt });

        modelBuilder.Entity<NewsSourceVerificationRun>()
            .HasIndex(x => x.TraceId);

        modelBuilder.Entity<CrawlerChangeQueue>()
            .HasIndex(x => new { x.SourceId, x.Status });

        modelBuilder.Entity<CrawlerChangeQueue>()
            .HasIndex(x => x.TraceId);

        modelBuilder.Entity<CrawlerChangeRun>()
            .HasIndex(x => new { x.QueueId, x.ExecutedAt });

        modelBuilder.Entity<CrawlerChangeRun>()
            .HasIndex(x => x.TraceId);

        modelBuilder.Entity<NewsSourceHealthDaily>()
            .HasOne(x => x.Source)
            .WithMany(x => x.HealthDailies)
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    property.SetPrecision(18);
                    property.SetScale(2);
                }
            }
        }
    }
}
