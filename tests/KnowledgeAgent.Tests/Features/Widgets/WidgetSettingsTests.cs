using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;

namespace KnowledgeAgent.Tests.Features.Widgets;

public sealed class WidgetSettingsTests
{
    [Fact]
    public void Stored_settings_with_the_former_offer_when_unsure_still_read()
    {
        // Settings saved with the old offerWhenUnsure flag must still load.
        const string stored = """{"behavior":{"handoverEnabled":true},"handover":{"channel":"slack","offerWhenUnsure":false,"waitMinutes":20}}""";

        var settings = (WidgetSettings)JsonColumns.Document<WidgetSettings>().ConvertFromProvider(stored)!;

        settings.Behavior.HandoverEnabled.ShouldBeTrue();
        settings.Handover.Channel.ShouldBe(WidgetChoices.HandoverSlack);
        settings.Handover.WaitMinutes.ShouldBe(20);
        ((string)JsonColumns.Document<WidgetSettings>().ConvertToProvider(settings)!).ShouldNotContain("offerWhenUnsure");
    }
}
