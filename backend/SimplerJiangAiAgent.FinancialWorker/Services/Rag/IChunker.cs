using SimplerJiangAiAgent.FinancialWorker.Models;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public interface IChunker
{
    List<FinancialChunk> Chunk(PdfFileDocument document);
}
