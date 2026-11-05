using System.Collections.Frozen;
using static KnowledgeAgent.Features.Prompts.Rendering.TemplateSyntax;

namespace KnowledgeAgent.Features.Prompts.Rendering;

internal static class TemplateVariableExtractor
{
    private static readonly FrozenSet<string> ContextBlocks = FrozenSet.ToFrozenSet(["each", "with"], StringComparer.Ordinal);

    public static IReadOnlyList<string> Extract(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var walker = new Walker();
        foreach (var tag in ReadTags(template))
        {
            walker.Process(tag);
        }

        return walker.Names.Order(StringComparer.Ordinal).ToArray();
    }

    private sealed class Walker
    {
        private readonly Stack<Scope> _scopes = new();

        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        private int ContextDepth => _scopes.Count(s => s.ChangesContext);

        public void Process(string rawTag)
        {
            var tag = TrimWhitespaceControl(rawTag);
            if (tag.Length == 0)
            {
                return;
            }

            switch (tag[0])
            {
                case '!': // comment
                case '>': // partial
                    return;
                case '#':
                    OpenBlock(tag[1..].Trim());
                    return;
                case '^':
                    InverseSection(tag[1..].Trim());
                    return;
                case '/':
                    if (_scopes.Count > 0)
                    {
                        _scopes.Pop();
                    }

                    return;
                case '&':
                    Mustache(tag[1..].Trim());
                    return;
                default:
                    Mustache(tag);
                    return;
            }
        }

        private void OpenBlock(string content)
        {
            // Partial blocks {{#> layout}} and decorator blocks {{#* inline}} only need to be balanced.
            if (content.StartsWith('>') || content.StartsWith('*'))
            {
                _scopes.Push(new Scope(false));
                return;
            }

            var tokens = Tokenize(content);
            if (tokens.Count == 0 || tokens[0] is not PathToken head)
            {
                _scopes.Push(new Scope(false));
                return;
            }

            if (TemplateHelpers.AllHelperNames.Contains(head.Text) || tokens.Count > 1)
            {
                VisitAll(tokens.Skip(1));
                _scopes.Push(new Scope(ContextBlocks.Contains(head.Text)));
                return;
            }

            // A mustache section such as {{#items}} iterates or enters the value, so it changes the context.
            VisitPath(head.Text);
            _scopes.Push(new Scope(true));
        }

        private void InverseSection(string content)
        {
            if (content.Length == 0)
            {
                Else([]); // {{^}} is an alias of {{else}}.
                return;
            }

            var tokens = Tokenize(content);
            if (tokens.Count == 1)
            {
                Visit(tokens[0]);
            }
            else
            {
                VisitAll(tokens.Skip(1));
            }

            _scopes.Push(new Scope(false));
        }

        private void Mustache(string content)
        {
            var tokens = Tokenize(content);
            if (tokens.Count == 0)
            {
                return;
            }

            if (tokens[0] is PathToken { Text: "else" })
            {
                Else(tokens.Skip(1).ToList());
                return;
            }

            if (tokens.Count == 1)
            {
                if (tokens[0] is PathToken single && TemplateHelpers.AllHelperNames.Contains(single.Text))
                {
                    return; // A helper without arguments, such as {{date}}.
                }

                Visit(tokens[0]);
                return;
            }

            if (tokens[0] is SubExpressionToken)
            {
                Visit(tokens[0]);
            }

            VisitAll(tokens.Skip(1));
        }

        // {{else}} renders in the enclosing context, even for #each and #with. A chained {{else each x}} enters a new one.
        private void Else(IReadOnlyList<Token> rest)
        {
            if (_scopes.TryPeek(out var current))
            {
                current.ChangesContext = false;
            }

            if (rest.Count == 0)
            {
                return;
            }

            VisitAll(rest.Skip(1));
            if (current is not null && rest[0] is PathToken chained && ContextBlocks.Contains(chained.Text))
            {
                current.ChangesContext = true;
            }
        }

        private void VisitAll(IEnumerable<Token> tokens)
        {
            foreach (var token in tokens)
            {
                Visit(token);
            }
        }

        private void Visit(Token token)
        {
            switch (token)
            {
                case PathToken path:
                    VisitPath(path.Text);
                    break;
                case SubExpressionToken subExpression when subExpression.Tokens.Count > 0:
                    if (subExpression.Tokens[0] is SubExpressionToken)
                    {
                        Visit(subExpression.Tokens[0]);
                    }

                    VisitAll(subExpression.Tokens.Skip(1));
                    break;
                case HashToken { Value: { } value }:
                    Visit(value);
                    break;
            }
        }

        private void VisitPath(string path)
        {
            if (path.Length == 0)
            {
                return;
            }

            if (path[0] == '@')
            {
                // Data variables such as @index are skipped, but @root.x reads the top-level x.
                if (path.StartsWith("@root.", StringComparison.Ordinal) || path.StartsWith("@root/", StringComparison.Ordinal))
                {
                    AddFirstSegment(path[6..]);
                }

                return;
            }

            var levelsUp = 0;
            var rest = path;
            while (rest.StartsWith("../", StringComparison.Ordinal))
            {
                levelsUp++;
                rest = rest[3..];
            }

            if (rest is "this" or "." or "..")
            {
                return;
            }

            if (rest.StartsWith("this.", StringComparison.Ordinal) || rest.StartsWith("this/", StringComparison.Ordinal))
            {
                rest = rest[5..];
            }
            else if (rest.StartsWith("./", StringComparison.Ordinal))
            {
                rest = rest[2..];
            }

            // Inside blocks that change the context, such as #each, names refer to the current item.
            if (ContextDepth - levelsUp <= 0)
            {
                AddFirstSegment(rest);
            }
        }

        private void AddFirstSegment(string path)
        {
            string first;
            if (path.StartsWith('['))
            {
                var end = path.IndexOf(']');
                first = end < 0 ? path[1..] : path[1..end];
            }
            else
            {
                var end = path.IndexOfAny(['.', '/', '[']);
                first = end < 0 ? path : path[..end];
            }

            if (first.Length > 0 && first is not ("this" or "else") && !LiteralKeywords.Contains(first) && !IsNumber(first))
            {
                Names.Add(first);
            }
        }
    }

    private sealed class Scope(bool changesContext)
    {
        public bool ChangesContext { get; set; } = changesContext;
    }
}
