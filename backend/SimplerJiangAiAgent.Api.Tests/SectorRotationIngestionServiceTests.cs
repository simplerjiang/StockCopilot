using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market.Services;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class SectorRotationIngestionServiceTests
{
	[Fact]
	public async Task SyncAsync_SwallowsPartialUpstreamFailures_AndPersistsAvailableSnapshots()
	{
		await using var dbContext = CreateDbContext();
		var client = new FakeSectorRotationClient
		{
			ThrowBoardTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectorBoardTypes.Concept, SectorBoardTypes.Style },
			BoardRankings = new Dictionary<string, IReadOnlyList<EastmoneySectorBoardRow>>(StringComparer.OrdinalIgnoreCase)
			{
				[SectorBoardTypes.Industry] =
				[
					new EastmoneySectorBoardRow(SectorBoardTypes.Industry, "BK1001", "半导体", 2.8m, 120m, 40m, 30m, 20m, 30m, 300m, 12m, 1, "{}")
				]
			},
			Leaders = new Dictionary<string, IReadOnlyList<EastmoneySectorLeaderRow>>(StringComparer.OrdinalIgnoreCase)
			{
				["BK1001"] =
				[
					new EastmoneySectorLeaderRow(1, "688001", "华芯", 7.8m, 90m, false, false)
				]
			},
			MarketBreadth = new EastmoneyMarketBreadthSnapshot(2200, 1800, 200, 5000m),
			LimitUpCount = 42,
			LimitDownCount = 6,
			BrokenBoardCount = 8,
			MaxLimitUpStreak = 4
		};

		var service = new SectorRotationIngestionService(
			dbContext,
			client,
			Options.Create(new SectorRotationOptions { BoardPageSize = 20, LeaderTake = 5 }),
			NullLogger<SectorRotationIngestionService>.Instance);

		await service.SyncAsync();

		Assert.Single(await dbContext.MarketSentimentSnapshots.ToListAsync());
		Assert.Single(await dbContext.SectorRotationSnapshots.ToListAsync());
		Assert.Single(await dbContext.SectorRotationLeaderSnapshots.ToListAsync());
		var sentiment = await dbContext.MarketSentimentSnapshots.SingleAsync();
		var sector = await dbContext.SectorRotationSnapshots.SingleAsync();
		Assert.False(string.IsNullOrWhiteSpace(sentiment.StageLabelV2));
		Assert.True(sentiment.DiffusionScore >= 0m);
		Assert.True(sentiment.ContinuationScore >= 0m);
		Assert.True(sector.DiffusionRate > 0m);
		Assert.True(sector.StrengthAvg5d > 0m);
	}

	[Fact]
	public async Task SyncAsync_SkipsMarketSentimentSnapshot_WhenCriticalMarketSourceFails()
	{
		await using var dbContext = CreateDbContext();
		var client = new FakeSectorRotationClient
		{
			BoardRankings = new Dictionary<string, IReadOnlyList<EastmoneySectorBoardRow>>(StringComparer.OrdinalIgnoreCase)
			{
				[SectorBoardTypes.Industry] =
				[
					new EastmoneySectorBoardRow(SectorBoardTypes.Industry, "BK1001", "半导体", 2.8m, 120m, 40m, 30m, 20m, 30m, 300m, 12m, 1, "{}")
				]
			},
			Leaders = new Dictionary<string, IReadOnlyList<EastmoneySectorLeaderRow>>(StringComparer.OrdinalIgnoreCase)
			{
				["BK1001"] =
				[
					new EastmoneySectorLeaderRow(1, "688001", "华芯", 7.8m, 90m, false, false)
				]
			},
			MarketBreadth = new EastmoneyMarketBreadthSnapshot(2200, 1800, 200, 5000m),
			ThrowLimitUpCount = true,
			LimitDownCount = 6,
			BrokenBoardCount = 8,
			MaxLimitUpStreak = 4
		};

		var service = new SectorRotationIngestionService(
			dbContext,
			client,
			Options.Create(new SectorRotationOptions { BoardPageSize = 20, LeaderTake = 5 }),
			NullLogger<SectorRotationIngestionService>.Instance);

		await service.SyncAsync();

		Assert.Empty(await dbContext.MarketSentimentSnapshots.ToListAsync());
		Assert.Single(await dbContext.SectorRotationSnapshots.ToListAsync());
		Assert.Single(await dbContext.SectorRotationLeaderSnapshots.ToListAsync());
	}

	[Fact]
	public async Task SyncAsync_UsesResolvedTradingDateForPersistedSnapshots()
	{
		await using var dbContext = CreateDbContext();
		var client = new FakeSectorRotationClient
		{
			BoardRankings = new Dictionary<string, IReadOnlyList<EastmoneySectorBoardRow>>(StringComparer.OrdinalIgnoreCase)
			{
				[SectorBoardTypes.Concept] =
				[
					new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK001", "机器人", 3.6m, 100m, 30m, 20m, 20m, 30m, 280m, 10m, 1, "{}")
				]
			},
			Leaders = new Dictionary<string, IReadOnlyList<EastmoneySectorLeaderRow>>(StringComparer.OrdinalIgnoreCase)
			{
				["BK001"] =
				[
					new EastmoneySectorLeaderRow(1, "300001", "机器人龙头", 9.9m, 88m, true, false)
				]
			},
			MarketBreadth = new EastmoneyMarketBreadthSnapshot(2500, 1700, 200, 6200m),
			LimitUpCount = 55,
			LimitDownCount = 8,
			BrokenBoardCount = 9,
			MaxLimitUpStreak = 5
		};

		var service = new SectorRotationIngestionService(
			dbContext,
			client,
			Options.Create(new SectorRotationOptions { BoardPageSize = 20, LeaderTake = 5 }),
			NullLogger<SectorRotationIngestionService>.Instance);

		await service.SyncAsync(new DateTimeOffset(2026, 3, 15, 5, 40, 0, TimeSpan.Zero));

		var sentimentSnapshot = await dbContext.MarketSentimentSnapshots.SingleAsync();
		var sectorSnapshot = await dbContext.SectorRotationSnapshots.SingleAsync();

		Assert.Equal(new DateTime(2026, 3, 13), sentimentSnapshot.TradingDate);
		Assert.Equal(new DateTime(2026, 3, 13), sectorSnapshot.TradingDate);
	}

	[Fact]
	public void ResolveMarketPoolTradingDate_UsesPreviousTradingDay_OnWeekend()
	{
		var localNow = new DateTimeOffset(2026, 3, 15, 13, 40, 0, TimeSpan.FromHours(8));

		var result = SectorRotationIngestionService.ResolveMarketPoolTradingDate(localNow);

		Assert.Equal(new DateOnly(2026, 3, 13), result);
	}

	[Fact]
	public void ResolveMarketPoolTradingDate_UsesPreviousTradingDay_BeforeMarketOpen()
	{
		var localNow = new DateTimeOffset(2026, 3, 16, 9, 0, 0, TimeSpan.FromHours(8));

		var result = SectorRotationIngestionService.ResolveMarketPoolTradingDate(localNow);

		Assert.Equal(new DateOnly(2026, 3, 13), result);
	}

	[Fact]
	public void ComputeTurnoverConcentration_UsesMarketTurnoverDenominator()
	{
		var rows = new[]
		{
			new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK001", "机器人", 3.1m, 0m, 0m, 0m, 0m, 0m, 30m, 0m, 1, "{}"),
			new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK002", "算力", 2.4m, 0m, 0m, 0m, 0m, 0m, 20m, 0m, 2, "{}"),
			new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK003", "半导体", 1.8m, 0m, 0m, 0m, 0m, 0m, 10m, 0m, 3, "{}")
		};

		var result = SectorRotationIngestionService.ComputeTurnoverConcentration(rows, 100m);

		Assert.Equal(60m, result.Top3Share);
		Assert.Equal(60m, result.Top10Share);
	}

	[Fact]
	public void ComputeTurnoverConcentration_ClampsToHundredWhenSectorTurnoverExceedsMarketBase()
	{
		var rows = new[]
		{
			new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK001", "机器人", 3.1m, 0m, 0m, 0m, 0m, 0m, 80m, 0m, 1, "{}"),
			new EastmoneySectorBoardRow(SectorBoardTypes.Concept, "BK002", "算力", 2.4m, 0m, 0m, 0m, 0m, 0m, 50m, 0m, 2, "{}")
		};

		var result = SectorRotationIngestionService.ComputeTurnoverConcentration(rows, 100m);

		Assert.Equal(100m, result.Top3Share);
		Assert.Equal(100m, result.Top10Share);
	}

	private static AppDbContext CreateDbContext()
	{
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseInMemoryDatabase(Guid.NewGuid().ToString())
			.Options;

		return new AppDbContext(options);
	}

	private sealed class FakeSectorRotationClient : IEastmoneySectorRotationClient
	{
		public HashSet<string> ThrowBoardTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, IReadOnlyList<EastmoneySectorBoardRow>> BoardRankings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, IReadOnlyList<EastmoneySectorLeaderRow>> Leaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);
		public bool ThrowMarketBreadth { get; init; }
		public bool ThrowLimitUpCount { get; init; }
		public bool ThrowLimitDownCount { get; init; }
		public bool ThrowBrokenBoardCount { get; init; }
		public bool ThrowMaxLimitUpStreak { get; init; }
		public EastmoneyMarketBreadthSnapshot MarketBreadth { get; init; } = new(0, 0, 0, 0m);
		public int LimitUpCount { get; init; }
		public int LimitDownCount { get; init; }
		public int BrokenBoardCount { get; init; }
		public int MaxLimitUpStreak { get; init; }

		public Task<IReadOnlyList<EastmoneySectorBoardRow>> GetBoardRankingsAsync(string boardType, int take, CancellationToken cancellationToken = default)
		{
			if (ThrowBoardTypes.Contains(boardType))
			{
				throw new HttpRequestException($"boom-{boardType}");
			}

			return Task.FromResult(BoardRankings.TryGetValue(boardType, out var rows) ? rows : Array.Empty<EastmoneySectorBoardRow>());
		}

		public Task<IReadOnlyList<EastmoneySectorLeaderRow>> GetSectorLeadersAsync(string sectorCode, int take, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(Leaders.TryGetValue(sectorCode, out var rows) ? rows : Array.Empty<EastmoneySectorLeaderRow>());
		}

		public Task<EastmoneyMarketBreadthSnapshot> GetMarketBreadthAsync(int take, CancellationToken cancellationToken = default)
		{
			if (ThrowMarketBreadth)
			{
				throw new HttpRequestException("boom-market-breadth");
			}

			return Task.FromResult(MarketBreadth);
		}

		public Task<int> GetLimitUpCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
		{
			if (ThrowLimitUpCount)
			{
				throw new HttpRequestException("boom-limit-up");
			}

			return Task.FromResult(LimitUpCount);
		}

		public Task<int> GetLimitDownCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
		{
			if (ThrowLimitDownCount)
			{
				throw new HttpRequestException("boom-limit-down");
			}

			return Task.FromResult(LimitDownCount);
		}

		public Task<int> GetBrokenBoardCountAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
		{
			if (ThrowBrokenBoardCount)
			{
				throw new HttpRequestException("boom-broken-board");
			}

			return Task.FromResult(BrokenBoardCount);
		}

		public Task<int> GetMaxLimitUpStreakAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
		{
			if (ThrowMaxLimitUpStreak)
			{
				throw new HttpRequestException("boom-max-streak");
			}

			return Task.FromResult(MaxLimitUpStreak);
		}
	}
}