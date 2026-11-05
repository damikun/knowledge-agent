using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Prompts;

// Messages must never include API keys or credentials.
internal static partial class PromptLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Fetched prompt {PromptName} version {PromptVersion} from Langfuse ({Selector})")]
    public static partial void PromptFetched(this ILogger logger, string promptName, int promptVersion, string selector);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Prompt {PromptName} ({Selector}) does not exist in Langfuse")]
    public static partial void PromptNotInLangfuse(this ILogger logger, string promptName, string selector);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Langfuse request for prompt {PromptName} ({Selector}) failed; serving the cached copy (version {PromptVersion}): {Reason}")]
    public static partial void ServingStalePrompt(this ILogger logger, string promptName, string selector, int promptVersion, string reason);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "Langfuse request for prompt {PromptName} ({Selector}) failed and nothing is cached: {Reason}")]
    public static partial void PromptFetchFailed(this ILogger logger, string promptName, string selector, string reason);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Debug, Message = "Using the local default of prompt {PromptName}")]
    public static partial void UsingLocalPrompt(this ILogger logger, string promptName);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Warning, Message = "Could not list prompts from Langfuse; showing local defaults only: {Reason}")]
    public static partial void ListFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Debug, Message = "Could not determine the Langfuse project id for deep links: {Reason}")]
    public static partial void ProjectLookupFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Debug, Message = "Prompt seeding skipped: {Reason}")]
    public static partial void SeedingSkipped(this ILogger logger, string reason);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Information, Message = "Seeded prompt {PromptName} into Langfuse as version {PromptVersion}")]
    public static partial void PromptSeeded(this ILogger logger, string promptName, int promptVersion);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Debug, Message = "Prompt {PromptName} already exists in Langfuse; not seeding it")]
    public static partial void PromptAlreadyInLangfuse(this ILogger logger, string promptName);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Information, Message = "Prompt seeding completed: {CreatedCount} created, {ExistingCount} already present")]
    public static partial void SeedingCompleted(this ILogger logger, int createdCount, int existingCount);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Information, Message = "Updated prompt {PromptName} in Langfuse from the seeded version {PreviousVersion} to the new default as version {PromptVersion}")]
    public static partial void PromptUpdated(this ILogger logger, string promptName, int previousVersion, int promptVersion);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Information, Message = "Langfuse is not reachable yet ({Reason}); prompt seeding continues in the background")]
    public static partial void SeedingDeferred(this ILogger logger, string reason);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Debug, Message = "Prompt seeding attempt {Attempt} failed ({Reason}); retrying in {RetryInterval}")]
    public static partial void SeedingAttemptFailed(this ILogger logger, int attempt, string reason, TimeSpan retryInterval);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Warning, Message = "Prompt seeding gave up after {Attempts} attempt(s); Langfuse is still unreachable ({Reason}). Local default prompts stay in use")]
    public static partial void SeedingGaveUp(this ILogger logger, int attempts, string reason);
}
