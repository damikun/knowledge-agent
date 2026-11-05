using System.Text;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Evaluations.Support;

internal static class EvalTestHost
{
    public const string JudgeModel = "fake-judge";

    // Blanks the well-known key variables so a developer's real keys are never used.
    private static readonly Dictionary<string, string?> NoRealProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AI:Providers:anthropic:ApiKey"] = string.Empty,
        ["AI:Providers:openai:ApiKey"] = string.Empty,
        ["AI:Providers:ollama:Endpoint"] = string.Empty,
        ["ANTHROPIC_API_KEY"] = string.Empty,
        ["OPENAI_API_KEY"] = string.Empty,
        ["VOYAGE_API_KEY"] = string.Empty,
        ["AI:Providers:voyage:ApiKey"] = string.Empty,
        ["OLLAMA_ENDPOINT"] = string.Empty,
        ["Langfuse:BaseUrl"] = string.Empty,
    };

    public static IConfiguration AppSettings(IReadOnlyDictionary<string, string?>? extra = null) =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("api.appsettings.json", optional: false)
            .AddInMemoryCollection(NoRealProviders)
            .AddInMemoryCollection(extra ?? new Dictionary<string, string?>())
            .Build();

    // Deterministic embeddings and a rule-based chat client that quotes the first passage; embedded prompts, no reranking.
    public static Dictionary<string, string?> TestModelSettings() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["AI:Providers:fake:Type"] = FakeAiProvider.TypeName,
        ["AI:Providers:fake:DefaultChatModel"] = "fake-chat",
        ["AI:Providers:fake:DefaultEmbeddingModel"] = "fake-embedding",
        ["AI:Profiles:orchestrator:Provider"] = "fake",
        ["AI:Profiles:researcher:Provider"] = "fake",
        ["AI:Profiles:utility:Provider"] = "fake",
        ["AI:Profiles:embedding:Provider"] = "fake",
        ["Prompts:Store"] = "Local",
        ["Search:Rerank"] = "false",
    };

    public static IsolatedTargetOptions TestModels(IReadOnlyDictionary<string, string?>? storage = null) => new()
    {
        Configuration = AppSettings(TestModelSettings()),
        Overrides = storage,
        ConfigureServices = services => services.AddSingleton<IAiProvider>(new FakeAiProvider()),
    };

    public static IsolatedTargetOptions WithFakeChat(FakeAiProvider provider, IReadOnlyDictionary<string, string?>? extra = null)
    {
        var settings = TestModelSettings();
        settings["AI:Profiles:judge:Provider"] = "fake";

        // Chat clients are cached per provider and model, so a distinct model gives the judge its own client.
        settings["AI:Profiles:judge:Model"] = JudgeModel;
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return new IsolatedTargetOptions
        {
            Configuration = AppSettings(settings),
            ConfigureServices = services => services.AddSingleton<IAiProvider>(provider),
        };
    }

    public static EvalCorpusDocument Document(string fileName, string title, string text) =>
        new(fileName, title, fileName, null, Encoding.UTF8.GetBytes(text));

    public static EvalItem Item(
        string id,
        string question,
        string[]? sources = null,
        string[]? facts = null,
        string[]? tags = null,
        bool answerable = true,
        string? reference = null) =>
        new(id, question, answerable, sources ?? [], facts ?? [], reference, tags ?? []);

    public static EvalDataset SmallDataset(params EvalItem[] items) => new()
    {
        Name = "small-golden",
        Source = EvalDatasetSources.Builtin,
        Corpus =
        [
            Document("vacation.md", "Vacation policy", """
                # Vacation policy

                Every employee receives 27 vacation days per calendar year. Unused vacation days can be carried over until the end of March of the following year.
                Requests longer than ten consecutive working days need approval from the department head at least six weeks in advance.
                """),
            Document("robot-errors.md", "Robot error codes", """
                # Robot error codes

                Error Q-417 means the gripper vacuum dropped below the safe threshold. Clean the suction cups and restart the cycle.
                Error Q-503 means the joint temperature exceeded 85 degrees Celsius. Let the arm cool down for twenty minutes before resuming work.
                """),
            Document("cafeteria.md", "Cafeteria", """
                # Cafeteria

                The cafeteria on the ground floor opens at 7:30 and serves lunch between 11:30 and 14:00. Vegetarian and vegan dishes are available every day.
                The coffee machines on each floor are free for employees and visitors.
                """),
        ],
        Items = items,
    };
}
