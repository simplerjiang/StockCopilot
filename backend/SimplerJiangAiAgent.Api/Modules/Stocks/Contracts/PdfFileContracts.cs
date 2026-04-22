namespace SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;

/// <summary>
/// v0.4.1 §S2：PDF 文件列表项 DTO（不含 ParseUnits，仅元数据 + StageLogs 摘要）。
/// </summary>
public sealed record PdfFileListItem(
    string Id,
    string Symbol,
    string FileName,
    string Title,
    string ReportPeriod,
    string ReportType,
    string? Extractor,
    string? VoteConfidence,
    int FieldCount,
    DateTime? LastParsedAt,
    DateTime? LastReparsedAt,
    string? LastError,
    string AccessKey,
    IReadOnlyList<PdfStageLogDto> StageLogs);

/// <summary>v0.4.1 §S2：PDF 文件详情 DTO（含 ParseUnits 三字段）。</summary>
public sealed record PdfFileDetail(
    string Id,
    string Symbol,
    string FileName,
    string Title,
    string ReportPeriod,
    string ReportType,
    string? Extractor,
    string? VoteConfidence,
    int FieldCount,
    DateTime? LastParsedAt,
    DateTime? LastReparsedAt,
    string? LastError,
    string AccessKey,
    IReadOnlyList<PdfParseUnitDto> ParseUnits,
    IReadOnlyList<PdfStageLogDto> StageLogs);

/// <summary>
/// v0.4.1 §9.1 硬约束：PageStart / PageEnd / BlockKind 三字段必须非空。
/// </summary>
public sealed record PdfParseUnitDto(
    string BlockKind,
    int PageStart,
    int PageEnd,
    string? SectionName,
    int FieldCount,
    string? Snippet);

public sealed record PdfStageLogDto(
    string Stage,
    string Status,
    long ElapsedMs,
    string? Message,
    DateTime Timestamp);

public sealed record PdfFileListQuery(
    string? Symbol,
    string? ReportType,
    int Page = 1,
    int PageSize = 20);

/// <summary>
/// v0.4.1 §S2：Reparse 接口响应。失败时 Success=false 但 HTTP 仍 200，业务错误下沉。
/// </summary>
public sealed record PdfFileReparseResponse(
    bool Success,
    string? Error,
    PdfFileDetail? Detail);
