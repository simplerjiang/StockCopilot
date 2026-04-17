using SimplerJiangAiAgent.Api.Modules.Market.Models;
using SimplerJiangAiAgent.Api.Modules.Market;
using SimplerJiangAiAgent.Api.Modules.Market.Services;
using System.Net;
using System.Reflection;
using System.Text;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class EastmoneySectorRotationClientTests
{
        [Fact]
        public async Task GetBoardRankingsAsync_MergesBkzjF3AndF62Rows()
    {
                var handler = new RouteHttpMessageHandler(request =>
                {
                        var url = request.RequestUri?.ToString() ?? string.Empty;
                        if (url.Contains("dataapi/bkzj/getbkzj", StringComparison.OrdinalIgnoreCase) && url.Contains("key=f3", StringComparison.OrdinalIgnoreCase))
                        {
                                return Json("""
                                {
                                    "data": [
                                        { "f12": "BK001", "f14": "AI", "f3": 3.2 },
                                        { "f12": "BK002", "f14": "Robot", "f3": 2.1 }
                                    ]
                                }
                                """);
                        }

                        if (url.Contains("dataapi/bkzj/getbkzj", StringComparison.OrdinalIgnoreCase) && url.Contains("key=f62", StringComparison.OrdinalIgnoreCase))
                        {
                                return Json("""
                                {
                                    "data": [
                                        { "f12": "BK002", "f14": "Robot", "f62": 1500000 },
                                        { "f12": "BK003", "f14": "Compute", "f62": 1200000 }
                                    ]
                                }
                                """);
                        }

                        throw new InvalidOperationException($"Unexpected request: {url}");
                });

                using var httpClient = new HttpClient(handler);
                var client = new EastmoneySectorRotationClient(httpClient);

                var result = await client.GetBoardRankingsAsync(SectorBoardTypes.Concept, 10);

                Assert.Equal(3, result.Count);
                Assert.Contains(result, x => x.SectorCode == "BK001" && x.ChangePercent == 3.2m);
                Assert.Contains(result, x => x.SectorCode == "BK002" && x.ChangePercent == 2.1m && x.MainNetInflow == 1500000m);
                Assert.Contains(result, x => x.SectorCode == "BK003" && x.MainNetInflow == 1200000m);
                Assert.Equal(1, handler.CountRequests("key=f3"));
                Assert.Equal(1, handler.CountRequests("key=f62"));
        }

        [Fact]
        public async Task GetMaxLimitUpStreakAsync_UsesThsContinuousLimitUpAsPrimary()
        {
                var handler = new RouteHttpMessageHandler(request =>
                {
                        var url = request.RequestUri?.ToString() ?? string.Empty;
                        if (url.Contains("continuous_limit_up", StringComparison.OrdinalIgnoreCase))
                        {
                                return Json("""
                                {
                                    "data": [
                                        { "height": 2 },
                                        { "height": 5 },
                                        { "height": 3 }
                                    ]
                                }
                                """);
                        }

                        throw new InvalidOperationException($"Unexpected request: {url}");
                });

                using var httpClient = new HttpClient(handler);
                var client = new EastmoneySectorRotationClient(httpClient);

                var value = await client.GetMaxLimitUpStreakAsync(new DateOnly(2026, 4, 16));

                Assert.Equal(5, value);
                Assert.Equal(1, handler.CountRequests("continuous_limit_up"));
                Assert.Equal(0, handler.CountRequests("getTopicZTPool"));
        }

        [Fact]
        public async Task GetMaxLimitUpStreakAsync_FallsBackWhenThsPrimaryFails()
        {
                var handler = new RouteHttpMessageHandler(request =>
                {
                        var url = request.RequestUri?.ToString() ?? string.Empty;
                        if (url.Contains("continuous_limit_up", StringComparison.OrdinalIgnoreCase))
                        {
                                throw new HttpRequestException("ths unavailable");
                        }

                        if (url.Contains("getTopicZTPool", StringComparison.OrdinalIgnoreCase))
                        {
                                return Json("""
                                {
                                    "data": {
                                        "pool": [
                                            { "lbc": 4 },
                                            { "lbc": 6 },
                                            { "lbc": 5 }
                                        ]
                                    }
                                }
                                """);
                        }

                        throw new InvalidOperationException($"Unexpected request: {url}");
                });

                using var httpClient = new HttpClient(handler);
                var client = new EastmoneySectorRotationClient(httpClient);

                var value = await client.GetMaxLimitUpStreakAsync(new DateOnly(2026, 4, 16));

                Assert.Equal(6, value);
                Assert.Equal(1, handler.CountRequests("continuous_limit_up"));
                Assert.Equal(1, handler.CountRequests("getTopicZTPool"));
        }

        [Fact]
        public async Task GetMarketBreadthAsync_UsesUlistTurnoverAggregation()
        {
                var handler = new RouteHttpMessageHandler(request =>
                {
                        var url = request.RequestUri?.ToString() ?? string.Empty;
                        if (url.Contains("api/qt/clist/get", StringComparison.OrdinalIgnoreCase)
                                && url.Contains("fields=f12,f3,f6", StringComparison.OrdinalIgnoreCase))
                        {
                                return Json("""
                                {
                                    "data": {
                                        "total": 1,
                                        "diff": [
                                            { "f12": "000001", "f3": 1.5, "f6": 10 }
                                        ]
                                    }
                                }
                                """);
                        }

                                                if (url.Contains("api/qt/ulist.np/get", StringComparison.OrdinalIgnoreCase)
                                                                && url.Contains("secids=1.000001,0.399001", StringComparison.OrdinalIgnoreCase))
                        {
                                return Json("""
                                {
                                    "data": {
                                        "diff": [
                                                                                        { "f12": "000001", "f6": 100 },
                                                                                        { "f12": "399001", "f6": 200 }
                                        ]
                                    }
                                }
                                """);
                        }

                        throw new InvalidOperationException($"Unexpected request: {url}");
                });

                using var httpClient = new HttpClient(handler);
                var client = new EastmoneySectorRotationClient(httpClient);

                var snapshot = await client.GetMarketBreadthAsync(200);

                Assert.Equal(300m, snapshot.TotalTurnover);
                Assert.Equal(1, handler.CountRequests(["/api/qt/ulist.np/get", "secids=1.000001,0.399001"]));
        }

        [Fact]
        public async Task GetMarketBreadthAsync_FallsBackWhenUlistTurnoverFails()
        {
                var handler = new RouteHttpMessageHandler(request =>
                {
                        var url = request.RequestUri?.ToString() ?? string.Empty;
                        if (url.Contains("api/qt/clist/get", StringComparison.OrdinalIgnoreCase)
                                && url.Contains("fields=f12,f3,f6", StringComparison.OrdinalIgnoreCase))
                        {
                                return Json("""
                                {
                                    "data": {
                                        "total": 2,
                                        "diff": [
                                            { "f12": "000001", "f3": 1.5, "f6": 10 },
                                            { "f12": "000002", "f3": -0.2, "f6": 20 }
                                        ]
                                    }
                                }
                                """);
                        }

                        if (url.Contains("api/qt/ulist.np/get", StringComparison.OrdinalIgnoreCase)
                                && url.Contains("secids=1.000001,0.399001", StringComparison.OrdinalIgnoreCase))
                        {
                                throw new HttpRequestException("ulist unavailable");
                        }

                        throw new InvalidOperationException($"Unexpected request: {url}");
                });

                using var httpClient = new HttpClient(handler);
                var client = new EastmoneySectorRotationClient(httpClient);

                var snapshot = await client.GetMarketBreadthAsync(200);

                Assert.Equal(30m, snapshot.TotalTurnover);
                Assert.Equal(1, handler.CountRequests(["/api/qt/ulist.np/get", "secids=1.000001,0.399001"]));
        }

        [Fact]
        public async Task GetMarketBreadthAsync_RecordsTurnoverSourceFailureWhenEarlyPathThrows()
        {
                ResetDataSourceTrackerSources();
                try
                {
                        var handler = new RouteHttpMessageHandler(request =>
                        {
                                var url = request.RequestUri?.ToString() ?? string.Empty;
                                if (url.Contains("api/qt/clist/get", StringComparison.OrdinalIgnoreCase)
                                        && url.Contains("fields=f12,f3,f6", StringComparison.OrdinalIgnoreCase))
                                {
                                        throw new HttpRequestException("market breadth unavailable");
                                }

                                throw new InvalidOperationException($"Unexpected request: {url}");
                        });

                        using var httpClient = new HttpClient(handler);
                        var client = new EastmoneySectorRotationClient(httpClient);

                        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetMarketBreadthAsync(200));
                        var source = DataSourceTracker.GetAll().SingleOrDefault(item => item.Name == "eastmoney_market_fs_sh_sz");

                        Assert.Equal("market breadth unavailable", exception.Message);
                        Assert.NotNull(source);
                        Assert.Equal("error", source!.Status);
                        Assert.Contains("market breadth unavailable", source.LastError, StringComparison.OrdinalIgnoreCase);
                        Assert.Equal(1, source.ConsecutiveFailures);
                        Assert.Equal(0, handler.CountRequests(["/api/qt/ulist.np/get", "secids=1.000001,0.399001"]));
                }
                finally
                {
                        ResetDataSourceTrackerSources();
                }
        }

        [Theory]
        [InlineData(SectorBoardTypes.Industry, "m:90+s:4")]
        [InlineData(SectorBoardTypes.Concept, "m:90+t:3")]
        [InlineData(SectorBoardTypes.Style, "m:90+t:1")]
        public void NormalizeBoardFilter_UsesBkzjMappings(string boardType, string expected)
        {
                var result = EastmoneySectorRotationClient.NormalizeBoardFilter(boardType);

        Assert.Equal(expected, result);
    }

        private static HttpResponseMessage Json(string payload)
        {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
        }

        private static void ResetDataSourceTrackerSources()
        {
                var field = typeof(EastmoneySectorRotationClient)
                        .Assembly
                        .GetType("SimplerJiangAiAgent.Api.Modules.Market.DataSourceTracker")?
                        .GetField("Sources", BindingFlags.Static | BindingFlags.NonPublic);
                var sources = field?.GetValue(null);
                var clearMethod = sources?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public);
                clearMethod?.Invoke(sources, null);
        }

        private sealed class RouteHttpMessageHandler : HttpMessageHandler
        {
                private readonly Func<HttpRequestMessage, HttpResponseMessage> _router;
                private readonly List<string> _urls = [];

                public RouteHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> router)
                {
                        _router = router;
                }

                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                        _urls.Add(request.RequestUri?.ToString() ?? string.Empty);
                        var response = _router(request);
                        return Task.FromResult(response);
                }

                public int CountRequests(string token)
                {
                        return _urls.Count(url => url.Contains(token, StringComparison.OrdinalIgnoreCase));
                }

                public int CountRequests(IReadOnlyList<string> tokens)
                {
                        return _urls.Count(url => tokens.All(token => url.Contains(token, StringComparison.OrdinalIgnoreCase)));
                }
        }
}