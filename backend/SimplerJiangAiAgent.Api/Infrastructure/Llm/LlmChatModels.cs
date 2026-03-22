namespace SimplerJiangAiAgent.Api.Infrastructure.Llm;

public sealed record LlmChatRequest(string Prompt, string? Model, double? Temperature, bool UseInternet = false, string? TraceId = null);

public sealed record LlmChatResult(string Content, string? TraceId = null);
