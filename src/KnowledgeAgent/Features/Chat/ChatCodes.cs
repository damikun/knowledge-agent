namespace KnowledgeAgent.Features.Chat;

public static class ChatFinishReasons
{
    public const string Stop = "stop";
    public const string Length = "length";
    public const string MaxIterations = "max_iterations";
    public const string Refusal = "refusal";
    public const string Error = "error";

    public const string Stopped = "stopped";
}

public static class VisitorErrorTexts
{
    public const string Unavailable = "The assistant is not available right now. Please try again later.";
    public const string Failed = "Something went wrong while answering. Please try again.";
    public const string Refused = "I can't help with that request.";
    public const string Gone = "This conversation is no longer available. Start a new one.";

    public static readonly IReadOnlyList<string> All = [Unavailable, Failed, Refused, Gone];

    public static string For(string? code) => code switch
    {
        ChatErrorCodes.ModelNotConfigured or ChatErrorCodes.ProviderError => Unavailable,
        ChatErrorCodes.Refusal => Refused,
        ChatErrorCodes.NotFound => Gone,
        _ => Failed,
    };
}

public static class ChatErrorCodes
{
    public const string NotFound = "not_found";
    public const string ModelNotConfigured = "model_not_configured";
    public const string ProviderError = "provider_error";
    public const string Refusal = "refusal";
    public const string InternalError = "internal_error";
    public const string ConversationBusy = "conversation_busy";
    public const string ConversationClosed = "conversation_closed";
    public const string PersonJoined = "person_joined";

    public const string ConfirmationExpired = "confirmation_expired";
}
