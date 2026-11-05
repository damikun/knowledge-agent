using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Infrastructure.AI.Configuration;

internal sealed class ModelProfileOverrideConfiguration : IEntityTypeConfiguration<ModelProfileOverride>
{
    public void Configure(EntityTypeBuilder<ModelProfileOverride> builder)
    {
        builder.ToTable("model_profile_overrides");
        builder.HasKey(profileOverride => profileOverride.Name);
    }
}

internal sealed class ProviderSettingsOverrideConfiguration : IEntityTypeConfiguration<ProviderSettingsOverride>
{
    public void Configure(EntityTypeBuilder<ProviderSettingsOverride> builder)
    {
        builder.ToTable("provider_settings_overrides");
        builder.HasKey(settings => settings.Key);
    }
}
