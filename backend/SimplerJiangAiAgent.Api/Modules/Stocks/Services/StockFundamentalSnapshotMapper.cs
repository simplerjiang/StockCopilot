using System.Text.Json;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class StockFundamentalSnapshotMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string? SerializeFacts(IReadOnlyList<StockFundamentalFactDto>? facts)
    {
        if (facts is null || facts.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(facts, SerializerOptions);
    }

    public static IReadOnlyList<StockFundamentalFactDto> DeserializeFacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<StockFundamentalFactDto>();
        }

        var facts = JsonSerializer.Deserialize<List<StockFundamentalFactDto>>(json, SerializerOptions);
        return facts is null ? Array.Empty<StockFundamentalFactDto>() : facts;
    }

    public static StockFundamentalSnapshotDto? FromProfile(StockCompanyProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        var facts = DeserializeFacts(profile.FundamentalFactsJson);
        if (facts.Count == 0)
        {
            return null;
        }

        return new StockFundamentalSnapshotDto(profile.FundamentalUpdatedAt ?? profile.UpdatedAt, facts);
    }
}
