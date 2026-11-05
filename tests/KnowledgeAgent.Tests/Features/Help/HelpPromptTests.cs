using KnowledgeAgent.Features.Help;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Rendering;

namespace KnowledgeAgent.Tests.Features.Help;

public sealed class HelpPromptTests
{
    [Fact]
    public void The_help_prompt_is_built_in_and_not_a_managed_prompt()
    {
        var catalog = new LocalPromptCatalog();

        catalog.Contains(HelpPrompt.Name).ShouldBeFalse("a managed prompt would be seeded to Langfuse and editable on the Prompts page");
        PromptNames.All.ShouldNotContain(HelpPrompt.Name);
        typeof(HelpPrompt).Assembly.GetManifestResourceNames().ShouldContain(HelpPrompt.ResourceName);
    }

    [Fact]
    public void The_help_prompt_names_the_page_the_role_the_missing_setup_and_the_contents()
    {
        var prompt = new HelpPrompt(new HandlebarsPromptRenderer(TimeProvider.System)).Render(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["appName"] = "Knowledge Agent",
            ["today"] = "2026-11-05",
            ["isAdmin"] = false,
            ["pageName"] = "Sources",
            ["pagePath"] = "/sources",
            ["missingSetup"] = new List<object?> { "a chat model (Models page)", "knowledge (Sources page)" },
            ["contents"] = new HelpIndex(new BundledHelpSource().Snapshot).Contents,
        });

        prompt.Name.ShouldBe(HelpPrompt.Name);
        prompt.Origin.ShouldBe(PromptOrigin.Local);
        prompt.Text.ShouldNotBeNull();
        prompt.Text.ShouldContain("They are on Sources (/sources)");
        prompt.Text.ShouldContain("They are a support user");
        prompt.Text.ShouldContain("Still missing: a chat model (Models page); knowledge (Sources page).");
        prompt.Text.ShouldContain(ReadHelpSectionTool.ToolName);
        prompt.Text.ShouldContain(SearchHelpTool.ToolName);
        prompt.Text.ShouldContain("- `guide/connect-a-model#add-an-api-key` Add an API key: ");
        prompt.Text.ShouldNotContain("{{");
    }
}
