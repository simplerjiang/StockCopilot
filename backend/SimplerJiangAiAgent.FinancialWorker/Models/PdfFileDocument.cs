using LiteDB;

namespace SimplerJiangAiAgent.FinancialWorker.Models;

/// <summary>
/// 解析单元类型（v0.4.1 §9.1 硬约束）。
/// </summary>
public enum PdfBlockKind
{
    /// <summary>叙述性段落（含报表正文章节）。</summary>
    NarrativeSection = 0,

    /// <summary>表格（资产负债表/利润表/现金流量表/明细表等）。</summary>
    Table = 1,

    /// <summary>图表说明 / 图注。</summary>
    FigureCaption = 2,
}

/// <summary>
/// PDF 解析单元（chunk / section / table）。
/// v0.4.1 §9.1 硬约束：page_start / page_end / block_kind 三字段不允许为空，
/// page_start / page_end 必须为 1-based 页码，且 page_end &gt;= page_start。
/// </summary>
public class PdfParseUnit
{
    /// <summary>解析单元类型。</summary>
    public PdfBlockKind BlockKind { get; set; }

    /// <summary>1-based 起始页码。0 视为缺页降级，落库前必须拒收。</summary>
    public int PageStart { get; set; }

    /// <summary>1-based 结束页码（含）。必须 &gt;= <see cref="PageStart"/>。</summary>
    public int PageEnd { get; set; }

    /// <summary>区段名称（例如 BalanceSheet / IncomeStatement / CashFlowStatement）。</summary>
    public string? SectionName { get; set; }

    /// <summary>该单元解析出的字段数量（针对 Table 区段）。</summary>
    public int FieldCount { get; set; }

    /// <summary>该单元的简短文本片段（用于前端预览，可选）。</summary>
    public string? Snippet { get; set; }

    /// <summary>校验三字段是否合法。</summary>
    public bool IsValid =>
        PageStart >= 1 &&
        PageEnd >= PageStart &&
        Enum.IsDefined(typeof(PdfBlockKind), BlockKind);
}

/// <summary>
/// PDF 详情持久化模型（v0.4.1 §5.1）。
/// 一个文档对应单个 PDF 文件，内嵌该文件的全部解析单元。
/// </summary>
public class PdfFileDocument
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    /// <summary>股票代码。</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>PDF 文件名（含扩展名）。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>公告标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>本地绝对路径。</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>对外可访问标识（建议使用 PDF 文件相对路径或哈希；接口层会用它建链接）。</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>报告期，如 "2024-12-31"。</summary>
    public string ReportPeriod { get; set; } = string.Empty;

    /// <summary>报告类型：Annual / Q1 / Q2 / Q3 / Unknown。</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>采用的提取器名称（PdfPig / iText7 / Docnet）。</summary>
    public string? Extractor { get; set; }

    /// <summary>投票置信度（VotingConfidence 枚举字符串）。</summary>
    public string? VoteConfidence { get; set; }

    /// <summary>三主表合计字段数量。</summary>
    public int FieldCount { get; set; }

    /// <summary>最近一次错误信息（成功时为 null）。</summary>
    public string? LastError { get; set; }

    /// <summary>最近一次解析时间（首次落库时间）。</summary>
    public DateTime LastParsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近一次重新解析时间（首次落库时为 null）。</summary>
    public DateTime? LastReparsedAt { get; set; }

    /// <summary>解析单元数组（chunk / section / table），含三字段。</summary>
    public List<PdfParseUnit> ParseUnits { get; set; } = new();

    /// <summary>
    /// 阶段日志（v0.4.1 §5.3）。覆盖 5 阶段：download / extract / vote / parse / persist。
    /// 每次解析或重新解析会整体覆盖该数组（仅保留最近一次）。
    /// </summary>
    public List<PdfStageLog> StageLogs { get; set; } = new();
}

/// <summary>
/// PDF 处理管线阶段日志（v0.4.1 §5.3）。
/// 每条记录某一阶段（download/extract/vote/parse/persist）的执行情况。
/// </summary>
public class PdfStageLog
{
    /// <summary>阶段名：download / extract / vote / parse / persist。</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>状态：success / failed / skipped。</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>耗时（毫秒）。skipped 阶段为 0。</summary>
    public long ElapsedMs { get; set; }

    /// <summary>错误摘要（success/skipped 阶段为 null）。</summary>
    public string? Message { get; set; }

    /// <summary>记录时间（UTC）。</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
