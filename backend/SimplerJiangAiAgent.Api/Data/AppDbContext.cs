using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Data;

public sealed class AppDbContext : DbContext
{
    private static readonly ValueConverter<TradingPlanStatus, string> TradingPlanStatusConverter = new(
        status => status.ToString(),
        value => ParseTradingPlanStatus(value));
    private static readonly ValueConverter<TradingPlanEventType, string> TradingPlanEventTypeConverter = new(
        value => value.ToString(),
        value => ParseTradingPlanEventType(value));
    private static readonly ValueConverter<TradingPlanEventSeverity, string> TradingPlanEventSeverityConverter = new(
        value => value.ToString(),
        value => ParseTradingPlanEventSeverity(value));

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ActiveWatchlist> ActiveWatchlists => Set<ActiveWatchlist>();
    public DbSet<StockQuoteSnapshot> StockQuoteSnapshots => Set<StockQuoteSnapshot>();
    public DbSet<StockCompanyProfile> StockCompanyProfiles => Set<StockCompanyProfile>();
    public DbSet<MarketIndexSnapshot> MarketIndexSnapshots => Set<MarketIndexSnapshot>();
    public DbSet<KLinePointEntity> KLinePoints => Set<KLinePointEntity>();
    public DbSet<MinuteLinePointEntity> MinuteLinePoints => Set<MinuteLinePointEntity>();
    public DbSet<IntradayMessageEntity> IntradayMessages => Set<IntradayMessageEntity>();
    public DbSet<LocalStockNews> LocalStockNews => Set<LocalStockNews>();
    public DbSet<LocalSectorReport> LocalSectorReports => Set<LocalSectorReport>();
    public DbSet<StockQueryHistory> StockQueryHistories => Set<StockQueryHistory>();
    public DbSet<StockAgentAnalysisHistory> StockAgentAnalysisHistories => Set<StockAgentAnalysisHistory>();
    public DbSet<TradingPlan> TradingPlans => Set<TradingPlan>();
    public DbSet<TradingPlanEvent> TradingPlanEvents => Set<TradingPlanEvent>();
    public DbSet<StockChatSession> StockChatSessions => Set<StockChatSession>();
    public DbSet<StockChatMessage> StockChatMessages => Set<StockChatMessage>();
    public DbSet<MarketSentimentSnapshot> MarketSentimentSnapshots => Set<MarketSentimentSnapshot>();
    public DbSet<SectorRotationSnapshot> SectorRotationSnapshots => Set<SectorRotationSnapshot>();
    public DbSet<SectorRotationLeaderSnapshot> SectorRotationLeaderSnapshots => Set<SectorRotationLeaderSnapshot>();
    public DbSet<NewsSourceRegistry> NewsSourceRegistries => Set<NewsSourceRegistry>();
    public DbSet<NewsSourceHealthDaily> NewsSourceHealthDailies => Set<NewsSourceHealthDaily>();
    public DbSet<NewsSourceCandidate> NewsSourceCandidates => Set<NewsSourceCandidate>();
    public DbSet<NewsSourceVerificationRun> NewsSourceVerificationRuns => Set<NewsSourceVerificationRun>();
    public DbSet<CrawlerChangeQueue> CrawlerChangeQueues => Set<CrawlerChangeQueue>();
    public DbSet<CrawlerChangeRun> CrawlerChangeRuns => Set<CrawlerChangeRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActiveWatchlist>()
            .HasIndex(x => x.Symbol)
            .IsUnique();

        modelBuilder.Entity<ActiveWatchlist>()
            .HasIndex(x => new { x.IsEnabled, x.UpdatedAt });

        modelBuilder.Entity<ActiveWatchlist>()
            .Property(x => x.Symbol)
            .HasMaxLength(32);

        modelBuilder.Entity<ActiveWatchlist>()
            .Property(x => x.Name)
            .HasMaxLength(128);

        modelBuilder.Entity<ActiveWatchlist>()
            .Property(x => x.SourceTag)
            .HasMaxLength(64);

        modelBuilder.Entity<ActiveWatchlist>()
            .Property(x => x.Note)
            .HasMaxLength(256);

        modelBuilder.Entity<StockQuoteSnapshot>()
            .HasIndex(x => new { x.Symbol, x.Timestamp });

        modelBuilder.Entity<StockQuoteSnapshot>()
            .Property(x => x.Symbol)
            .HasMaxLength(32);

        modelBuilder.Entity<StockQuoteSnapshot>()
            .Property(x => x.Name)
            .HasMaxLength(128);

        modelBuilder.Entity<StockQuoteSnapshot>()
            .Property(x => x.SectorName)
            .HasMaxLength(128);

        modelBuilder.Entity<StockCompanyProfile>()
            .HasIndex(x => x.Symbol)
            .IsUnique();

        modelBuilder.Entity<StockCompanyProfile>()
            .Property(x => x.Symbol)
            .HasMaxLength(32);

        modelBuilder.Entity<StockCompanyProfile>()
            .Property(x => x.Name)
            .HasMaxLength(128);

        modelBuilder.Entity<StockCompanyProfile>()
            .Property(x => x.SectorName)
            .HasMaxLength(128);

        modelBuilder.Entity<StockCompanyProfile>()
            .Property(x => x.FundamentalFactsJson)
            .HasColumnType("nvarchar(max)");

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

        modelBuilder.Entity<TradingPlan>()
            .HasIndex(x => x.PlanKey)
            .IsUnique();

        modelBuilder.Entity<TradingPlan>()
            .HasIndex(x => new { x.Symbol, x.CreatedAt });

        modelBuilder.Entity<TradingPlan>()
            .HasIndex(x => x.AnalysisHistoryId);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.PlanKey)
            .HasMaxLength(64);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.Title)
            .HasMaxLength(450);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.Symbol)
            .HasMaxLength(32);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.Name)
            .HasMaxLength(128);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.Direction)
            .HasConversion<string>()
            .HasMaxLength(16);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.Status)
            .HasConversion(TradingPlanStatusConverter)
            .HasMaxLength(16);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.SourceAgent)
            .HasMaxLength(64);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.MarketStageLabelAtCreation)
            .HasMaxLength(16);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.ExecutionFrequencyLabel)
            .HasMaxLength(32);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.MainlineSectorName)
            .HasMaxLength(128);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.SectorNameAtCreation)
            .HasMaxLength(128);

        modelBuilder.Entity<TradingPlan>()
            .Property(x => x.SectorCodeAtCreation)
            .HasMaxLength(32);

        modelBuilder.Entity<TradingPlan>()
            .HasOne(x => x.AnalysisHistory)
            .WithMany()
            .HasForeignKey(x => x.AnalysisHistoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TradingPlanEvent>()
            .HasIndex(x => new { x.PlanId, x.OccurredAt });

        modelBuilder.Entity<TradingPlanEvent>()
            .HasIndex(x => new { x.Symbol, x.OccurredAt });

        modelBuilder.Entity<TradingPlanEvent>()
            .Property(x => x.Symbol)
            .HasMaxLength(32);

        modelBuilder.Entity<TradingPlanEvent>()
            .Property(x => x.Strategy)
            .HasMaxLength(64);

        modelBuilder.Entity<TradingPlanEvent>()
            .Property(x => x.EventType)
            .HasConversion(TradingPlanEventTypeConverter)
            .HasMaxLength(32);

        modelBuilder.Entity<TradingPlanEvent>()
            .Property(x => x.Severity)
            .HasConversion(TradingPlanEventSeverityConverter)
            .HasMaxLength(16);

        modelBuilder.Entity<TradingPlanEvent>()
            .HasOne(x => x.Plan)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

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

        modelBuilder.Entity<MarketSentimentSnapshot>()
            .HasIndex(x => new { x.TradingDate, x.SnapshotTime });

        modelBuilder.Entity<MarketSentimentSnapshot>()
            .Property(x => x.SessionPhase)
            .HasMaxLength(16);

        modelBuilder.Entity<MarketSentimentSnapshot>()
            .Property(x => x.StageLabel)
            .HasMaxLength(16);

        modelBuilder.Entity<MarketSentimentSnapshot>()
            .Property(x => x.StageLabelV2)
            .HasMaxLength(16);

        modelBuilder.Entity<MarketSentimentSnapshot>()
            .Property(x => x.SourceTag)
            .HasMaxLength(32);

        modelBuilder.Entity<SectorRotationSnapshot>()
            .HasIndex(x => new { x.BoardType, x.SnapshotTime, x.RankNo });

        modelBuilder.Entity<SectorRotationSnapshot>()
            .HasIndex(x => new { x.SectorCode, x.BoardType, x.SnapshotTime });

        modelBuilder.Entity<SectorRotationSnapshot>()
            .Property(x => x.BoardType)
            .HasMaxLength(16);

        modelBuilder.Entity<SectorRotationSnapshot>()
            .Property(x => x.SectorCode)
            .HasMaxLength(32);

        modelBuilder.Entity<SectorRotationSnapshot>()
            .Property(x => x.SectorName)
            .HasMaxLength(128);

        modelBuilder.Entity<SectorRotationSnapshot>()
            .Property(x => x.NewsSentiment)
            .HasMaxLength(16);

        modelBuilder.Entity<SectorRotationSnapshot>()
            .Property(x => x.LeaderSymbol)
            .HasMaxLength(32);

        modelBuilder.Entity<SectorRotationSnapshot>()
            .Property(x => x.LeaderName)
            .HasMaxLength(128);

        modelBuilder.Entity<SectorRotationSnapshot>()
            .Property(x => x.SourceTag)
            .HasMaxLength(32);

        modelBuilder.Entity<SectorRotationLeaderSnapshot>()
            .HasIndex(x => new { x.SectorRotationSnapshotId, x.RankInSector });

        modelBuilder.Entity<SectorRotationLeaderSnapshot>()
            .Property(x => x.Symbol)
            .HasMaxLength(32);

        modelBuilder.Entity<SectorRotationLeaderSnapshot>()
            .Property(x => x.Name)
            .HasMaxLength(128);

        modelBuilder.Entity<SectorRotationLeaderSnapshot>()
            .HasOne(x => x.SectorRotationSnapshot)
            .WithMany(x => x.Leaders)
            .HasForeignKey(x => x.SectorRotationSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

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

    internal static TradingPlanStatus ParseTradingPlanStatus(string? value)
    {
        if (Enum.TryParse<TradingPlanStatus>(value, true, out var parsed))
        {
            return parsed;
        }

        if (string.Equals(value, "Archived", StringComparison.OrdinalIgnoreCase))
        {
            return TradingPlanStatus.Cancelled;
        }

        if (string.Equals(value, "Review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "NeedsReview", StringComparison.OrdinalIgnoreCase))
        {
            return TradingPlanStatus.ReviewRequired;
        }

        return TradingPlanStatus.Cancelled;
    }

    internal static TradingPlanEventType ParseTradingPlanEventType(string? value)
    {
        return Enum.TryParse<TradingPlanEventType>(value, true, out var parsed)
            ? parsed
            : TradingPlanEventType.VolumeDivergenceWarning;
    }

    internal static TradingPlanEventSeverity ParseTradingPlanEventSeverity(string? value)
    {
        return Enum.TryParse<TradingPlanEventSeverity>(value, true, out var parsed)
            ? parsed
            : TradingPlanEventSeverity.Warning;
    }
}
