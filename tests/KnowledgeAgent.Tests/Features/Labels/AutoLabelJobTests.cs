using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Labels;

public sealed class AutoLabelJobTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeChatClientFactory _models = new();
    private readonly FakePromptService _prompts = new();
    private readonly RecordingJobQueue _jobs = new();
    private IHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _prompts.SetTemplate(PromptNames.ConversationLabels, "Pick from {{labels}}:\n{{transcript}}");
        _host = PersistenceHost.Build(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Persistence:InMemoryDatabaseName"] = $"auto-label-{Guid.NewGuid():N}",
            },
            _time,
            builder =>
            {
                builder.Services.AddSingleton<IChatClientFactory>(_models);
                builder.Services.AddSingleton<IPromptService>(_prompts);
                builder.Services.AddSingleton<IJobQueue>(_jobs);
                builder.Services.AddOptions<AgentOptions>();
                builder.Services.AddScoped<AutoLabelJob>();
                builder.Services.AddScoped<ILabelService, LabelService>();
            });
        await PersistenceHost.RunStartupInitializersAsync(_host.Services, Ct);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task The_model_picks_at_most_two_offered_labels_and_a_conversation_is_labeled_once()
    {
        var question = await AddLabelAsync("Question", autoApply: true);
        var problem = await AddLabelAsync("Problem", autoApply: true);
        var feedback = await AddLabelAsync("Feedback", autoApply: true);
        var manualOnly = await AddLabelAsync("VIP", autoApply: false);
        var conversationId = await AddConversationAsync("My invoice shows the wrong amount, can you fix it?", "I am sorry, let me check.");
        var utility = _models.Script("utility")
            .ThenText($$"""Sure: {"labels": ["{{problem.Id}}", "{{Guid.NewGuid()}}", "vip", "question", "{{feedback.Id}}"]}""");

        await RunAsync(conversationId);

        (await LabelsOfAsync(conversationId)).ShouldBe([("Problem", "auto"), ("Question", "auto")]);
        var request = utility.Requests.ShouldHaveSingleItem();
        var prompt = _prompts.RendersOf(PromptNames.ConversationLabels).ShouldHaveSingleItem();
        prompt.Text.ShouldContain("Customer: My invoice shows the wrong amount, can you fix it?\nAssistant: I am sorry, let me check.");
        ((IEnumerable<Dictionary<string, object?>>)prompt.Variables["labels"]!).Select(label => label["name"]).ShouldBe(["Feedback", "Problem", "Question"]);
        request.Options.ShouldNotBeNull().Tools.ShouldBeNull();
        manualOnly.AutoApply.ShouldBeFalse();
        question.Id.ShouldNotBe(problem.Id);

        await RunAsync(conversationId);
        utility.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_model_nothing_is_recorded_and_the_job_does_not_fail()
    {
        await AddLabelAsync("Question", autoApply: true);
        var conversationId = await AddConversationAsync("How do I reset my password?");
        _models.NotConfigured("utility", "No utility model is set up.");

        await RunAsync(conversationId);

        (await LabelsOfAsync(conversationId)).ShouldBeEmpty();
        (await ConversationAsync(conversationId)).AutoLabeledAt.ShouldBeNull("it is labeled once a model is set up");
    }

    [Fact]
    public async Task Labels_staff_removed_are_not_offered_and_an_unclear_answer_labels_nothing()
    {
        var question = await AddLabelAsync("Question", autoApply: true);
        var problem = await AddLabelAsync("Problem", autoApply: true);
        var conversationId = await AddConversationAsync("Where do I find the export?");
        await InScopeAsync((ILabelRepository labels) => labels.ApplyAutoAsync(conversationId, [question.Id], _time.GetUtcNow(), Ct));
        await InScopeAsync((ILabelRepository labels) => labels.SetAsync(conversationId, [], _time.GetUtcNow(), Ct));
        await InScopeAsync((ILabelRepository labels) => labels.ResetAutoLabeledAsync(_time.GetUtcNow().AddDays(-1), 10, Ct));
        _models.Script("utility").ThenText($"I think {question.Name} fits best.");

        await RunAsync(conversationId);

        var prompt = _prompts.RendersOf(PromptNames.ConversationLabels).ShouldHaveSingleItem();
        ((IEnumerable<Dictionary<string, object?>>)prompt.Variables["labels"]!).Select(label => label["id"]).ShouldBe([problem.Id.ToString()]);
        (await LabelsOfAsync(conversationId)).ShouldBeEmpty();
        (await ConversationAsync(conversationId)).AutoLabeledAt.ShouldBe(_time.GetUtcNow(), "an answer without labels still counts as labeled");
    }

    [Fact]
    public async Task Re_labeling_queues_the_recent_conversations_again()
    {
        var question = await AddLabelAsync("Question", autoApply: true);
        var recent = await AddConversationAsync("Hello?");
        await InScopeAsync((ILabelRepository labels) => labels.ApplyAutoAsync(recent, [question.Id], _time.GetUtcNow(), Ct));
        _time.Advance(TimeSpan.FromDays(40));
        var newest = await AddConversationAsync("Is there a discount?");

        var queued = await InScopeAsync((ILabelService labels) => labels.RelabelAsync(30, Ct));

        queued.ShouldBe(1);
        _jobs.Enqueued.ShouldBe([(AutoLabelJob.Kind, newest.ToString(), null)]);
        await Should.ThrowAsync<ValidationFailedException>(() => InScopeAsync((ILabelService labels) => labels.RelabelAsync(91, Ct)));
    }

    [Theory]
    [InlineData("""{"labels": []}""", 0)]
    [InlineData("""["question"]""", 1)]
    [InlineData("""```json\n{"labels": ["Question", "QUESTION"]}\n```""", 1)]
    [InlineData("nothing fits", 0)]
    [InlineData("""{"labels": "question"}""", 0)]
    public void Answers_are_read_leniently_but_only_offered_labels_count(string answer, int expected)
    {
        var labels = new[] { new Label { Name = "Question" }, new Label { Name = "Problem" } };

        AutoLabelJob.Parse(answer, labels).Count.ShouldBe(expected);
    }

    [Fact]
    public void A_conversation_without_a_question_has_no_transcript()
    {
        AutoLabelJob.Transcript([new ConversationMessage { Author = MessageAuthor.System, Text = "Closed." }]).ShouldBeNull();
        AutoLabelJob.Transcript([
            new ConversationMessage { Sequence = 2, Author = MessageAuthor.Staff, Text = "We refunded it." },
            new ConversationMessage { Sequence = 1, Author = MessageAuthor.Visitor, Text = "  Refund   please " },
            new ConversationMessage { Sequence = 3, Author = MessageAuthor.System, Text = "Closed." },
        ]).ShouldBe("Customer: Refund please\nSupport team: We refunded it.");
    }

    private async Task RunAsync(Guid conversationId) =>
        await InScopeAsync((AutoLabelJob job) => job.HandleAsync(new JobContext("job-1", AutoLabelJob.Kind, conversationId.ToString(), 1, 3), Ct));

    private Task<Label> AddLabelAsync(string name, bool autoApply) =>
        InScopeAsync((ILabelRepository labels) => labels.AddAsync(
            new Label { Name = name, Description = $"About {name}", AutoApply = autoApply, CreatedAt = _time.GetUtcNow(), UpdatedAt = _time.GetUtcNow() },
            Ct));

    private async Task<Guid> AddConversationAsync(string question, string? answer = null)
    {
        var now = _time.GetUtcNow();
        var conversation = new Conversation { Title = question, CreatedAt = now, UpdatedAt = now };
        conversation.Messages.Add(new ConversationMessage { ConversationId = conversation.Id, Sequence = 1, Author = MessageAuthor.User, Text = question, CreatedAt = now });
        if (answer is not null)
        {
            conversation.Messages.Add(new ConversationMessage { ConversationId = conversation.Id, Sequence = 2, Author = MessageAuthor.Assistant, Text = answer, CreatedAt = now });
        }

        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        return conversation.Id;
    }

    private async Task<Conversation> ConversationAsync(Guid id) =>
        (await InScopeAsync((IConversationRepository conversations) => conversations.GetAsync(id, includeTurns: false, Ct))).ShouldNotBeNull();

    private async Task<List<(string Name, string Source)>> LabelsOfAsync(Guid conversationId)
    {
        var labels = await InScopeAsync((ILabelRepository repository) => repository.ForConversationsAsync([conversationId], Ct));
        return labels.TryGetValue(conversationId, out var found) ? found.Select(label => (label.Name, label.Source)).ToList() : [];
    }

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task InScopeAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private sealed class RecordingJobQueue : IJobQueue
    {
        public List<(string Kind, string Key, DateTimeOffset? RunAt)> Enqueued { get; } = [];

        public Task EnqueueAsync(string kind, string key, DateTimeOffset? runAt = null, CancellationToken cancellationToken = default)
        {
            Enqueued.Add((kind, key, runAt));
            return Task.CompletedTask;
        }
    }
}
