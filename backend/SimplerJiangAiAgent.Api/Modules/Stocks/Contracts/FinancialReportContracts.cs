namespace SimplerJiangAiAgent.Api.Modules.Stocks.Contracts;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record FinancialReportListItem(
    string Id,
    string Symbol,
    string ReportDate,
    string ReportType,
    string? SourceChannel,
    DateTime? CollectedAt,
    DateTime? UpdatedAt);

public sealed record FinancialReportDetail(
    string Id,
    string Symbol,
    string ReportDate,
    string ReportType,
    int CompanyType,
    string? SourceChannel,
    DateTime? CollectedAt,
    DateTime? UpdatedAt,
    Dictionary<string, object?> BalanceSheet,
    Dictionary<string, object?> IncomeStatement,
    Dictionary<string, object?> CashFlow);

public sealed record FinancialReportListQuery(
    string? Symbol,
    string? ReportType,
    string? StartDate,
    string? EndDate,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20,
    string Sort = "reportDate:desc");
