using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Data.Entities;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public interface ISourceGovernanceService
{
    Task RunOnceAsync(CancellationToken cancellationToken = default);
}

public sealed class SourceGovernanceService : ISourceGovernanceService
{
    private readonly AppDbContext _dbContext;
    private readonly SourceGovernanceOptions _options;
    private readonly IFileLogWriter _fileLogWriter;
    private readonly ILlmService _llmService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICommandRunner _commandRunner;
    private readonly string _repoRoot;

    private static readonly string[] AllowedCrawlerPathPrefixes =
    {
        "backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/",
        "backend/SimplerJiangAiAgent.Api.Tests/"
    };

    private static readonly Regex TimestampPattern = new(
        @"\b(20\d{2}[-/.]?(0[1-9]|1[0-2])[-/.]?(0[1-9]|[12]\d|3[01]))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SourceGovernanceService(
        AppDbContext dbContext,
        IOptions<SourceGovernanceOptions> options,
        IFileLogWriter fileLogWriter,
        ILlmService llmService,
        IHttpClientFactory httpClientFactory,
        ICommandRunner commandRunner)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _fileLogWriter = fileLogWriter;
        _llmService = llmService;
        _httpClientFactory = httpClientFactory;
        _commandRunner = commandRunner;
        _repoRoot = string.IsNullOrWhiteSpace(_options.RepositoryRoot) ? ResolveRepositoryRoot() : _options.RepositoryRoot;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_options.EnableLlmDiscovery)
        {
            await DiscoverCandidatesFromLlmAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await VerifyPendingCandidatesAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await PromoteQualifiedCandidatesAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await RefreshSourceStatusesAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_options.EnableCrawlerAutoFix)
        {
            await EnqueueCrawlerFixesAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await GenerateCrawlerPatchProposalsAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await ValidateCrawlerPatchProposalsAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await DeployValidatedCrawlerPatchesAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await EvaluateRollbackForDeployedChangesAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DiscoverCandidatesFromLlmAsync(CancellationToken cancellationToken)
    {
        var prompt =
            "Return JSON array only. Find up to " + _options.MaxDailyDiscoveryCandidates +
            " Chinese market news source domains with fields: domain, homepageUrl, proposedTier(authoritative|preferred|fallback), fetchStrategy(rss|html|api), reason.";

        try
        {
            var result = await _llmService.ChatAsync(
                _options.LlmProvider,
                new LlmChatRequest(prompt, _options.LlmModel, 0.1, true),
                cancellationToken);

            var parsed = ParseLlmCandidates(result.Content);
            foreach (var item in parsed)
            {
                if (string.IsNullOrWhiteSpace(item.Domain) || string.IsNullOrWhiteSpace(item.HomepageUrl))
                {
                    continue;
                }

                var exists = await _dbContext.NewsSourceCandidates
                    .AnyAsync(x => x.Domain == item.Domain && x.Status == NewsSourceStatus.Pending, cancellationToken);

                if (!exists)
                {
                    _dbContext.NewsSourceCandidates.Add(new NewsSourceCandidate
                    {
                        Domain = item.Domain,
                        HomepageUrl = item.HomepageUrl,
                        ProposedTier = NormalizeTier(item.ProposedTier),
                        Status = NewsSourceStatus.Pending,
                        DiscoveryReason = item.Reason ?? "llm_discovery",
                        FetchStrategy = string.IsNullOrWhiteSpace(item.FetchStrategy) ? "html" : item.FetchStrategy,
                        DiscoveredAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _fileLogWriter.Write("SOURCE-GOV", $"stage=discover error={ex.GetType().Name} message={ex.Message}");
        }
    }

    private async Task VerifyPendingCandidatesAsync(CancellationToken cancellationToken)
    {
        var pending = await _dbContext.NewsSourceCandidates
            .Where(x => x.Status == NewsSourceStatus.Pending)
            .OrderBy(x => x.DiscoveredAt)
            .Take(Math.Max(1, _options.MaxDailyDiscoveryCandidates))
            .ToListAsync(cancellationToken);

        var client = _httpClientFactory.CreateClient("source-governance");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.HttpTimeoutSeconds));

        foreach (var candidate in pending)
        {
            var verificationTraceId = BuildTraceId("verify", candidate.Domain);
            var verification = await VerifyCandidateAsync(client, candidate, cancellationToken);
            candidate.ParseSuccessRate = verification.ParseSuccessRate;
            candidate.TimestampCoverage = verification.TimestampCoverage;
            candidate.FreshnessLagMinutes = verification.FreshnessLagMinutes;
            candidate.VerificationScore = verification.VerificationScore;
            candidate.VerifiedAt = DateTime.UtcNow;

            _dbContext.NewsSourceVerificationRuns.Add(new NewsSourceVerificationRun
            {
                TraceId = verificationTraceId,
                CandidateId = candidate.Id,
                Domain = candidate.Domain,
                Success = verification.Success,
                HttpStatusCode = verification.HttpStatusCode,
                ParseSuccessRate = verification.ParseSuccessRate,
                TimestampCoverage = verification.TimestampCoverage,
                DuplicateRate = verification.DuplicateRate,
                ContentDepth = verification.ContentDepth,
                CrossSourceAgreement = verification.CrossSourceAgreement,
                FreshnessLagMinutes = verification.FreshnessLagMinutes,
                VerificationScore = verification.VerificationScore,
                FailureReason = verification.FailureReason,
                ExecutedAt = DateTime.UtcNow
            });
        }
    }

    private async Task PromoteQualifiedCandidatesAsync(CancellationToken cancellationToken)
    {
        var pending = await _dbContext.NewsSourceCandidates
            .Where(x => x.Status == NewsSourceStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var item in pending)
        {
            if (!SourceGovernancePolicy.CanPromoteCandidate(item, _options))
            {
                continue;
            }

            var exists = await _dbContext.NewsSourceRegistries
                .AnyAsync(x => x.Domain == item.Domain, cancellationToken);

            if (!exists)
            {
                _dbContext.NewsSourceRegistries.Add(new NewsSourceRegistry
                {
                    Domain = item.Domain,
                    BaseUrl = item.HomepageUrl,
                    Tier = item.ProposedTier,
                    Status = NewsSourceStatus.Active,
                    FetchStrategy = item.FetchStrategy,
                    ParseSuccessRate = item.ParseSuccessRate,
                    TimestampCoverage = item.TimestampCoverage,
                    FreshnessLagMinutes = item.FreshnessLagMinutes,
                    QualityScore = item.VerificationScore,
                    LastStatusReason = "auto_promoted",
                    LastCheckedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            item.Status = NewsSourceStatus.Active;
            item.VerifiedAt = DateTime.UtcNow;
        }
    }

    private async Task RefreshSourceStatusesAsync(CancellationToken cancellationToken)
    {
        var healthDate = DateTime.UtcNow.Date;
        var registries = await _dbContext.NewsSourceRegistries
            .Where(x => x.Status != NewsSourceStatus.Rejected)
            .ToListAsync(cancellationToken);

        var registryIds = registries.Select(x => x.Id).ToArray();
        var existingHealthRows = await _dbContext.NewsSourceHealthDailies
            .Where(x => registryIds.Contains(x.SourceId) && x.HealthDate == healthDate)
            .ToDictionaryAsync(x => x.SourceId, cancellationToken);

        var domains = registries.Select(x => x.Domain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var recentRuns = await _dbContext.NewsSourceVerificationRuns
            .Where(x => domains.Contains(x.Domain))
            .OrderByDescending(x => x.ExecutedAt)
            .ToListAsync(cancellationToken);
        var latestRunByDomain = recentRuns
            .GroupBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var source in registries)
        {
            var (nextStatus, reason) = SourceGovernancePolicy.EvaluateSourceStatus(source, _options);
            var oldStatus = source.Status;

            source.Status = nextStatus;
            source.LastStatusReason = reason;
            source.LastCheckedAt = DateTime.UtcNow;
            source.UpdatedAt = DateTime.UtcNow;

            if (!existingHealthRows.TryGetValue(source.Id, out var healthDaily))
            {
                healthDaily = new NewsSourceHealthDaily
                {
                    SourceId = source.Id,
                    HealthDate = healthDate,
                    CreatedAt = DateTime.UtcNow
                };
                existingHealthRows[source.Id] = healthDaily;
                _dbContext.NewsSourceHealthDailies.Add(healthDaily);
            }

            healthDaily.ParseSuccessRate = source.ParseSuccessRate ?? 0m;
            healthDaily.TimestampCoverage = source.TimestampCoverage ?? 0m;
            healthDaily.DuplicateRate = latestRunByDomain.TryGetValue(source.Domain, out var latestRun) ? latestRun.DuplicateRate : 0m;
            healthDaily.FreshnessLagMinutes = source.FreshnessLagMinutes ?? 0;
            healthDaily.ErrorCount = source.ConsecutiveFailures;
            healthDaily.SuggestedStatus = nextStatus;
            healthDaily.SuggestionReason = reason;

            if (!string.Equals(oldStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
            {
                _fileLogWriter.Write(
                    "SOURCE-GOV",
                    $"domain={source.Domain} old={oldStatus} next={nextStatus} reason={reason}");
            }
        }
    }

    private async Task EnqueueCrawlerFixesAsync(CancellationToken cancellationToken)
    {
        var quarantined = await _dbContext.NewsSourceRegistries
            .Where(x => x.Status == NewsSourceStatus.Quarantine)
            .ToListAsync(cancellationToken);

        foreach (var source in quarantined)
        {
            var exists = await _dbContext.CrawlerChangeQueues
                .AnyAsync(x => x.SourceId == source.Id && x.Status != CrawlerChangeStatus.Deployed && x.Status != CrawlerChangeStatus.Rejected, cancellationToken);

            if (!exists)
            {
                _dbContext.CrawlerChangeQueues.Add(new CrawlerChangeQueue
                {
                    TraceId = BuildTraceId("queue", source.Domain),
                    SourceId = source.Id,
                    Domain = source.Domain,
                    Status = CrawlerChangeStatus.Pending,
                    TriggerReason = source.LastStatusReason ?? "quarantine"
                });
            }
        }
    }

    private async Task GenerateCrawlerPatchProposalsAsync(CancellationToken cancellationToken)
    {
        var pending = await _dbContext.CrawlerChangeQueues
            .Where(x => x.Status == CrawlerChangeStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var queue in pending)
        {
            queue.TraceId ??= BuildTraceId("queue", queue.Domain);
            try
            {
                var prompt =
                    "Return JSON object only with fields files(string array), patches(array of {path,content}), summary(string), testCommand(string), replayCommand(string). " +
                    "Domain=" + queue.Domain + ". Reason=" + queue.TriggerReason +
                    ". Files must be parser/test files under backend modules and patches must contain full target file content.";

                var llmResult = await _llmService.ChatAsync(
                    _options.LlmProvider,
                    new LlmChatRequest(prompt, _options.LlmModel, 0.1, true),
                    cancellationToken);

                var proposal = ParsePatchProposal(llmResult.Content);
                queue.ProposedFilesJson = JsonSerializer.Serialize(proposal.Files);
                queue.ProposedPatchJson = JsonSerializer.Serialize(proposal.Patches);
                queue.ProposedPatchSummary = proposal.Summary;
                queue.ProposedTestCommand = proposal.TestCommand;
                queue.ProposedReplayCommand = proposal.ReplayCommand;
                queue.Status = CrawlerChangeStatus.Generated;
                queue.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                queue.ProposedFilesJson = JsonSerializer.Serialize(new[] { "backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/GenericNewsParser.cs" });
                queue.ProposedPatchJson = JsonSerializer.Serialize(new[]
                {
                    new FilePatch(
                        "backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/GenericNewsParser.cs",
                        "// llm fallback proposal placeholder")
                });
                queue.ProposedPatchSummary = "fallback proposal due to llm error: " + ex.GetType().Name;
                queue.ProposedTestCommand = "dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter Parser";
                queue.ProposedReplayCommand = "dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter Parser";
                queue.Status = CrawlerChangeStatus.Generated;
                queue.UpdatedAt = DateTime.UtcNow;
                _fileLogWriter.Write("SOURCE-GOV", $"stage=auto-fix-generate domain={queue.Domain} error={ex.Message}");
            }
        }
    }

    private async Task ValidateCrawlerPatchProposalsAsync(CancellationToken cancellationToken)
    {
        var generated = await _dbContext.CrawlerChangeQueues
            .Where(x => x.Status == CrawlerChangeStatus.Generated)
            .ToListAsync(cancellationToken);

        const string buildCommand = "dotnet build backend/SimplerJiangAiAgent.Api/SimplerJiangAiAgent.Api.csproj -nologo";

        foreach (var queue in generated)
        {
            queue.TraceId ??= BuildTraceId("queue", queue.Domain);
            var sandboxBackups = new List<FileBackup>();
            try
            {
                var files = DeserializeFiles(queue.ProposedFilesJson);
                var patches = DeserializePatches(queue.ProposedPatchJson);
                if (files.Count == 0)
                {
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "empty_files";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                if (patches.Count == 0)
                {
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "empty_patches";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                if (!ArePatchTargetsMatching(files, patches))
                {
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "patch_file_mismatch";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                var invalid = files.Any(file => !IsAllowedCrawlerFile(file));
                if (invalid)
                {
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "path_not_allowed";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                sandboxBackups = await ApplyPatchesAsync(patches, cancellationToken);
                var sandboxApplied = sandboxBackups.Count == patches.Count;
                if (!sandboxApplied)
                {
                    await RestoreBackupsAsync(sandboxBackups, cancellationToken);
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "sandbox_apply_failed";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                var buildExitCode = await _commandRunner.RunAsync(buildCommand, _repoRoot, 0, cancellationToken);
                if (buildExitCode != 0)
                {
                    await RestoreBackupsAsync(sandboxBackups, cancellationToken);
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "build_failed";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(queue.ProposedTestCommand))
                {
                    await RestoreBackupsAsync(sandboxBackups, cancellationToken);
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "missing_test_command";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                var testExitCode = await _commandRunner.RunAsync(queue.ProposedTestCommand, _repoRoot, 0, cancellationToken);
                if (testExitCode != 0)
                {
                    await RestoreBackupsAsync(sandboxBackups, cancellationToken);
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "test_failed";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(queue.ProposedReplayCommand))
                {
                    await RestoreBackupsAsync(sandboxBackups, cancellationToken);
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "missing_replay_command";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                var replayExitCode = await _commandRunner.RunAsync(queue.ProposedReplayCommand, _repoRoot, _options.ReplayTimeoutSeconds, cancellationToken);
                await RestoreBackupsAsync(sandboxBackups, cancellationToken);
                if (replayExitCode != 0)
                {
                    queue.Status = CrawlerChangeStatus.Rejected;
                    queue.ValidationNote = "replay_failed";
                    queue.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                queue.Status = CrawlerChangeStatus.Validated;
                queue.ValidationNote = "validated_by_policy";
                queue.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                await RestoreBackupsAsync(sandboxBackups, cancellationToken);
                queue.Status = CrawlerChangeStatus.Rejected;
                queue.ValidationNote = "sandbox_exception";
                queue.UpdatedAt = DateTime.UtcNow;
                _fileLogWriter.Write("SOURCE-GOV", $"stage=validate_patch_exception queueId={queue.Id} domain={queue.Domain} type={ex.GetType().Name}");
            }
        }
    }

    private async Task DeployValidatedCrawlerPatchesAsync(CancellationToken cancellationToken)
    {
        var validated = await _dbContext.CrawlerChangeQueues
            .Where(x => x.Status == CrawlerChangeStatus.Validated)
            .ToListAsync(cancellationToken);

        foreach (var queue in validated)
        {
            queue.TraceId ??= BuildTraceId("queue", queue.Domain);
            var patches = DeserializePatches(queue.ProposedPatchJson);
            if (patches.Count == 0)
            {
                queue.Status = CrawlerChangeStatus.Rejected;
                queue.ValidationNote = "deploy_missing_patches";
                queue.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            var deploymentBackups = await ApplyPatchesAsync(patches, cancellationToken);
            if (deploymentBackups.Count != patches.Count)
            {
                await RestoreBackupsAsync(deploymentBackups, cancellationToken);
                queue.Status = CrawlerChangeStatus.Rejected;
                queue.ValidationNote = "deploy_apply_failed";
                queue.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            queue.DeploymentBackupJson = JsonSerializer.Serialize(deploymentBackups);
            queue.Status = CrawlerChangeStatus.Deployed;
            queue.UpdatedAt = DateTime.UtcNow;

            _dbContext.CrawlerChangeRuns.Add(new CrawlerChangeRun
            {
                TraceId = queue.TraceId,
                QueueId = queue.Id,
                Domain = queue.Domain,
                Result = "deployed",
                Note = queue.ProposedPatchSummary,
                ExecutedAt = DateTime.UtcNow
            });
        }
    }

    private async Task EvaluateRollbackForDeployedChangesAsync(CancellationToken cancellationToken)
    {
        var rollbackCutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, _options.RollbackGraceMinutes));
        var deployed = await _dbContext.CrawlerChangeQueues
            .Where(x => x.Status == CrawlerChangeStatus.Deployed)
            .ToListAsync(cancellationToken);
        if (deployed.Count == 0)
        {
            return;
        }

        var sourceIds = deployed.Select(x => x.SourceId).Distinct().ToArray();
        var sourceMap = await _dbContext.NewsSourceRegistries
            .Where(x => sourceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var queue in deployed)
        {
            queue.TraceId ??= BuildTraceId("queue", queue.Domain);
            if (queue.UpdatedAt > rollbackCutoff)
            {
                continue;
            }

            if (!sourceMap.TryGetValue(queue.SourceId, out var source))
            {
                continue;
            }

            var shouldRollback = source.Status == NewsSourceStatus.Quarantine || source.ConsecutiveFailures >= _options.MaxConsecutiveFailures;
            if (!shouldRollback)
            {
                continue;
            }

            var backups = DeserializeBackups(queue.DeploymentBackupJson);
            if (backups.Count == 0)
            {
                _fileLogWriter.Write("SOURCE-GOV", $"stage=rollback_skipped_missing_backup domain={queue.Domain} queueId={queue.Id}");
                continue;
            }

            var rollbackSucceeded = await RestoreBackupsAsync(backups, cancellationToken);
            if (!rollbackSucceeded)
            {
                _fileLogWriter.Write("SOURCE-GOV", $"stage=rollback_failed domain={queue.Domain} queueId={queue.Id}");
                continue;
            }

            queue.Status = CrawlerChangeStatus.RolledBack;
            queue.DeploymentBackupJson = null;
            queue.UpdatedAt = DateTime.UtcNow;

            _dbContext.CrawlerChangeRuns.Add(new CrawlerChangeRun
            {
                TraceId = queue.TraceId,
                QueueId = queue.Id,
                Domain = queue.Domain,
                Result = CrawlerChangeStatus.RolledBack,
                Note = "auto_rollback_due_to_source_health",
                ExecutedAt = DateTime.UtcNow
            });
        }
    }

    private async Task<List<FileBackup>> ApplyPatchesAsync(List<FilePatch> patches, CancellationToken cancellationToken)
    {
        var backups = new List<FileBackup>();

        foreach (var patch in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalized = NormalizeRelativePath(patch.Path);
            if (!IsAllowedCrawlerFile(normalized))
            {
                return backups;
            }

            var absolutePath = Path.Combine(_repoRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
            var existed = File.Exists(absolutePath);
            var originalContent = existed ? await File.ReadAllTextAsync(absolutePath, cancellationToken) : null;
            backups.Add(new FileBackup(normalized, existed, originalContent));

            var parent = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await File.WriteAllTextAsync(absolutePath, patch.Content, Encoding.UTF8, cancellationToken);
        }

        return backups;
    }

    private async Task<bool> RestoreBackupsAsync(List<FileBackup> backups, CancellationToken cancellationToken)
    {
        if (backups.Count == 0)
        {
            return true;
        }

        foreach (var backup in backups.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var absolutePath = Path.Combine(_repoRoot, backup.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!backup.Existed)
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                continue;
            }

            if (backup.Content is null)
            {
                return false;
            }

            var parent = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await File.WriteAllTextAsync(absolutePath, backup.Content, Encoding.UTF8, cancellationToken);
        }

        return true;
    }

    private static bool ArePatchTargetsMatching(List<string> files, List<FilePatch> patches)
    {
        var fileSet = files
            .Select(NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var patchSet = patches
            .Select(x => NormalizeRelativePath(x.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return fileSet.SetEquals(patchSet);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Trim().Replace('\\', '/');
    }

    private static List<FilePatch> DeserializePatches(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<FilePatch>();
        }

        try
        {
            var patches = JsonSerializer.Deserialize<List<FilePatch>>(json) ?? new List<FilePatch>();
            return patches
                .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                .Select(x => new FilePatch(NormalizeRelativePath(x.Path), x.Content))
                .ToList();
        }
        catch
        {
            return new List<FilePatch>();
        }
    }

    private static List<FileBackup> DeserializeBackups(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<FileBackup>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<FileBackup>>(json) ?? new List<FileBackup>();
        }
        catch
        {
            return new List<FileBackup>();
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (current.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string NormalizeTier(string? tier)
    {
        var value = tier?.Trim().ToLowerInvariant();
        return value switch
        {
            NewsSourceTier.Authoritative => NewsSourceTier.Authoritative,
            NewsSourceTier.Preferred => NewsSourceTier.Preferred,
            NewsSourceTier.Fallback => NewsSourceTier.Fallback,
            _ => NewsSourceTier.Fallback
        };
    }

    private static List<string> DeserializeFiles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<LlmCandidateItem> ParseLlmCandidates(string content)
    {
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new List<LlmCandidateItem>();
        }

        var list = new List<LlmCandidateItem>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            list.Add(new LlmCandidateItem(
                GetString(element, "domain"),
                GetString(element, "homepageUrl"),
                GetString(element, "proposedTier"),
                GetString(element, "fetchStrategy"),
                GetString(element, "reason")));
        }

        return list;
    }

    private static LlmPatchProposal ParsePatchProposal(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var files = new List<string>();

        if (root.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in filesElement.EnumerateArray())
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    files.Add(value);
                }
            }
        }

        var patches = new List<FilePatch>();
        if (root.TryGetProperty("patches", out var patchesElement) && patchesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var patchElement in patchesElement.EnumerateArray())
            {
                var patchPath = GetString(patchElement, "path");
                var patchContent = GetString(patchElement, "content");
                if (!string.IsNullOrWhiteSpace(patchPath) && patchContent is not null)
                {
                    patches.Add(new FilePatch(patchPath, patchContent));
                }
            }
        }

        return new LlmPatchProposal(
            files,
            patches,
            GetString(root, "summary") ?? "llm_generated_patch",
            GetString(root, "testCommand") ?? "dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj",
            GetString(root, "replayCommand") ?? "dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter Parser");
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool IsAllowedCrawlerFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return false;
        }

        var normalized = NormalizeRelativePath(file);
        var inAllowedPrefix = AllowedCrawlerPathPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (!inAllowedPrefix)
        {
            return false;
        }

        if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.Contains("appsettings", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Program.cs", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("DbContext", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var isServiceParser = normalized.StartsWith("backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("Parser", StringComparison.OrdinalIgnoreCase);
        var isParserTest = normalized.StartsWith("backend/SimplerJiangAiAgent.Api.Tests/", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("Parser", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("Test", StringComparison.OrdinalIgnoreCase);

        return isServiceParser || isParserTest;
    }

    private static string BuildTraceId(string stage, string domain)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain)
            ? "unknown"
            : domain.Replace('.', '-').Replace('/', '-');
        return $"sg-{stage}-{safeDomain}-{Guid.NewGuid():N}";
    }

    private async Task<CandidateVerificationResult> VerifyCandidateAsync(HttpClient client, NewsSourceCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(candidate.HomepageUrl, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var parseSuccessRate = response.IsSuccessStatusCode ? 1.00m : 0m;
            var timestampCoverage = TimestampPattern.IsMatch(content) ? 1.00m : 0.20m;
            var duplicateRate = CalculateDuplicateRate(content);
            var contentDepth = Math.Clamp(content.Length / 4000m, 0m, 1m);
            var crossSourceAgreement = await EstimateCrossSourceAgreementAsync(candidate.Domain, timestampCoverage, cancellationToken);
            var freshnessLag = timestampCoverage >= 1.00m ? 30 : 300;
            var score = (parseSuccessRate * 0.35m)
                + (timestampCoverage * 0.25m)
                + ((1m - duplicateRate) * 0.15m)
                + (contentDepth * 0.15m)
                + (crossSourceAgreement * 0.10m);

            return new CandidateVerificationResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                parseSuccessRate,
                timestampCoverage,
                duplicateRate,
                contentDepth,
                crossSourceAgreement,
                freshnessLag,
                score,
                response.IsSuccessStatusCode ? null : "http_failed");
        }
        catch (Exception ex)
        {
            return new CandidateVerificationResult(
                false,
                (int)HttpStatusCode.ServiceUnavailable,
                0m,
                0m,
                1m,
                0m,
                0m,
                720,
                0m,
                ex.GetType().Name);
        }
    }

    private static decimal CalculateDuplicateRate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 1m;
        }

        var segments = content
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 8)
            .Take(300)
            .ToArray();

        if (segments.Length <= 1)
        {
            return 0m;
        }

        var uniqueCount = segments.Distinct(StringComparer.Ordinal).Count();
        var duplicateCount = segments.Length - uniqueCount;
        return Math.Clamp(duplicateCount / (decimal)segments.Length, 0m, 1m);
    }

    private async Task<decimal> EstimateCrossSourceAgreementAsync(string domain, decimal timestampCoverage, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var activePeerCount = await _dbContext.NewsSourceRegistries
            .Where(x => x.Status == NewsSourceStatus.Active && x.Domain != domain && x.LastCheckedAt.HasValue && x.LastCheckedAt >= now.AddDays(-3))
            .CountAsync(cancellationToken);

        if (activePeerCount == 0)
        {
            return timestampCoverage >= 1m ? 0.60m : 0.40m;
        }

        return timestampCoverage >= 1m ? 0.85m : 0.65m;
    }

    private sealed record LlmCandidateItem(
        string? Domain,
        string? HomepageUrl,
        string? ProposedTier,
        string? FetchStrategy,
        string? Reason);

    private sealed record LlmPatchProposal(
        List<string> Files,
        List<FilePatch> Patches,
        string Summary,
        string TestCommand,
        string ReplayCommand);

    private sealed record FilePatch(
        string Path,
        string Content);

    private sealed record CandidateVerificationResult(
        bool Success,
        int HttpStatusCode,
        decimal ParseSuccessRate,
        decimal TimestampCoverage,
        decimal DuplicateRate,
        decimal ContentDepth,
        decimal CrossSourceAgreement,
        int FreshnessLagMinutes,
        decimal VerificationScore,
        string? FailureReason);

    private sealed record FileBackup(
        string Path,
        bool Existed,
        string? Content);
}