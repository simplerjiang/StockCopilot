namespace SimplerJiangAiAgent.Api.Data.Entities;

public sealed class BacktestResult
{
    public long Id { get; set; }
    public long AnalysisHistoryId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly AnalysisDate { get; set; }

    // 预测信号（从 AnalysisHistory.ResultJson 提取）
    public string PredictedDirection { get; set; } = string.Empty; // 偏多/偏空/中性
    public int Confidence { get; set; }
    public decimal? TargetPrice { get; set; }
    public decimal? StopLoss { get; set; }

    // 多窗口实际涨跌幅（%）
    public decimal? Window1dActual { get; set; }
    public decimal? Window3dActual { get; set; }
    public decimal? Window5dActual { get; set; }
    public decimal? Window10dActual { get; set; }

    // 多窗口预测是否正确
    public bool? IsCorrect1d { get; set; }
    public bool? IsCorrect3d { get; set; }
    public bool? IsCorrect5d { get; set; }
    public bool? IsCorrect10d { get; set; }

    // 目标价/止损价验证
    public bool? TargetHit { get; set; }     // 目标价是否在 10 日内触达
    public bool? StopTriggered { get; set; }  // 止损价是否在 10 日内触发

    // 状态
    public string CalcStatus { get; set; } = "pending"; // pending / calculated / insufficient_data

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
