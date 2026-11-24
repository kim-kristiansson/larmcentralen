using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Larmcentralen.Client.UI.Services;

/// <summary>
/// The text of an åtgärd (Solution.Content): always Markdown, written with the Milkdown editor. It is rendered with
/// Markdig with raw HTML switched off, so anything that looks like HTML (also at the very start) is shown as text.
/// </summary>
public static partial class SolutionContent
{
    /// <summary>
    /// Only what the editor can produce: GFM pipe tables (with column alignment), ~~strikethrough~~, task lists,
    /// autolinks and GitHub alerts (the Fara, Varning and Info boxes). Raw HTML is off.
    /// </summary>
    /// <remarks>
    /// Alerts: "&gt; [!CAUTION]" is the Fara box (risk of injury), "&gt; [!WARNING]" Varning and "&gt; [!NOTE]" Info.
    /// They render as &lt;div class="markdown-alert markdown-alert-caution|warning|note"&gt; without Markdig's English
    /// title; the look is in app.css. A future Word export should map [!CAUTION] to the Word style "Fara",
    /// [!WARNING] to "Varning" and [!NOTE] to "Info" (TIP and IMPORTANT like NOTE).
    /// </remarks>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAlertBlocks(renderKind: static (_, _) => { })
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    /// <summary>True when there is nothing to show: no content, or only whitespace (non-breaking and zero-width too).</summary>
    public static bool IsEmpty(string? content)
        => string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(content.Replace('\u00A0', ' ').Replace("\u200B", ""));

    /// <summary>
    /// HTML for the reader. Raw HTML is escaped, only http, https and relative URLs are kept in links and images, and
    /// a Markdown image of an .mp4 or .webm file becomes a video with controls.
    /// </summary>
    public static string ToHtml(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";

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
            foreach (var child in link.ToList())
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

    [GeneratedRegex(@"[\u0000-\u0020\u007F-\u009F]")]
    private static partial Regex ControlAndSpace();

    [GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.\-]*):")]
    private static partial Regex Scheme();
}
