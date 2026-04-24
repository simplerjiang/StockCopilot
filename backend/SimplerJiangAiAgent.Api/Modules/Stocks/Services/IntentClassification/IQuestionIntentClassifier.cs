namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.IntentClassification;

public interface IQuestionIntentClassifier
{
    Task<QuestionIntent> ClassifyAsync(string question, string? stockSymbol = null, CancellationToken ct = default);
}
