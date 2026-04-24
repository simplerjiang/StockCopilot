using JiebaNet.Segmenter;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public class JiebaTokenizer : IChineseTokenizer
{
    private readonly JiebaSegmenter _segmenter;

    public JiebaTokenizer()
    {
        _segmenter = new JiebaSegmenter();
    }

    public string Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var words = _segmenter.Cut(text, cutAll: false);
        // Filter out whitespace-only tokens and join with spaces
        return string.Join(" ", words.Where(w => !string.IsNullOrWhiteSpace(w)));
    }
}
