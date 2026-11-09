using System.Runtime.CompilerServices;
using Anthropic.Core;
using Anthropic.Models.Beta;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Beta = Anthropic.Models.Beta.Messages;
using Messages = Anthropic.Models.Messages;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Anthropic;

internal sealed partial class ClaudeChatClient(
    IChatClient innerClient,
    AnthropicSettings settings,
    string defaultModel,
    int defaultMaxOutputTokens,
    OneTimeWarnings warnings,
    ILogger logger,
    IDisposable? ownedResource = null) : DelegatingChatClient(innerClient)
{
    public AnthropicSettings Settings { get; } = settings;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, Prepare(options), cancellationToken);
        ReportInputTransformations(response.RawRepresentation);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, Prepare(options), cancellationToken))
        {
            ReportInputTransformations(update.RawRepresentation);
            yield return update;
        }
    }

    internal ChatOptions Prepare(ChatOptions? options)
    {
        var prepared = options?.Clone() ?? new ChatOptions();
        AnthropicRequestRules.Apply(prepared, Settings.ThinkingMode, defaultEffort: false, warnings);

        // A caller-supplied factory owns the raw request shape and is left alone.
        if (prepared.RawRepresentationFactory is null && (Settings.UsesBetaEndpoint || Settings.PromptCaching))
        {
            // The SDK adapter fills in messages, tools and thinking on top of these parameters but takes model and
            // max_tokens from them, so they are built per call from the request options.
            var model = string.IsNullOrWhiteSpace(prepared.ModelId) ? defaultModel : prepared.ModelId;
            long maxTokens = prepared.MaxOutputTokens is > 0 ? prepared.MaxOutputTokens.Value : defaultMaxOutputTokens;
            var reasoning = prepared.Reasoning;
            var explicitMaxTokens = prepared.MaxOutputTokens is > 0;
            prepared.RawRepresentationFactory = Settings.UsesBetaEndpoint
                ? _ => WithBlockBinding(CreateBetaParameters(model, maxTokens), reasoning, explicitMaxTokens)
                : _ => CreateParameters(model, maxTokens);
        }

        return prepared;
    }

    internal Beta.MessageCreateParams CreateBetaParameters(string model, long maxTokens)
    {
        var parameters = new Beta.MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = [],
        };

        if (Settings.RefusalFallback == RefusalFallbackMode.Models)
        {
            // The array form is gated by the earlier beta header of the series.
            parameters = parameters with
            {
                Betas = [AnthropicBeta.ServerSideFallback2026_06_01],
                Fallbacks = Settings.FallbackModels.Select(m => new Beta.BetaFallbackParam(m)).ToList(),
            };
        }
        else if (Settings.RefusalFallback == RefusalFallbackMode.Default)
        {
            parameters = parameters with { Betas = [AnthropicBeta.ServerSideFallback2026_07_01], Fallbacks = new Beta.Default() };
        }

        // Only set when enabled: assigning null would serialize an explicit "cache_control": null.
        if (Settings.PromptCaching)
        {
            parameters = parameters with
            {
                CacheControl = Settings.OneHourCache ? new Beta.BetaCacheControlEphemeral { Ttl = Beta.Ttl.Ttl1h } : new Beta.BetaCacheControlEphemeral(),
            };
        }

        return parameters;
    }

    // The adapter only fills in thinking and effort when the parameters carry no thinking, so with block binding both
    // are set here the way the adapter would set them.
    internal Beta.MessageCreateParams WithBlockBinding(Beta.MessageCreateParams parameters, ReasoningOptions? reasoning, bool explicitMaxTokens)
    {
        if (Settings.ThinkingMismatch == ThinkingMismatchMode.Off || reasoning?.Effort is not { } effort || effort == ReasoningEffort.None)
        {
            return parameters;
        }

        var binding = new Beta.BetaThinkingBlockBinding
        {
            PrefixMismatchBehavior = Settings.ThinkingMismatch == ThinkingMismatchMode.Error
                ? Beta.BetaThinkingPrefixMismatchBehavior.Error
                : Beta.BetaThinkingPrefixMismatchBehavior.DropBlock,
        };

        long budget = effort == ReasoningEffort.Low ? 1024
            : effort == ReasoningEffort.Medium ? 8192
            : effort == ReasoningEffort.High ? 16384
            : 32768;
        var maxTokens = parameters.MaxTokens;
        if (maxTokens <= budget)
        {
            // The budget must stay below max_tokens: a default limit grows, a configured one caps the budget.
            if (explicitMaxTokens)
            {
                budget = maxTokens - 1;
            }
            else
            {
                maxTokens += budget;
            }
        }

        List<ApiEnum<string, AnthropicBeta>> betas = [.. parameters.Betas ?? [], AnthropicBeta.ThinkingBindingControls2026_08_01];

        if (Settings.ThinkingMode == AnthropicThinkingMode.Extended)
        {
            var enabled = new Beta.BetaThinkingConfigEnabled { BudgetTokens = budget, BlockBinding = binding };
            if (reasoning.Output == ReasoningOutput.None)
            {
                enabled = enabled with { Display = Beta.BetaThinkingConfigEnabledDisplay.Omitted };
            }

            return parameters with { MaxTokens = maxTokens, Betas = betas, Thinking = enabled };
        }

        var adaptive = new Beta.BetaThinkingConfigAdaptive { BlockBinding = binding };
        if (reasoning.Output is { } output)
        {
            adaptive = adaptive with { Display = output == ReasoningOutput.None ? Beta.Display.Omitted : Beta.Display.Summarized };
        }

        var level = effort == ReasoningEffort.Low ? Beta.Effort.Low
            : effort == ReasoningEffort.Medium ? Beta.Effort.Medium
            : effort == ReasoningEffort.High ? Beta.Effort.High
            : Beta.Effort.Xhigh;
        return parameters with
        {
            MaxTokens = maxTokens,
            Betas = betas,
            Thinking = adaptive,
            OutputConfig = new Beta.BetaOutputConfig { Effort = level },
        };
    }

    private void ReportInputTransformations(object? raw)
    {
        var transformations = raw switch
        {
            Beta.BetaMessage message => message.InputTransformations,
            Beta.BetaRawMessageStreamEvent streamEvent when streamEvent.TryPickDelta(out var delta) => delta.InputTransformations,
            _ => null,
        };

        if (transformations is { Count: > 0 })
        {
            var reasons = string.Join(", ", transformations.Select(item => item.Match(
                dropped => $"{dropped.Path}: dropped ({dropped.Reason.Raw()})",
                allowed => $"{allowed.Path}: kept ({allowed.Reason.Raw()})")));
            LogInputTransformed(logger, transformations.Count, reasons);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude did not accept {Count} replayed thinking block(s) as sent: {Reasons}")]
    private static partial void LogInputTransformed(ILogger logger, int count, string reasons);

    internal Messages.MessageCreateParams CreateParameters(string model, long maxTokens)
    {
        var parameters = new Messages.MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = [],
        };

        if (Settings.PromptCaching)
        {
            parameters = parameters with
            {
                CacheControl = Settings.OneHourCache ? new Messages.CacheControlEphemeral { Ttl = Messages.Ttl.Ttl1h } : new Messages.CacheControlEphemeral(),
            };
        }

        return parameters;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ownedResource?.Dispose();
        }

        base.Dispose(disposing);
    }
}
