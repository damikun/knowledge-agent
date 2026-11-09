using KnowledgeAgent.Features.Chat;

namespace KnowledgeAgent.Features.Widgets;

// Models, token counts, reasoning, raw tool arguments and the researcher's work stay internal.
internal static class VisitorChatEvents
{
    public static ChatStreamEvent? ToVisitor(ChatStreamEvent streamEvent, string citationMode)
    {
        if (streamEvent.ParentCallId is not null)
        {
            return null;
        }

        return streamEvent switch
        {
            ConversationEvent or TextDeltaEvent or StepFinishedEvent or DoneEvent or HandoverOfferedEvent => streamEvent,

            ToolCallEvent { ToolName: Chat.Tools.ToolNames.TransferToAgent or Chat.Tools.ToolNames.TransferBack } => null,
            ToolResultEvent { ToolName: Chat.Tools.ToolNames.TransferToAgent or Chat.Tools.ToolNames.TransferBack } => null,
            ToolCallEvent call => call with { Arguments = null },

            // The details are built by the server from the arguments, so the visitor sees what they confirm.
            ConfirmationRequiredEvent confirmation => confirmation with { Arguments = null, ToolName = string.Empty },
            ToolResultEvent result => result with
            {
                Summary = string.Empty,
                Citations = result.Citations.Select(citation => ForVisitor(citation, citationMode)).OfType<CitationDto>().ToList(),
            },
            ErrorEvent error => ToVisitor(error),
            _ => null,
        };
    }

    private static ErrorEvent ToVisitor(ErrorEvent error) => error.Code switch
    {
        ChatErrorCodes.ModelNotConfigured or ChatErrorCodes.ProviderError or ChatErrorCodes.Refusal or ChatErrorCodes.NotFound =>
            new ErrorEvent(VisitorErrorTexts.For(error.Code), error.Code),
        _ => new ErrorEvent(VisitorErrorTexts.Failed, ChatErrorCodes.InternalError),
    };

    public static VisitorCitationDto? ToVisitorCitation(CitationDto citation, string citationMode) =>
        citationMode == WidgetChoices.CitationsHidden
            ? null
            : new VisitorCitationDto(
                citation.Ref,
                citation.SourceTitle,
                citationMode == WidgetChoices.CitationsLinks ? WebUrl(citation.SourceUrl) : null,
                citation.Snippet);

    private static CitationDto? ForVisitor(CitationDto citation, string citationMode) =>
        citationMode == WidgetChoices.CitationsHidden
            ? null
            : citation with
            {
                SourceId = Guid.Empty,
                ChunkId = Guid.Empty,
                SourceUrl = citationMode == WidgetChoices.CitationsLinks ? WebUrl(citation.SourceUrl) : null,
                Score = 0,
            };

    // Only web pages are linked; uploaded files have no public address.
    private static string? WebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? url : null;
}
