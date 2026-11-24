using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Larmcentralen.Client.UI.Services;

/// <summary>
/// The text of an åtgärd (Solution.Content). New and edited åtgärder are Markdown; older ones are HTML from Quill
/// and stay readable as they are until someone edits them. Content counts as legacy HTML when the trimmed text
/// starts with "&lt;".
/// </summary>
public static partial class SolutionContent
{
    /// <summary>Markdown with raw HTML switched off: any HTML in the text is shown as text, never run.</summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseTaskLists()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    private static readonly ReverseMarkdown.Converter HtmlConverter = CreateHtmlConverter();

    private static ReverseMarkdown.Converter CreateHtmlConverter()
    {
        var config = new ReverseMarkdown.Config
        {
            // GitHub flavour (tables, ~~strike~~, fenced code). Not Flavor = MarkdownFlavor.GitHub: in 6.3.0 that keeps
            // headings, emphasis and tables as HTML.
#pragma warning disable CS0618
            GithubFlavored = true,
#pragma warning restore CS0618
            // Tags without a Markdown form (Quill's <u>, <span>, ...) are dropped and their text kept, so no raw HTML
            // ends up in the Markdown and nothing that was written is lost.
            Tags = { Unknown = ReverseMarkdown.Config.UnknownTagsOption.Bypass },
            Formatting = { RemoveComments = true },
            Links = { SmartHref = true },
        };
        config.Preprocess.RemoveScripts().RemoveStyles();
        return new ReverseMarkdown.Converter(config);
    }

    public static bool IsLegacyHtml(string? content) => content is not null && content.TrimStart().StartsWith('<');

    /// <summary>True when there is nothing to show: blank, an empty Quill document, or Markdown with no text.</summary>
    public static bool IsEmpty(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return true;
        if (IsLegacyHtml(content)) return Format.IsEmptyHtml(content);
        return string.IsNullOrWhiteSpace(content.Replace('\u00A0', ' ').Replace("\u200B", ""));
    }

    /// <summary>The text for the editor: Markdown as it is, legacy HTML converted to Markdown.</summary>
    public static string ToEditorMarkdown(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";
        return IsLegacyHtml(content) ? HtmlToMarkdown(content) : content;
    }

    /// <summary>Converts Quill HTML to GitHub-flavoured Markdown.</summary>
    public static string HtmlToMarkdown(string html)
    {
        if (Format.IsEmptyHtml(html)) return "";
        // Quill 2.0.3 wrote spaces as &nbsp; (and real non-breaking spaces turn up in pasted text): plain spaces.
        var cleaned = html.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase).Replace('\u00A0', ' ');
        var markdown = HtmlConverter.Convert(cleaned).Replace('\u00A0', ' ').ReplaceLineEndings("\n");
        // Whitespace-only lines become empty and runs of blank lines one. (Two trailing spaces after text are a
        // line break from <br>, so those stay.)
        markdown = TrailingSpaces().Replace(markdown, "");
        markdown = BlankLines().Replace(markdown, "\n\n");
        return markdown.Trim();
    }

    /// <summary>
    /// HTML for the reader. Markdown is rendered with raw HTML off and only http, https and relative URLs;
    /// a Markdown image of an .mp4 or .webm file becomes a video with controls. Legacy HTML is returned as stored.
    /// </summary>
    public static string ToHtml(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";
        if (IsLegacyHtml(content)) return content;

        var document = Markdown.Parse(content, Pipeline);
        RemoveUnsafeLinks(document);

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<LinkInlineRenderer>(new VideoAwareLinkRenderer());
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>http, https and relative URLs only. Refuses javascript:, data:, mailto:, protocol-relative "//host" and the rest.</summary>
    public static bool IsAllowedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        // Browsers ignore control characters and spaces inside a scheme ("java\tscript:").
        var compact = ControlAndSpace().Replace(WebUtility.HtmlDecode(url.Trim()), "");
        var scheme = Scheme().Match(compact);
        if (scheme.Success)
            return scheme.Groups[1].Value.Equals("http", StringComparison.OrdinalIgnoreCase)
                   || scheme.Groups[1].Value.Equals("https", StringComparison.OrdinalIgnoreCase);
        return !compact.StartsWith("//", StringComparison.Ordinal) && !compact.StartsWith(@"\\", StringComparison.Ordinal)
               && !compact.StartsWith(@"/\", StringComparison.Ordinal) && !compact.StartsWith(@"\/", StringComparison.Ordinal);
    }

    /// <summary>True for a URL whose path ends in .mp4 or .webm (query and fragment ignored).</summary>
    public static bool IsVideoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var path = url.Split('?', '#')[0];
        return path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Links with a refused URL become their plain text; images with one are removed.</summary>
    private static void RemoveUnsafeLinks(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (IsAllowedUrl(link.GetDynamicUrl?.Invoke() ?? link.Url)) continue;
            if (link.IsImage)
            {
                link.Remove();
                continue;
            }
            // Keep the words, lose the link.
            var children = link.ToList();
            foreach (var child in children)
            {
                child.Remove();
                link.InsertBefore(child);
            }
            link.Remove();
        }

        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (!autolink.IsEmail && IsAllowedUrl(autolink.Url)) continue;
            autolink.ReplaceBy(new LiteralInline(autolink.Url));
        }
    }

    /// <summary>Renders ![text](film.mp4) as a video with controls; every other link or image as usual.</summary>
    private sealed class VideoAwareLinkRenderer : LinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            var url = link.GetDynamicUrl?.Invoke() ?? link.Url;
            if (!link.IsImage || !IsVideoUrl(url))
            {
                base.Write(renderer, link);
                return;
            }

            renderer.Write("<video controls preload=\"metadata\" src=\"");
            renderer.WriteEscapeUrl(url);
            renderer.Write('"');
            if (!string.IsNullOrEmpty(link.Title))
            {
                renderer.Write(" title=\"");
                renderer.WriteEscape(link.Title);
                renderer.Write('"');
            }
            renderer.Write('>');
            // Fallback text (the alt text) for a WebView that can't play the file.
            renderer.WriteChildren(link);
            renderer.Write("</video>");
        }
    }

    [GeneratedRegex(@"^[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"[\u0000-\u0020\u007F-\u009F]")]
    private static partial Regex ControlAndSpace();

    [GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.\-]*):")]
    private static partial Regex Scheme();
}
