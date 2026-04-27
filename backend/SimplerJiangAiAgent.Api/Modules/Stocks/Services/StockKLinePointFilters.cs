using System.Linq.Expressions;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class StockKLinePointFilters
{
    public static Expression<Func<KLinePointEntity, bool>> HasUsableHighLowEntity =>
        point => point.High != 0m || point.Low != 0m;

    public static bool HasUsableHighLow(KLinePointDto point)
    {
        return point.High != 0m || point.Low != 0m;
    }
}