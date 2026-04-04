# Ollama 本地模型集成开发指南

## 1. 环境现状

### 已完成部署
- **Ollama**: v0.20.0+, 已安装并运行
- **模型**: gemma4:e4b (9.6GB)
- **存储**: D:\ollama-models (环境变量 OLLAMA_MODELS, User级别)
- **API端点**: http://localhost:11434/v1/chat/completions (OpenAI兼容)
- **硬件**: RTX 5060 8GB GDDR7, CUDA 13.2

### Ollama 版本要求

Gemma 4 需要 Ollama **v0.20.0** 或更高版本。旧版本无法识别 gemma4 模型格式。

升级命令:
```powershell
winget install Ollama.Ollama --force --accept-package-agreements --accept-source-agreements
```

### 代理配置

如果网络需要代理，启动 Ollama 前设置：
```powershell
$env:HTTPS_PROXY = "http://127.0.0.1:10808"
$env:HTTP_PROXY = "http://127.0.0.1:10808"
```

### 性能基线 (2026-04-04 实测)
| 指标 | 值 |
|------|------|
| 12条新闻清洗耗时 | 25.3秒 |
| 推理速度 | 24.6 tok/s |
| GPU显存占用 | 55% (4.5GB/8.1GB) |
| Sentiment合规率 | 100% |
| Tags合规率 | 100% |
| JSON格式 | 无需处理，直接输出纯JSON |

---

## 2. 后端集成步骤

### 2.1 新建 OllamaProvider

路径: `backend/SimplerJiangAiAgent.Api/Infrastructure/Llm/OllamaProvider.cs`

**接口需实现:**
```csharp
public interface ILlmProvider
{
    string Name { get; }
    Task<LlmChatResult> ChatAsync(LlmProviderSettings settings, LlmChatRequest request, CancellationToken cancellationToken = default);
}
```

**关键设计要点:**
- `Name` 返回 `"ollama"`
- 不需要 ApiKey 验证（本地服务无需认证）
- BaseUrl 默认 `http://localhost:11434`
- 调用 `/v1/chat/completions` 端点（OpenAI 兼容格式）
- 请求体结构与 OpenAI 相同: `{ model, messages, temperature, stream }`
- model 默认 `gemma4:e4b`，从 settings.Model 或 request.Model 读取

**参考实现骨架:**
```csharp
public sealed class OllamaProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly IFileLogWriter _fileLogWriter;

    public OllamaProvider(HttpClient httpClient, IFileLogWriter fileLogWriter)
    {
        _httpClient = httpClient;
        _fileLogWriter = fileLogWriter;
    }

    public string Name => "ollama";

    public async Task<LlmChatResult> ChatAsync(
        LlmProviderSettings settings, LlmChatRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) 
            ? "http://localhost:11434" 
            : settings.BaseUrl.TrimEnd('/');

        var model = string.IsNullOrWhiteSpace(request.Model) 
            ? (string.IsNullOrWhiteSpace(settings.Model) ? "gemma4:e4b" : settings.Model) 
            : request.Model;

        var messages = new List<object>();
        // system prompt 从 settings.SystemPrompt 或 request 中获取
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
        {
            messages.Add(new { role = "system", content = settings.SystemPrompt });
        }
        messages.Add(new { role = "user", content = request.Prompt });

        var payload = new
        {
            model,
            messages,
            temperature = request.Temperature ?? 0.3,  // 本地模型默认低温度保证格式稳定
            stream = false
        };

        var url = $"{baseUrl}/v1/chat/completions";
        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ollama 请求失败: {response.StatusCode} {responseText}");
        }

        using var doc = JsonDocument.Parse(responseText);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
        {
            return new LlmChatResult(string.Empty);
        }

        var content = choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        return new LlmChatResult(content.Trim());
    }
}
```

### 2.2 DI 注册

文件: `backend/SimplerJiangAiAgent.Api/Modules/Llm/LlmModule.cs`

在 `Register` 方法中添加:
```csharp
// Ollama 本地模型 - 较长超时（本地推理慢）
services.AddHttpClient<OllamaProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(300);  // 本地12B模型可能需要更长时间
});
services.AddSingleton<ILlmProvider, OllamaProvider>();
```

**注意:** Ollama 超时应设为 300 秒（5分钟），因为本地模型处理 12 条新闻需要约 25 秒，大批量可能更久。

### 2.3 新闻清洗适配

文件: `backend/SimplerJiangAiAgent.Api/Infrastructure/Jobs/StockSyncOptions.cs`

当前默认配置:
```csharp
public string AiProvider { get; set; } = "active";
public string AiModel { get; set; } = "gpt-4.1-nano";
public int AiBatchSize { get; set; } = 12;
```

用户若选择 Ollama 进行清洗:
- `AiProvider` 设为 `"ollama"` (自定义渠道名)
- `AiModel` 设为 `"gemma4:e4b"`
- `AiBatchSize` 保持 **12**（Gemma 4 速度更快，显存占用更低）

文件: `backend/SimplerJiangAiAgent.Api/Infrastructure/Jobs/LocalFactAiEnrichmentService.cs`

**并发限制修改:** 当 provider 为 ollama 时，`maxConcurrency` 从 3 降为 **1**:
```csharp
// const int maxConcurrency = 3;  // 原逻辑
var maxConcurrency = isOllamaProvider ? 1 : 3;  // 本地GPU只能串行
```

### 2.4 LLM 设置存储

文件: `App_Data/llm-settings.json`

新增 ollama 渠道配置示例:
```json
{
  "ActiveProviderKey": "active",
  "Providers": {
    "ollama": {
      "Provider": "ollama",
      "ProviderType": "ollama",
      "ApiKey": "",
      "BaseUrl": "http://localhost:11434",
      "Model": "gemma4:e4b",
      "SystemPrompt": "",
      "ForceChinese": false,
      "Enabled": true
    }
  }
}
```

**安全提醒:** `ApiKey` 留空，Ollama 本地不需要认证。不要往里填任何密钥。

---

## 3. 前端集成步骤

### 3.1 LLM 设置页面

需要在 LLM 设置页面支持新增 `ollama` 渠道:
- Provider Type 下拉增加 `"ollama"` 选项
- 当选择 ollama 时:
  - ApiKey 字段隐藏或标为"可选"
  - BaseUrl 默认填入 `http://localhost:11434`
  - Model 默认填入 `gemma4:e4b`
  - 增加"测试连接"按钮，调后端 `/api/admin/llm/test/ollama`

### 3.2 新闻清洗渠道选择

如果后续实现"全量资讯库"页面的手动清洗功能(buglist 已列需求):
- 允许用户选择清洗使用的 provider (active / ollama)
- 显示预估耗时（基于待清洗条数 × 25秒/12条 的公式）

---

## 4. 已知问题和解决方案

### 4.1 Markdown Fence 包裹
**问题:** Gemma 4 已不再输出 markdown fence，此问题已消除。但保留 fence strip 逻辑作为防御性编程。

**解决:** 后端已有 `TryCleanJsonOutput` 方法（在 `RecommendationRoleExecutor.cs` 中），以及 `LocalFactAiEnrichmentService` 的 JSON 解析逻辑已经处理了 fence strip。**代码保留不动，作为防御性兼容。**

### 4.2 显存占用
**现状:** Gemma 4 e4b 仅占用 55% 显存（4.5GB），显存压力大幅降低。仍建议配置空闲卸载。

**建议:** 配置 Ollama 空闲卸载:
```bash
# 设置空闲 5 分钟后自动卸载模型
set OLLAMA_KEEP_ALIVE=5m
```
或在 LlmModule 注册时给 OllamaProvider 加一个预热/空闲管理机制。

### 4.3 中文标题不返回 null
**问题:** 模型对已是中文的标题也返回了 translatedTitle（应为 null）

**影响:** 不违规，只是多消耗 token。可接受，无需修复。

### 4.4 并发限制
**问题:** 单 GPU 无法并行推理，3 路并发会排队

**解决:** OllamaProvider 或 LocalFactAiEnrichmentService 检测到 provider 类型为 ollama 时，限制 maxConcurrency=1。

---

## 5. 配置优先级和切换逻辑

### 推荐架构
```
用户在 LLM 设置中配置多个渠道:
├── default (Antigravity/OpenAI 云端)  ← 默认 active
├── ollama (本地 Gemma)               ← 可手动切换
└── ...

新闻清洗的 AiProvider:
├── "active" → 使用当前 active 渠道
├── "ollama" → 强制使用本地 Ollama
└── 具体渠道名 → 使用指定渠道
```

### StockSyncOptions 配置
在 `appsettings.json` 或 `appsettings.Development.json` 中:
```json
{
  "StockSync": {
    "AiProvider": "ollama",
    "AiModel": "gemma4:e4b", 
    "AiBatchSize": 12
  }
}
```

---

## 6. 验证清单

集成完成后需通过以下验证:

- [ ] `ollama list` 确认 gemma4:e4b 可用
- [ ] 后端启动时 DI 正确注入 OllamaProvider
- [ ] `/api/admin/llm/test/ollama` 接口返回有效响应
- [ ] LLM 设置页面能新建/编辑 ollama 渠道
- [ ] 新闻清洗切换到 ollama provider 后能正常处理
- [ ] JSON 解析正确（markdown fence 已 strip）
- [ ] aiSentiment、aiTags 合规（与云端模型对比）
- [ ] 并发限制为 1（不并行推理）
- [ ] 空闲 5 分钟后显存释放
- [ ] 后端测试通过 `dotnet test`

## 7. Ollama 常用命令

```bash
# 启动服务
ollama serve

# 查看已安装模型
ollama list

# 拉取新模型
ollama pull gemma4:e4b

# 交互式对话
ollama run gemma4:e4b

# 查看运行中模型
ollama ps

# 删除模型
ollama rm gemma4:e4b

# 查看模型信息
ollama show gemma4:e4b
```

## 8. 相关代码路径速查

| 文件 | 用途 |
|------|------|
| `Infrastructure/Llm/ILlmProvider.cs` | Provider 接口定义 |
| `Infrastructure/Llm/OpenAiProvider.cs` | 现有 OpenAI provider（参考模板） |
| `Infrastructure/Llm/LlmProviderSettings.cs` | 配置结构体 |
| `Infrastructure/Llm/LlmService.cs` | Provider 路由和解析 |
| `Infrastructure/Llm/LlmChatModels.cs` | Request/Result record 定义 |
| `Modules/Llm/LlmModule.cs` | DI 注册 + API 端点 |
| `Infrastructure/Jobs/LocalFactAiEnrichmentService.cs` | 新闻清洗服务 |
| `Infrastructure/Jobs/StockSyncOptions.cs` | 清洗批量配置 |
| `App_Data/llm-settings.json` | 运行时 LLM 设置（不提交到 git） |
