namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public interface IChineseTokenizer
{
    /// <summary>
    /// Segment Chinese text into space-joined tokens for FTS5 indexing.
    /// </summary>
    string Tokenize(string text);
}
