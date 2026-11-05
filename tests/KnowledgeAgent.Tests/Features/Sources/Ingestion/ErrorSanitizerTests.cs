using KnowledgeAgent.Features.Sources.Ingestion;

namespace KnowledgeAgent.Tests.Features.Sources.Ingestion;

public sealed class ErrorSanitizerTests
{
    [Theory]
    [InlineData("Invalid api_key=sk-abc123SECRET supplied", "Invalid api_key=*** supplied")]
    [InlineData("Authorization: Bearer eyJhbGciOi.abc.def rejected", "Authorization=*** *** rejected")]
    [InlineData("token: abc123 expired", "token=*** expired")]
    [InlineData("Incorrect API key provided: sk-proj-ABCDEFGHIJKLMNOPQRSTUVWX.", "Incorrect API key provided: ***.")]
    [InlineData("GET https://host.example.com/path?sig=SECRET&x=1 failed", "GET https://host.example.com/path failed")]
    [InlineData("first line\nsecond line with details", "first line")]
    [InlineData("  spaced    out   message  ", "spaced out message")]
    public void Messages_are_redacted_and_shortened(string message, string expected)
    {
        ErrorSanitizer.Sanitize(message).ShouldBe(expected);
    }

    [Fact]
    public void Long_messages_are_truncated_with_an_ellipsis()
    {
        var sanitized = ErrorSanitizer.Sanitize(new string('x', 1000));

        sanitized.Length.ShouldBe(ErrorSanitizer.MaxLength);
        sanitized.ShouldEndWith("…");
    }

    [Fact]
    public void Exceptions_without_a_message_get_a_generic_one()
    {
        ErrorSanitizer.ToUserMessage(new EmptyMessageException()).ShouldBe("Ingestion failed (EmptyMessageException).");
    }

    [Fact]
    public void Timeouts_and_cancellations_get_friendly_messages()
    {
        ErrorSanitizer.ToUserMessage(new TimeoutException("socket internals")).ShouldBe("The operation timed out.");
        ErrorSanitizer.ToUserMessage(new TaskCanceledException()).ShouldBe("The operation was cancelled or timed out.");
    }

    [Fact]
    public void Ingestion_exceptions_keep_their_message()
    {
        ErrorSanitizer.ToUserMessage(new IngestionException("The server responded with HTTP 404 (NotFound).", isPermanent: true))
            .ShouldBe("The server responded with HTTP 404 (NotFound).");
    }

    private sealed class EmptyMessageException : Exception
    {
        public override string Message => string.Empty;
    }
}
