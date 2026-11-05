using KnowledgeAgent.Features.Search.Reranking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Search;

public static class SearchModule
{
    public static IHostApplicationBuilder AddSearch(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSearch(builder.Configuration.GetSection(SearchOptions.SectionName));
        return builder;
    }

    internal static IServiceCollection AddSearch(this IServiceCollection services, IConfiguration? section = null)
    {
        var options = services.AddOptions<SearchOptions>();
        if (section is not null)
        {
            options.Bind(section);
        }

        options
            .Validate(o => SearchModes.Normalize(o.Mode) is not null, "Search:Mode must be hybrid, vector or keyword.")
            .Validate(o => o.CandidatePool is >= 1 and <= KnowledgeSearch.MaxCandidatePool, "Search:CandidatePool must be between 1 and 100.")
            .Validate(o => o.RrfK >= 1, "Search:RrfK must be at least 1.")
            .Validate(o => o.VectorWeight >= 0 && o.KeywordWeight >= 0 && o.VectorWeight + o.KeywordWeight > 0,
                "Search:VectorWeight and Search:KeywordWeight must not be negative, and at least one must be above 0.")
            .Validate(o => o.RerankCandidates is >= 1 and <= KnowledgeSearch.MaxCandidatePool, "Search:RerankCandidates must be between 1 and 100.")
            .ValidateOnStart();

        services.TryAddSingleton<IRerankService, RerankService>();
        services.TryAddScoped<IKnowledgeSearch, KnowledgeSearch>();
        services.TryAddScoped<IExactSearch, ExactSearch>();
        return services;
    }
}
