using LiteDB;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

/// <summary>
/// v0.4.1 §S2：PDF 详情读取与文件路径沙箱校验服务。
/// 直接读取 FinancialWorker 写入的 LiteDB pdf_files 集合（共享只读连接）。
/// </summary>
public interface IPdfFileQueryService : IDisposable
{
    PagedResult<PdfFileListItem> List(PdfFileListQuery query);
    PdfFileDetail? GetById(string id);

    /// <summary>
    /// 解析 PDF 物理文件，返回沙箱校验结果。
    /// status：found / not_found / forbidden / db_unavailable。
    /// 调用方根据 status 决定 200/403/404 响应。
    /// </summary>
    PdfFileContentResolution ResolveContent(string id);
}

/// <summary>v0.4.1 §S2：content 接口的沙箱校验结果。</summary>
public sealed class PdfFileContentResolution
{
    public string Status { get; init; } = "not_found";
    public string? FullPath { get; init; }
    public string? AccessKey { get; init; }
}

public class PdfFileQueryService : IPdfFileQueryService
{
    private readonly LiteDatabase? _db;
    private readonly ILogger<PdfFileQueryService> _logger;
    private readonly bool _available;
    private readonly string _reportsRootFullPath;

    public PdfFileQueryService(AppRuntimePaths runtimePaths, ILogger<PdfFileQueryService> logger)
    {
        _logger = logger;
        var dbPath = Path.Combine(runtimePaths.AppDataPath, "financial-data.db");
        _reportsRootFullPath = Path.GetFullPath(Path.Combine(runtimePaths.AppDataPath, "financial-reports"));

        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Financial LiteDB not found at {Path}, PDF query service running in unavailable mode", dbPath);
            _available = false;
            return;
        }

        try
        {
            // 写入由 FinancialWorker 进程负责，这里只读
            _db = new LiteDatabase($"Filename={dbPath};Connection=shared;ReadOnly=true");
            _available = true;
            _logger.LogInformation("PdfFileQueryService opened LiteDB (read-only): {Path}", dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Financial LiteDB at {Path} for PdfFileQueryService", dbPath);
            _available = false;
        }
    }

    public PagedResult<PdfFileListItem> List(PdfFileListQuery query)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        if (!_available)
        {
            return new PagedResult<PdfFileListItem>(Array.Empty<PdfFileListItem>(), 0, page, pageSize);
        }

        var col = _db!.GetCollection("pdf_files");

        var parts = new List<string>();
        var pars = new BsonDocument();
        var idx = 0;

        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            pars[$"p{idx}"] = query.Symbol.Trim();
            parts.Add($"$.Symbol = @p{idx}");
            idx++;
        }
        if (!string.IsNullOrWhiteSpace(query.ReportType))
        {
            pars[$"p{idx}"] = query.ReportType.Trim();
            parts.Add($"$.ReportType = @p{idx}");
            idx++;
        }

        BsonExpression? predicate = parts.Count == 0
            ? null
            : BsonExpression.Create(string.Join(" AND ", parts), pars);

        var total = predicate is null ? col.Count() : col.Count(predicate);

        var queryable = col.Query();
        if (predicate is not null)
        {
            queryable = queryable.Where(predicate);
        }

        // 默认按 LastParsedAt desc
        var docs = queryable
            .OrderByDescending(BsonExpression.Create("$.LastParsedAt"))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToList();

        var items = docs.Select(MapListItem).ToList();
        return new PagedResult<PdfFileListItem>(items, total, page, pageSize);
    }

    public PdfFileDetail? GetById(string id)
    {
        if (!_available || string.IsNullOrWhiteSpace(id)) return null;
        if (!TryParseObjectId(id, out var objectId)) return null;

        var col = _db!.GetCollection("pdf_files");
        var doc = col.FindById(new BsonValue(objectId));
        if (doc == null) return null;
        return MapDetail(doc);
    }

    public PdfFileContentResolution ResolveContent(string id)
    {
        if (!_available)
        {
            return new PdfFileContentResolution { Status = "db_unavailable" };
        }
        if (string.IsNullOrWhiteSpace(id) || !TryParseObjectId(id, out var objectId))
        {
            return new PdfFileContentResolution { Status = "not_found" };
        }

        var col = _db!.GetCollection("pdf_files");
        var doc = col.FindById(new BsonValue(objectId));
        if (doc == null)
        {
            return new PdfFileContentResolution { Status = "not_found" };
        }

        var localPath = SafeString(doc, "LocalPath");
        var accessKey = SafeString(doc, "AccessKey");
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return new PdfFileContentResolution { Status = "not_found", AccessKey = accessKey };
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(localPath);
        }
        catch
        {
            return new PdfFileContentResolution { Status = "forbidden", AccessKey = accessKey };
        }

        // 沙箱：必须落在 financial-reports 根目录之内
        if (!fullPath.StartsWith(_reportsRootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return new PdfFileContentResolution { Status = "forbidden", AccessKey = accessKey };
        }

        if (!File.Exists(fullPath))
        {
            return new PdfFileContentResolution { Status = "not_found", AccessKey = accessKey };
        }

        return new PdfFileContentResolution { Status = "found", FullPath = fullPath, AccessKey = accessKey };
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    // ── helpers ──

    private static bool TryParseObjectId(string id, out ObjectId objectId)
    {
        try
        {
            objectId = new ObjectId(id);
            return true;
        }
        catch
        {
            objectId = ObjectId.Empty;
            return false;
        }
    }

    private static PdfFileListItem MapListItem(BsonDocument doc)
    {
        return new PdfFileListItem(
            Id: SafeIdString(doc),
            Symbol: SafeString(doc, "Symbol"),
            FileName: SafeString(doc, "FileName"),
            Title: SafeString(doc, "Title"),
            ReportPeriod: SafeString(doc, "ReportPeriod"),
            ReportType: SafeString(doc, "ReportType"),
            Extractor: SafeNullableString(doc, "Extractor"),
            VoteConfidence: SafeNullableString(doc, "VoteConfidence"),
            FieldCount: SafeInt(doc, "FieldCount"),
            LastParsedAt: SafeNullableDateTime(doc, "LastParsedAt"),
            LastReparsedAt: SafeNullableDateTime(doc, "LastReparsedAt"),
            LastError: SafeNullableString(doc, "LastError"),
            AccessKey: SafeString(doc, "AccessKey"),
            StageLogs: ReadStageLogs(doc));
    }

    private static PdfFileDetail MapDetail(BsonDocument doc)
    {
        return new PdfFileDetail(
            Id: SafeIdString(doc),
            Symbol: SafeString(doc, "Symbol"),
            FileName: SafeString(doc, "FileName"),
            Title: SafeString(doc, "Title"),
            ReportPeriod: SafeString(doc, "ReportPeriod"),
            ReportType: SafeString(doc, "ReportType"),
            Extractor: SafeNullableString(doc, "Extractor"),
            VoteConfidence: SafeNullableString(doc, "VoteConfidence"),
            FieldCount: SafeInt(doc, "FieldCount"),
            LastParsedAt: SafeNullableDateTime(doc, "LastParsedAt"),
            LastReparsedAt: SafeNullableDateTime(doc, "LastReparsedAt"),
            LastError: SafeNullableString(doc, "LastError"),
            AccessKey: SafeString(doc, "AccessKey"),
            ParseUnits: ReadParseUnits(doc),
            StageLogs: ReadStageLogs(doc))
        {
            FullTextPages = ReadFullTextPages(doc),
            VotingCandidates = ReadVotingCandidates(doc),
            VotingNotes = SafeNullableString(doc, "VotingNotes"),
        };
    }

    private static IReadOnlyList<PdfParseUnitDto> ReadParseUnits(BsonDocument doc)
    {
        if (!doc.ContainsKey("ParseUnits") || doc["ParseUnits"].IsNull) return Array.Empty<PdfParseUnitDto>();
        var arr = doc["ParseUnits"].AsArray;
        if (arr == null) return Array.Empty<PdfParseUnitDto>();

        var list = new List<PdfParseUnitDto>(arr.Count);
        foreach (var v in arr)
        {
            if (!v.IsDocument) continue;
            var d = v.AsDocument;
            list.Add(new PdfParseUnitDto(
                BlockKind: MapBlockKind(d),
                PageStart: SafeInt(d, "PageStart"),
                PageEnd: SafeInt(d, "PageEnd"),
                SectionName: SafeNullableString(d, "SectionName"),
                FieldCount: SafeInt(d, "FieldCount"),
                Snippet: SafeNullableString(d, "Snippet"),
                ExtractedText: SafeNullableString(d, "ExtractedText"),
                ParsedFields: ReadParsedFields(d)));
        }
        return list;
    }

    private static IReadOnlyList<PdfStageLogDto> ReadStageLogs(BsonDocument doc)
    {
        if (!doc.ContainsKey("StageLogs") || doc["StageLogs"].IsNull) return Array.Empty<PdfStageLogDto>();
        var arr = doc["StageLogs"].AsArray;
        if (arr == null) return Array.Empty<PdfStageLogDto>();

        var list = new List<PdfStageLogDto>(arr.Count);
        foreach (var v in arr)
        {
            if (!v.IsDocument) continue;
            var d = v.AsDocument;
            Dictionary<string, string>? details = null;
            if (d.ContainsKey("Details") && d["Details"].IsDocument)
            {
                details = new Dictionary<string, string>();
                foreach (var kv in d["Details"].AsDocument)
                    details[kv.Key] = kv.Value?.AsString ?? "";
            }
            list.Add(new PdfStageLogDto(
                Stage: SafeString(d, "Stage"),
                Status: SafeString(d, "Status"),
                ElapsedMs: SafeLong(d, "ElapsedMs"),
                Message: SafeNullableString(d, "Message"),
                Timestamp: SafeNullableDateTime(d, "Timestamp") ?? DateTime.MinValue,
                Details: details));
        }
        return list;
    }

    private static IReadOnlyList<PdfPageTextDto> ReadFullTextPages(BsonDocument doc)
    {
        if (!doc.ContainsKey("FullTextPages") || doc["FullTextPages"].IsNull) return Array.Empty<PdfPageTextDto>();
        var arr = doc["FullTextPages"].AsArray;
        if (arr == null) return Array.Empty<PdfPageTextDto>();

        var list = new List<PdfPageTextDto>(arr.Count);
        foreach (var v in arr)
        {
            if (!v.IsDocument) continue;
            var d = v.AsDocument;
            list.Add(new PdfPageTextDto(
                PageNumber: SafeInt(d, "PageNumber"),
                Text: SafeString(d, "Text")));
        }
        return list;
    }

    private static List<VotingCandidateDto> ReadVotingCandidates(BsonDocument doc)
    {
        if (!doc.ContainsKey("VotingCandidates") || doc["VotingCandidates"].IsNull) return new();
        var arr = doc["VotingCandidates"].AsArray;
        if (arr == null) return new();

        var list = new List<VotingCandidateDto>(arr.Count);
        foreach (var v in arr)
        {
            if (!v.IsDocument) continue;
            var d = v.AsDocument;
            list.Add(new VotingCandidateDto(
                Extractor: SafeString(d, "Extractor"),
                Success: d.ContainsKey("Success") && d["Success"].AsBoolean,
                PageCount: SafeInt(d, "PageCount"),
                TextLength: SafeInt(d, "TextLength"),
                SampleText: SafeNullableString(d, "SampleText"),
                IsWinner: d.ContainsKey("IsWinner") && d["IsWinner"].AsBoolean));
        }
        return list;
    }

    private static Dictionary<string, object?>? ReadParsedFields(BsonDocument d)
    {
        if (!d.ContainsKey("ParsedFields") || d["ParsedFields"].IsNull) return null;
        var sub = d["ParsedFields"];
        if (!sub.IsDocument) return null;
        var doc = sub.AsDocument;
        var dict = new Dictionary<string, object?>(doc.Count);
        foreach (var key in doc.Keys)
        {
            var val = doc[key];
            dict[key] = val.IsNull ? null
                : val.IsNumber ? (object)val.AsDouble
                : val.IsString ? val.AsString
                : val.ToString();
        }
        return dict;
    }

    private static string MapBlockKind(BsonDocument d)
    {
        // LiteDB 序列化枚举可能为 int 或 string；统一映射到 narrative_section / table / figure_caption
        if (!d.ContainsKey("BlockKind")) return "narrative_section";
        var v = d["BlockKind"];
        int code;
        if (v.IsNumber) code = v.AsInt32;
        else if (v.IsString && int.TryParse(v.AsString, out var parsed)) code = parsed;
        else if (v.IsString)
        {
            // 直接是枚举名字符串
            return v.AsString switch
            {
                "Table" => "table",
                "FigureCaption" => "figure_caption",
                _ => "narrative_section",
            };
        }
        else code = 0;

        return code switch
        {
            1 => "table",
            2 => "figure_caption",
            _ => "narrative_section",
        };
    }

    private static string SafeIdString(BsonDocument doc)
    {
        if (!doc.ContainsKey("_id")) return string.Empty;
        var v = doc["_id"];
        if (v.IsObjectId) return v.AsObjectId.ToString();
        if (v.IsString) return v.AsString;
        return v.ToString() ?? string.Empty;
    }

    private static string SafeString(BsonDocument doc, string key)
        => doc.ContainsKey(key) && !doc[key].IsNull ? (doc[key].IsString ? doc[key].AsString : doc[key].ToString() ?? "") : string.Empty;

    private static string? SafeNullableString(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key) || doc[key].IsNull) return null;
        var v = doc[key];
        return v.IsString ? v.AsString : v.ToString();
    }

    private static int SafeInt(BsonDocument doc, string key)
        => doc.ContainsKey(key) && doc[key].IsNumber ? doc[key].AsInt32 : 0;

    private static long SafeLong(BsonDocument doc, string key)
        => doc.ContainsKey(key) && doc[key].IsNumber ? doc[key].AsInt64 : 0L;

    private static DateTime? SafeNullableDateTime(BsonDocument doc, string key)
    {
        if (!doc.ContainsKey(key) || doc[key].IsNull) return null;
        var v = doc[key];
        if (v.IsDateTime) return v.AsDateTime;
        if (v.IsString && DateTime.TryParse(v.AsString, out var dt)) return dt;
        return null;
    }
}
