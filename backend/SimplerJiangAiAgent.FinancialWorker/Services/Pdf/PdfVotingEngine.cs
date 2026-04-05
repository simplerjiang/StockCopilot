using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Pdf;

/// <summary>
/// 三路 PDF 提取投票引擎
/// </summary>
public class PdfVotingEngine
{
    private readonly IEnumerable<IPdfTextExtractor> _extractors;
    private readonly ILogger<PdfVotingEngine> _logger;

    public PdfVotingEngine(IEnumerable<IPdfTextExtractor> extractors, ILogger<PdfVotingEngine> logger)
    {
        _extractors = extractors;
        _logger = logger;
    }

    /// <summary>
    /// 执行三路提取并投票选择最佳结果
    /// </summary>
    public async Task<PdfVotingResult> ExtractAndVoteAsync(string pdfFilePath, CancellationToken ct = default)
    {
        var extractions = new List<PdfExtractionResult>();

        foreach (var extractor in _extractors)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await extractor.ExtractAsync(pdfFilePath, ct);
                extractions.Add(result);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "提取器 {Name} 异常", extractor.Name);
                extractions.Add(new PdfExtractionResult
                {
                    ExtractorName = extractor.Name,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        var successful = extractions.Where(e => e.Success && e.Pages.Count > 0).ToList();

        if (successful.Count == 0)
        {
            _logger.LogError("所有提取器均失败: {File}", pdfFilePath);
            return new PdfVotingResult
            {
                Winner = null,
                AllExtractions = extractions,
                Confidence = VotingConfidence.AllFailed,
                Notes = "所有提取器均失败"
            };
        }

        if (successful.Count == 1)
        {
            return new PdfVotingResult
            {
                Winner = successful[0],
                AllExtractions = extractions,
                Confidence = VotingConfidence.SingleExtractor,
                Notes = $"仅 {successful[0].ExtractorName} 成功"
            };
        }

        // Compare text similarity between successful extractions
        var similarities = new List<(PdfExtractionResult A, PdfExtractionResult B, double Score)>();
        for (var i = 0; i < successful.Count; i++)
        {
            for (var j = i + 1; j < successful.Count; j++)
            {
                var score = ComputeTextSimilarity(successful[i].FullText, successful[j].FullText);
                similarities.Add((successful[i], successful[j], score));
            }
        }

        // All three agree (>95% similarity among all pairs)
        if (successful.Count == 3 && similarities.All(s => s.Score > 0.95))
        {
            // Prefer PdfPig when all agree (best table capabilities)
            var winner = successful.FirstOrDefault(e => e.ExtractorName == "PdfPig") ?? successful[0];
            return new PdfVotingResult
            {
                Winner = winner,
                AllExtractions = extractions,
                Confidence = VotingConfidence.Unanimous,
                Notes = $"三路一致 (similarity: {similarities.Min(s => s.Score):P1})"
            };
        }

        // Two agree, one differs
        if (successful.Count >= 2)
        {
            var bestPair = similarities.OrderByDescending(s => s.Score).First();
            if (bestPair.Score > 0.85)
            {
                // Use the one with more content from the agreeing pair
                var winner = bestPair.A.FullText.Length >= bestPair.B.FullText.Length ? bestPair.A : bestPair.B;
                return new PdfVotingResult
                {
                    Winner = winner,
                    AllExtractions = extractions,
                    Confidence = VotingConfidence.MajorityAgree,
                    Notes = $"{bestPair.A.ExtractorName}+{bestPair.B.ExtractorName} 一致 ({bestPair.Score:P1})"
                };
            }
        }

        // All differ — use PdfPig as primary (strongest table capabilities per design doc)
        var fallback = successful.FirstOrDefault(e => e.ExtractorName == "PdfPig")
                       ?? successful.OrderByDescending(e => e.FullText.Length).First();
        return new PdfVotingResult
        {
            Winner = fallback,
            AllExtractions = extractions,
            Confidence = VotingConfidence.NoConsensus,
            Notes = "三路不一致，使用 PdfPig 作为主要结果"
        };
    }

    /// <summary>
    /// 计算两段文本的相似度 (基于字符级 Jaccard 系数的变体)
    /// </summary>
    private static double ComputeTextSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

        // Use character trigram Jaccard for efficiency on large texts
        var trigramsA = GetTrigrams(a);
        var trigramsB = GetTrigrams(b);

        var intersection = trigramsA.Intersect(trigramsB).Count();
        var union = trigramsA.Union(trigramsB).Count();

        return union == 0 ? 1.0 : (double)intersection / union;
    }

    private static HashSet<string> GetTrigrams(string text)
    {
        var set = new HashSet<string>();
        // Sample every 10th trigram for performance on long PDFs
        var step = text.Length > 10000 ? 10 : 1;
        for (var i = 0; i < text.Length - 2; i += step)
        {
            set.Add(text.Substring(i, 3));
        }
        return set;
    }
}

public class PdfVotingResult
{
    public PdfExtractionResult? Winner { get; set; }
    public List<PdfExtractionResult> AllExtractions { get; set; } = new();
    public VotingConfidence Confidence { get; set; }
    public string Notes { get; set; } = "";
}

public enum VotingConfidence
{
    AllFailed,
    SingleExtractor,
    Unanimous,
    MajorityAgree,
    NoConsensus
}
