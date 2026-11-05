using Anthropic.Models.Beta;
using Microsoft.Extensions.AI;
using Beta = Anthropic.Models.Beta.Messages;
using Messages = Anthropic.Models.Messages;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Anthropic;

internal sealed class ClaudeChatClient(
    IChatClient innerClient,
    AnthropicSettings settings,
    string defaultModel,
    int defaultMaxOutputTokens,
    OneTimeWarnings warnings,
    IDisposable? ownedResource = null) : DelegatingChatClient(innerClient)
{
    public AnthropicSettings Settings { get; } = settings;

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Prepare(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Prepare(options), cancellationToken);

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
            prepared.RawRepresentationFactory = Settings.UsesBetaEndpoint
                ? _ => CreateBetaParameters(model, maxTokens)
                : _ => CreateParameters(model, maxTokens);
        }

        return prepared;
    }

    internal Beta.MessageCreateParams CreateBetaParameters(string model, long maxTokens)
    {
        Beta.BetaFallbacksParam fallbacks;
        AnthropicBeta beta;
        if (Settings.RefusalFallback == RefusalFallbackMode.Models)
        {
            // The array form is gated by the earlier beta header of the series.
            fallbacks = Settings.FallbackModels.Select(m => new Beta.BetaFallbackParam(m)).ToList();
            beta = AnthropicBeta.ServerSideFallback2026_06_01;
        }
        else
        {
            fallbacks = new Beta.Default();
            beta = AnthropicBeta.ServerSideFallback2026_07_01;
        }

        var parameters = new Beta.MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = [],
            Betas = [beta],
            Fallbacks = fallbacks,
        };

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
