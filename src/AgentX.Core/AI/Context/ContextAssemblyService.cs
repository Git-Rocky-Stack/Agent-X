using System.Text;
using AgentX.Core.AI.Models;
using Serilog;

namespace AgentX.Core.AI.Context;

public sealed class ContextAssemblyService : IContextAssemblyService
{
    private readonly IContextWindowManager _contextWindowManager;
    private readonly ISemanticContextSelector _semanticContextSelector;
    private readonly IConversationCompressionService _conversationCompressionService;
    private readonly ILogger _logger;

    public ContextAssemblyService(
        IContextWindowManager contextWindowManager,
        ISemanticContextSelector semanticContextSelector,
        IConversationCompressionService conversationCompressionService,
        ILogger logger)
    {
        _contextWindowManager = contextWindowManager ?? throw new ArgumentNullException(nameof(contextWindowManager));
        _semanticContextSelector = semanticContextSelector ?? throw new ArgumentNullException(nameof(semanticContextSelector));
        _conversationCompressionService = conversationCompressionService ?? throw new ArgumentNullException(nameof(conversationCompressionService));
        _logger = logger?.ForContext<ContextAssemblyService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ContextAssemblyResult> AssembleAsync(
        ContextAssemblyRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await AssembleCoreAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Context assembly failed. Falling back to legacy context fitting.");
            return await BuildLegacyFallbackAsync(
                request,
                ComposeSystemPrompt(request.SystemPrompt, request.MemoryContext, null),
                "assembly_error",
                ct).ConfigureAwait(false);
        }
    }

    private async Task<ContextAssemblyResult> AssembleCoreAsync(
        ContextAssemblyRequest request,
        CancellationToken ct)
    {
        var effectiveContextWindow = _contextWindowManager.GetEffectiveContextWindow(request.ContextWindow);
        var baseSystemPrompt = ComposeSystemPrompt(request.SystemPrompt, request.MemoryContext, null);

        if (request.ConversationMessages.Count == 0)
        {
            return new ContextAssemblyResult
            {
                SystemPrompt = baseSystemPrompt,
                Diagnostics = new ContextAssemblyDiagnostics
                {
                    EstimatedPromptTokens = _contextWindowManager.EstimateTokenCount(baseSystemPrompt ?? string.Empty)
                }
            };
        }

        var systemPromptTokens = _contextWindowManager.EstimateTokenCount(baseSystemPrompt ?? string.Empty);
        var availableMessageBudget = effectiveContextWindow - request.ReserveForResponse - systemPromptTokens;
        if (availableMessageBudget <= 0)
        {
            return await BuildLegacyFallbackAsync(request, baseSystemPrompt, "no_message_budget", ct).ConfigureAwait(false);
        }

        var originalMessageTokens = _contextWindowManager.EstimateTokenCount(request.ConversationMessages);
        if (originalMessageTokens <= availableMessageBudget)
        {
            return new ContextAssemblyResult
            {
                Messages = request.ConversationMessages.ToList(),
                SystemPrompt = baseSystemPrompt,
                Diagnostics = new ContextAssemblyDiagnostics
                {
                    OriginalMessageCount = request.ConversationMessages.Count,
                    SelectedMessageCount = request.ConversationMessages.Count,
                    AnchorMessageCount = Math.Min(request.ConversationMessages.Count, request.RecentAnchorCount),
                    EstimatedMessageTokens = originalMessageTokens,
                    EstimatedPromptTokens = originalMessageTokens + systemPromptTokens
                }
            };
        }

        var indexedMessages = request.ConversationMessages
            .Select((message, index) => new IndexedChatMessage(index, message))
            .ToList();

        var anchors = indexedMessages
            .TakeLast(Math.Min(request.RecentAnchorCount, indexedMessages.Count))
            .ToList();
        var anchorTokens = _contextWindowManager.EstimateTokenCount(anchors.Select(x => x.Message));
        if (anchorTokens >= availableMessageBudget)
        {
            return await BuildLegacyFallbackAsync(request, baseSystemPrompt, "anchor_budget_exceeded", ct).ConfigureAwait(false);
        }

        var olderCandidates = indexedMessages
            .Take(Math.Max(0, indexedMessages.Count - anchors.Count))
            .ToList();

        var selection = await _semanticContextSelector.SelectRelevantContextAsync(
            new ContextSelectionRequest
            {
                CurrentQuery = request.CurrentQuery,
                CandidateMessages = olderCandidates,
                MaxTokenBudget = availableMessageBudget - anchorTokens
            },
            ct).ConfigureAwait(false);

        var selectedMessages = selection.SelectedMessages
            .Concat(anchors)
            .OrderBy(x => x.Index)
            .Select(x => x.Message)
            .ToList();

        var selectedMessageTokens = _contextWindowManager.EstimateTokenCount(selectedMessages);
        if (selectedMessageTokens > availableMessageBudget)
        {
            selectedMessages = await _contextWindowManager
                .FitToContextWindowAsync(
                    selectedMessages,
                    availableMessageBudget + request.ReserveForResponse,
                    request.ReserveForResponse,
                    ct)
                .ConfigureAwait(false);
            selectedMessageTokens = _contextWindowManager.EstimateTokenCount(selectedMessages);
        }

        var compressionResult = ConversationCompressionResult.Skip("not_needed", selection.OverflowMessages.Count);
        var augmentedSystemPrompt = baseSystemPrompt;
        var unusedBudget = Math.Max(0, availableMessageBudget - selectedMessageTokens);

        if (selection.OverflowMessages.Count > 0 && unusedBudget >= 32)
        {
            try
            {
                compressionResult = await _conversationCompressionService.CompressAsync(
                    new ConversationCompressionRequest
                    {
                        CurrentQuery = request.CurrentQuery,
                        OverflowMessages = selection.OverflowMessages,
                        MaxSummaryTokens = unusedBudget
                    },
                    ct).ConfigureAwait(false);

                if (!compressionResult.WasSkipped &&
                    !string.IsNullOrWhiteSpace(compressionResult.Summary) &&
                    compressionResult.EstimatedSummaryTokens <= unusedBudget)
                {
                    augmentedSystemPrompt = ComposeSystemPrompt(
                        request.SystemPrompt,
                        request.MemoryContext,
                        compressionResult.Summary);
                }
                else if (!compressionResult.WasSkipped)
                {
                    compressionResult = ConversationCompressionResult.Skip(
                        "summary_exceeded_unused_budget",
                        selection.OverflowMessages.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warning(ex, "Overflow compression failed. Continuing without summary.");
                compressionResult = ConversationCompressionResult.Skip(
                    "compression_error",
                    selection.OverflowMessages.Count);
            }
        }

        var estimatedPromptTokens = selectedMessageTokens +
                                    _contextWindowManager.EstimateTokenCount(augmentedSystemPrompt ?? string.Empty);

        _logger.Debug(
            "Context assembled: original={OriginalCount}, selected={SelectedCount}, anchors={AnchorCount}, overflow={OverflowCount}, summary={AddedSummary}, lexicalFallback={LexicalFallback}",
            request.ConversationMessages.Count,
            selectedMessages.Count,
            anchors.Count,
            selection.OverflowMessages.Count,
            !compressionResult.WasSkipped,
            selection.UsedLexicalFallback);

        return new ContextAssemblyResult
        {
            Messages = selectedMessages,
            SystemPrompt = augmentedSystemPrompt,
            Diagnostics = new ContextAssemblyDiagnostics
            {
                OriginalMessageCount = request.ConversationMessages.Count,
                SelectedMessageCount = selectedMessages.Count,
                AnchorMessageCount = anchors.Count,
                OverflowMessageCount = selection.OverflowMessages.Count,
                EstimatedMessageTokens = selectedMessageTokens,
                EstimatedPromptTokens = estimatedPromptTokens,
                AddedOverflowSummary = !compressionResult.WasSkipped,
                UsedLexicalFallback = selection.UsedLexicalFallback,
                CompressionSkipReason = compressionResult.SkipReason
            }
        };
    }

    private async Task<ContextAssemblyResult> BuildLegacyFallbackAsync(
        ContextAssemblyRequest request,
        string? systemPrompt,
        string reason,
        CancellationToken ct)
    {
        var effectiveContextWindow = _contextWindowManager.GetEffectiveContextWindow(request.ContextWindow);
        var fittedMessages = await _contextWindowManager
            .FitToContextWindowAsync(
                request.ConversationMessages.ToList(),
                effectiveContextWindow,
                request.ReserveForResponse,
                ct)
            .ConfigureAwait(false);

        return new ContextAssemblyResult
        {
            Messages = fittedMessages,
            SystemPrompt = systemPrompt,
            Diagnostics = new ContextAssemblyDiagnostics
            {
                OriginalMessageCount = request.ConversationMessages.Count,
                SelectedMessageCount = fittedMessages.Count,
                AnchorMessageCount = Math.Min(fittedMessages.Count, request.RecentAnchorCount),
                EstimatedMessageTokens = _contextWindowManager.EstimateTokenCount(fittedMessages),
                EstimatedPromptTokens = _contextWindowManager.EstimateTokenCount(fittedMessages) +
                                        _contextWindowManager.EstimateTokenCount(systemPrompt ?? string.Empty),
                UsedLegacyFallback = true,
                CompressionSkipReason = reason
            }
        };
    }

    private static string? ComposeSystemPrompt(
        string? baseSystemPrompt,
        string? memoryContext,
        string? overflowSummary)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(baseSystemPrompt))
        {
            parts.Add(baseSystemPrompt.Trim());
        }

        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            parts.Add(memoryContext.Trim());
        }

        if (!string.IsNullOrWhiteSpace(overflowSummary))
        {
            var builder = new StringBuilder();
            builder.AppendLine("[Condensed Earlier Conversation Context]");
            builder.AppendLine(overflowSummary.Trim());
            parts.Add(builder.ToString().Trim());
        }

        return parts.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}
