using LiteDB;
using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Data;

/// <summary>
/// LiteDB 数据库上下文，管理所有财务数据集合
/// </summary>
public class FinancialDbContext : IDisposable
{
    private readonly LiteDatabase _db;
    
    public FinancialDbContext(string connectionString)
    {
        _db = new LiteDatabase(connectionString);
        EnsureIndexes();
    }
    
    public ILiteCollection<FinancialReport> Reports => _db.GetCollection<FinancialReport>("financial_reports");
    public ILiteCollection<FinancialIndicator> Indicators => _db.GetCollection<FinancialIndicator>("financial_indicators");
    public ILiteCollection<DividendRecord> Dividends => _db.GetCollection<DividendRecord>("dividends");
    public ILiteCollection<MarginTradingRecord> MarginTrading => _db.GetCollection<MarginTradingRecord>("margin_trading");
    public ILiteCollection<CollectionLog> Logs => _db.GetCollection<CollectionLog>("collection_logs");
    public ILiteCollection<FinancialCollectionConfig> Config => _db.GetCollection<FinancialCollectionConfig>("config");

    /// <summary>
    /// PDF 详情持久化集合（v0.4.1 §5.1）。每文档内嵌 ParseUnits 数组，
    /// 含 page_start / page_end / block_kind 三字段（v0.4.1 §9.1 硬约束）。
    /// </summary>
    public ILiteCollection<PdfFileDocument> PdfFiles => _db.GetCollection<PdfFileDocument>("pdf_files");

    private void EnsureIndexes()
    {
        // 财务报表：按 Symbol + ReportDate 唯一
        Reports.EnsureIndex(x => x.Symbol);
        Reports.EnsureIndex(x => x.ReportDate);
        Reports.EnsureIndex("Symbol_ReportDate", BsonExpression.Create("$.Symbol + '_' + $.ReportDate"), unique: true);
        
        // 财务指标：按 Symbol + ReportDate 唯一
        Indicators.EnsureIndex(x => x.Symbol);
        Indicators.EnsureIndex(x => x.ReportDate);
        Indicators.EnsureIndex("Symbol_ReportDate", BsonExpression.Create("$.Symbol + '_' + $.ReportDate"), unique: true);
        
        // 分红：按 Symbol 索引
        Dividends.EnsureIndex(x => x.Symbol);
        Dividends.EnsureIndex(x => x.RecordDate);
        
        // 融资融券：按 Symbol + TradeDate 唯一
        MarginTrading.EnsureIndex(x => x.Symbol);
        MarginTrading.EnsureIndex(x => x.TradeDate);
        MarginTrading.EnsureIndex("Symbol_TradeDate", BsonExpression.Create("$.Symbol + '_' + $.TradeDate"), unique: true);
        
        // 采集日志：按时间和Symbol索引
        Logs.EnsureIndex(x => x.Timestamp);
        Logs.EnsureIndex(x => x.Symbol);

        // PDF 详情：按 Symbol、LocalPath 索引；Symbol+LocalPath 唯一（一份 PDF 只持久化一条）。
        PdfFiles.EnsureIndex(x => x.Symbol);
        PdfFiles.EnsureIndex(x => x.LocalPath);
        PdfFiles.EnsureIndex("Symbol_LocalPath", BsonExpression.Create("$.Symbol + '_' + $.LocalPath"), unique: true);
    }
    
    /// <summary>获取数据库文件大小（字节）</summary>
    public long GetDatabaseSize()
    {
        var info = _db.GetCollection("$database").FindAll().FirstOrDefault();
        return info?["dataFileLength"].AsInt64 ?? 0;
    }
    
    public void Dispose()
    {
        _db.Dispose();
    }
}
