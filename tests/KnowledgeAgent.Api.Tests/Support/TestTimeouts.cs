namespace KnowledgeAgent.Api.Tests.Support;

// Generous because the first test of a class also starts the app and possibly the containers.
public static class TestTimeouts
{
    public const int Default = 180_000;
}
