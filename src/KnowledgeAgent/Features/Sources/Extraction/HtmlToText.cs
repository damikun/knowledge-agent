using System.Globalization;
using System.Text;
using AngleSharp.Dom;

namespace KnowledgeAgent.Features.Sources.Extraction;

internal sealed class HtmlToText
{
    private static readonly HashSet<string> NonContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "head", "title", "meta", "link", "base",
        "svg", "math", "canvas", "iframe", "frame", "frameset", "object", "embed", "applet",
        "audio", "video", "source", "track", "picture", "img", "map", "area",
        "button", "select", "option", "optgroup", "input", "textarea", "datalist", "output", "progress", "meter",
    };

    private static readonly HashSet<string> BoilerplateTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "nav", "aside", "footer", "dialog", "menu",
    };

    private static readonly HashSet<string> ParagraphTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "dl", "table", "blockquote", "pre", "figure", "hr",
        "address", "header", "details", "fieldset",
    };

    private static readonly HashSet<string> LineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "div", "section", "article", "main", "body", "li", "dt", "dd", "tr", "caption", "figcaption", "summary",
        "legend", "center",
    };

    private static readonly HashSet<string> BoilerplateRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "navigation", "banner", "contentinfo", "complementary", "search", "dialog", "alertdialog", "menu", "menubar",
        "toolbar",
    };

    private static readonly HashSet<string> BoilerplateWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cookie", "cookies", "consent", "gdpr", "newsletter", "subscribe", "paywall", "breadcrumb", "breadcrumbs",
        "advert", "advertisement", "ad", "ads", "sponsored", "promo", "sidebar", "share", "sharing", "social",
        "related", "comments", "popup", "modal", "overlay", "nav", "navbar", "navigation", "menu", "footer", "skip",
        "sr", "visually",
    };

    // Exempt from the class and id heuristic because body classes such as "has-sidebar" are common.
    private static readonly HashSet<string> StructuralTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "main", "article",
    };

    private readonly INode _root;
    private readonly StringBuilder _builder = new();
    private readonly Stack<int> _orderedListCounters = new();
    private int _pendingBreaks;
    private bool _pendingSpace;
    private int _preformattedDepth;
    private int? _rootTextLength;

    private HtmlToText(INode root) => _root = root;

    public static string Convert(INode root)
    {
        var converter = new HtmlToText(root);
        converter.Walk();
        return converter._builder.ToString();
    }

    // Iterative so that deeply nested markup cannot overflow the stack.
    private void Walk()
    {
        var stack = new Stack<(INode Node, bool Exit)>();
        stack.Push((_root, false));
        while (stack.Count > 0)
        {
            var (node, exit) = stack.Pop();
            switch (node)
            {
                case IText text:
                    AppendText(text.Data);
                    break;
                case IElement element when exit:
                    Leave(element);
                    break;
                case IElement element:
                    if (!ReferenceEquals(element, _root) && ShouldSkip(element))
                    {
                        break;
                    }

                    Enter(element);
                    stack.Push((element, true));
                    PushChildren(stack, element);
                    break;
                case IDocument or IDocumentFragment when !exit:
                    PushChildren(stack, node);
                    break;
            }
        }
    }

    private static void PushChildren(Stack<(INode Node, bool Exit)> stack, INode parent)
    {
        for (var child = parent.LastChild; child is not null; child = child.PreviousSibling)
        {
            stack.Push((child, false));
        }
    }

    private void Enter(IElement element)
    {
        var name = element.LocalName;
        switch (name)
        {
            case "br":
                LineBreak();
                return;
            case "pre":
                Break(2);
                _preformattedDepth++;
                return;
            case "ol":
                Break(2);
                _orderedListCounters.Push(ParseStart(element));
                return;
            case "li":
                Break(1);
                AppendMarker(element);
                return;
            case "td" or "th":
                if (element.PreviousElementSibling is { LocalName: "td" or "th" })
                {
                    AppendText(" | ");
                }
                else
                {
                    _pendingSpace = _builder.Length > 0 && _pendingBreaks == 0;
                }

                return;
        }

        if (ParagraphTags.Contains(name))
        {
            Break(2);
        }
        else if (LineTags.Contains(name))
        {
            Break(1);
        }
    }

    private void Leave(IElement element)
    {
        var name = element.LocalName;
        switch (name)
        {
            case "pre":
                _preformattedDepth--;
                Break(2);
                return;
            case "ol":
                _orderedListCounters.TryPop(out _);
                Break(2);
                return;
        }

        if (ParagraphTags.Contains(name))
        {
            Break(2);
        }
        else if (LineTags.Contains(name))
        {
            Break(1);
        }
    }

    private void AppendMarker(IElement listItem)
    {
        if (listItem.ParentElement is { LocalName: "ol" } && _orderedListCounters.TryPop(out var number))
        {
            _orderedListCounters.Push(number + 1);
            AppendText(number.ToString(CultureInfo.InvariantCulture) + ". ");
        }
        else
        {
            AppendText("- ");
        }
    }

    private static int ParseStart(IElement orderedList) =>
        int.TryParse(orderedList.GetAttribute("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ? start : 1;

    private bool ShouldSkip(IElement element)
    {
        var name = element.LocalName;
        if (NonContentTags.Contains(name) || IsHidden(element))
        {
            return true;
        }

        if (BoilerplateTags.Contains(name)
            || (name == "header" && !HasAncestor(element, "article", "main"))
            || (element.GetAttribute("role") is { } role && BoilerplateRoles.Contains(role.Trim())))
        {
            return true;
        }

        // A match that holds most of the page text is the page itself: ASP.NET WebForms wraps whole pages in a <form>.
        var heuristic = name == "form" || (!StructuralTags.Contains(name) && HasBoilerplateMarker(element));
        return heuristic && TextLength(element) <= RootTextLength / 2;
    }

    private int RootTextLength => _rootTextLength ??= TextLength(_root);

    // Not TextContent, which recurses and can overflow the stack on deeply nested documents.
    internal static int TextLength(INode node)
    {
        var length = 0;
        var stack = new Stack<INode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is IText text)
            {
                length += text.Data.Length;
                continue;
            }

            for (var child = current.FirstChild; child is not null; child = child.NextSibling)
            {
                stack.Push(child);
            }
        }

        return length;
    }

    internal static int MaxDepth(INode node)
    {
        var max = 0;
        var stack = new Stack<(INode Node, int Depth)>();
        stack.Push((node, 0));
        while (stack.Count > 0)
        {
            var (current, depth) = stack.Pop();
            max = Math.Max(max, depth);
            for (var child = current.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is IElement)
                {
                    stack.Push((child, depth + 1));
                }
            }
        }

        return max;
    }

    private static bool IsHidden(IElement element)
    {
        if (element.HasAttribute("hidden")
            || string.Equals(element.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style))
        {
            return false;
        }

        var compact = string.Concat(style.Where(c => !char.IsWhiteSpace(c)));
        return compact.Contains("display:none", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAncestor(IElement element, string first, string second)
    {
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (parent.LocalName == first || parent.LocalName == second)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBoilerplateMarker(IElement element) =>
        ContainsBoilerplateWord(element.Id) || ContainsBoilerplateWord(element.ClassName);

    private static bool ContainsBoilerplateWord(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var word in value.Split([' ', '\t', '\n', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (BoilerplateWords.Contains(word))
            {
                return true;
            }
        }

        return false;
    }

    private void Break(int count)
    {
        if (_builder.Length == 0)
        {
            return;
        }

        _pendingBreaks = Math.Max(_pendingBreaks, count);
        _pendingSpace = false;
    }

    private void LineBreak()
    {
        if (_builder.Length == 0)
        {
            return;
        }

        _pendingBreaks = Math.Min(2, _pendingBreaks + 1);
        _pendingSpace = false;
    }

    private void AppendText(string text)
    {
        foreach (var c in text)
        {
            if (_preformattedDepth > 0)
            {
                if (c == '\n')
                {
                    LineBreak();
                }
                else if (c != '\r')
                {
                    Flush();
                    _builder.Append(c);
                }

                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                _pendingSpace = _builder.Length > 0 && _pendingBreaks == 0;
                continue;
            }

            Flush();
            _builder.Append(c);
        }
    }

    private void Flush()
    {
        if (_pendingBreaks > 0)
        {
            _builder.Append('\n', _pendingBreaks);
            _pendingBreaks = 0;
            _pendingSpace = false;
        }
        else if (_pendingSpace)
        {
            _builder.Append(' ');
            _pendingSpace = false;
        }
    }
}
