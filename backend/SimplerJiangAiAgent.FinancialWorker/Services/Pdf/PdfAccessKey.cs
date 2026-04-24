using System.Security.Cryptography;
using System.Text;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// v0.4.1 §S2：基于本地路径生成稳定的 AccessKey。
/// 旧实现 AccessKey = fileName 易冲突且暴露文件名信息；
/// 新算法：SHA256(localPath) 取前 16 个 hex 字符 + ".pdf"。
/// </summary>
public static class PdfAccessKey
{
    /// <summary>
    /// 由本地绝对路径生成 16 位 hex + ".pdf" 形式的 AccessKey。
    /// localPath 为 null 或空白时返回空字符串（调用方需自行兜底）。
    /// </summary>
    public static string From(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(localPath);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++) // 8 bytes -> 16 hex chars
        {
            sb.Append(hash[i].ToString("x2"));
        }
        sb.Append(".pdf");
        return sb.ToString();
    }
}
