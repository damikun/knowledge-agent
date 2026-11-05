namespace KnowledgeAgent.Infrastructure.Web;

public sealed class ValidationFailedException : Exception
{
    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public ValidationFailedException(IDictionary<string, string[]> errors)
        : base(string.Join(" ", errors.SelectMany(e => e.Value)))
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
