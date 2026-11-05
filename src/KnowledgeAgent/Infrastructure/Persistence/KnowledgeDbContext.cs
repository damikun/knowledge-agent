using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Agents.Access;
using KnowledgeAgent.Features.Alerts;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Connections.Mcp;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Infrastructure.Persistence;

public sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SourceContent> SourceContents => Set<SourceContent>();
    public DbSet<SourceFolder> SourceFolders => Set<SourceFolder>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<ConversationRead> ConversationReads => Set<ConversationRead>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<ConversationLabel> ConversationLabels => Set<ConversationLabel>();
    public DbSet<AgentConfig> Agents => Set<AgentConfig>();
    public DbSet<AgentApiKey> AgentApiKeys => Set<AgentApiKey>();
    public DbSet<McpServer> McpServers => Set<McpServer>();
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<WidgetAsset> WidgetAssets => Set<WidgetAsset>();
    public DbSet<WidgetVisitor> WidgetVisitors => Set<WidgetVisitor>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactAttributeDefinition> ContactAttributes => Set<ContactAttributeDefinition>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<WidgetDailyUsage> WidgetDailyUsage => Set<WidgetDailyUsage>();
    public DbSet<Feedback> Feedback => Set<Feedback>();
    public DbSet<Handover> Handovers => Set<Handover>();
    public DbSet<HandoverInbound> HandoverInbound => Set<HandoverInbound>();
    public DbSet<IntegrationSettingsRecord> IntegrationSettings => Set<IntegrationSettingsRecord>();
    public DbSet<ModelProfileOverride> ModelProfileOverrides => Set<ModelProfileOverride>();
    public DbSet<ProviderSettingsOverride> ProviderSettingsOverrides => Set<ProviderSettingsOverride>();
    public DbSet<EvalRunRecord> EvalRuns => Set<EvalRunRecord>();
    public DbSet<CustomDatasetRecord> EvalDatasets => Set<CustomDatasetRecord>();
    public DbSet<CustomDatasetItemRecord> EvalDatasetItems => Set<CustomDatasetItemRecord>();
    public DbSet<AlertSnooze> AlertSnoozes => Set<AlertSnooze>();
    public DbSet<AlertState> AlertStates => Set<AlertState>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<SourceType>().HaveConversion<string>();
        configurationBuilder.Properties<SourceStatus>().HaveConversion<string>();
        configurationBuilder.Properties<SourceStage>().HaveConversion<string>();
        configurationBuilder.Properties<ConversationChannel>().HaveConversion<string>();
        configurationBuilder.Properties<ConversationStatus>().HaveConversion<string>();
        configurationBuilder.Properties<MessageAuthor>().HaveConversion<string>();
        configurationBuilder.Properties<FeedbackKind>().HaveConversion<string>();
        configurationBuilder.Properties<FeedbackAuthor>().HaveConversion<string>();
        configurationBuilder.Properties<HandoverStatus>().HaveConversion<string>();
        configurationBuilder.Properties<HandoverSource>().HaveConversion<string>();
        configurationBuilder.Properties<LabelSource>().HaveConversion<string>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<string>().HaveConversion<NulStrippingStringConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // EF caches one model per database provider, so provider-specific store types are safe here.
        var isNpgsql = Database.IsNpgsql();
        if (isNpgsql)
        {
            modelBuilder.HasPostgresExtension("pg_trgm");
        }

        modelBuilder.ApplyConfiguration(new SourceConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new SourceContentConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new SourceFolderConfiguration());
        modelBuilder.ApplyConfiguration(new ConversationConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new ConversationTurnConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new ConversationMessageConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new ConversationReadConfiguration());
        modelBuilder.ApplyConfiguration(new StaffMemberConfiguration());
        modelBuilder.ApplyConfiguration(new LabelConfiguration());
        modelBuilder.ApplyConfiguration(new ConversationLabelConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new AgentApiKeyConfiguration());
        modelBuilder.ApplyConfiguration(new McpServerConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new WidgetConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new WidgetAssetConfiguration());
        modelBuilder.ApplyConfiguration(new WidgetVisitorConfiguration());
        modelBuilder.ApplyConfiguration(new ContactConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new ContactAttributeDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new SegmentConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new WidgetDailyUsageConfiguration());
        modelBuilder.ApplyConfiguration(new FeedbackConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new HandoverConfiguration());
        modelBuilder.ApplyConfiguration(new HandoverInboundConfiguration());
        modelBuilder.ApplyConfiguration(new IntegrationSettingsConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new ModelProfileOverrideConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderSettingsOverrideConfiguration());
        modelBuilder.ApplyConfiguration(new EvalRunConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new CustomDatasetConfiguration());
        modelBuilder.ApplyConfiguration(new CustomDatasetItemConfiguration(isNpgsql));
        modelBuilder.ApplyConfiguration(new AlertSnoozeConfiguration());
        modelBuilder.ApplyConfiguration(new AlertStateConfiguration());
        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        if (Database.IsRelational())
        {
            SnakeCaseNaming.Apply(modelBuilder.Model);
        }
    }
}
