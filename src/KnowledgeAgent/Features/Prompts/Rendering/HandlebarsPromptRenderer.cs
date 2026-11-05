using System.Globalization;
using HandlebarsDotNet;
using HandlebarsDotNet.Features;

namespace KnowledgeAgent.Features.Prompts.Rendering;

internal sealed class HandlebarsPromptRenderer : IPromptTemplateRenderer
{
    internal const int DefaultCacheCapacity = 256;

    private readonly IHandlebars _handlebars;
    private readonly LruCache<string, HandlebarsTemplate<object, object>> _templates;

    public HandlebarsPromptRenderer(TimeProvider timeProvider)
        : this(timeProvider, DefaultCacheCapacity)
    {
    }

    internal HandlebarsPromptRenderer(TimeProvider timeProvider, int cacheCapacity)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var configuration = new HandlebarsConfiguration
        {
            NoEscape = true,
            ThrowOnUnresolvedBindingExpression = false,
            FormatProvider = CultureInfo.InvariantCulture,
        };
        configuration.FormatterProviders.Add(new InvariantValueFormatterProvider());
        configuration.UseCollectionMemberAliasProvider(); // Supports {{items.length}} as in JavaScript.

        _handlebars = Handlebars.Create(configuration);
        new TemplateHelpers(timeProvider).Register(_handlebars);
        _templates = new LruCache<string, HandlebarsTemplate<object, object>>(cacheCapacity, StringComparer.Ordinal);
    }

    internal int CachedTemplateCount => _templates.Count;

    public string Render(string template, IReadOnlyDictionary<string, object?> variables)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        if (template.Length == 0)
        {
            return string.Empty;
        }

        var compiled = GetOrCompile(template);
        var context = TemplateValueConverter.ConvertVariables(variables);
        try
        {
            return compiled(context, null!);
        }
        catch (PromptRenderException)
        {
            throw;
        }
        catch (Exception ex) when (ex.InnerException is PromptRenderException inner)
        {
            throw new PromptRenderException(inner.Message, ex);
        }
        catch (Exception ex)
        {
            throw new PromptRenderException($"Template rendering failed: {Describe(ex)}", ex);
        }
    }

    public IReadOnlyList<string> ExtractVariables(string template) => TemplateVariableExtractor.Extract(template);

    // Compiles without rendering, so helpers that need values do not fail a template that is fine.
    public void Validate(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Length > 0)
        {
            GetOrCompile(template);
        }
    }

    private HandlebarsTemplate<object, object> GetOrCompile(string template)
    {
        if (_templates.TryGet(template, out var cached))
        {
            return cached;
        }

        if (TemplateSyntax.FindProblem(template) is { } problem)
        {
            throw new PromptRenderException($"Template syntax error: {problem}.");
        }

        HandlebarsTemplate<object, object> compiled;
        try
        {
            compiled = _handlebars.Compile(template);
        }
        catch (Exception ex)
        {
            // Handlebars.Net reports some malformed expressions, such as "{{#if}}", as bare LINQ errors.
            var reason = ex is HandlebarsException
                ? Describe(ex)
                : "the template contains an incomplete expression (for example a block helper without arguments)";
            throw new PromptRenderException($"Template syntax error: {reason}", ex);
        }

        _templates.Set(template, compiled);
        return compiled;
    }

    private static string Describe(Exception exception) =>
        string.Join(' ', exception.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
