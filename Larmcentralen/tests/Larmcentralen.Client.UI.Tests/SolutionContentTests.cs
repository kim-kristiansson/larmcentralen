using Larmcentralen.Client.UI.Services;

namespace Larmcentralen.Client.UI.Tests;

public class SolutionContentTests
{
    private const char Nbsp = (char)0xA0;

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  \n\n ", true)]
    [InlineData("1. Stäng av ugnen", false)]
    [InlineData("<p></p>", false)]
    public void Empty_content_is_blank_or_whitespace_only(string? content, bool expected)
        => Assert.Equal(expected, SolutionContent.IsEmpty(content));

    [Fact]
    public void Non_breaking_spaces_alone_count_as_empty()
        => Assert.True(SolutionContent.IsEmpty($" {Nbsp}{Nbsp} "));

    [Fact]
    public void Text_starting_with_a_less_than_sign_is_markdown_not_html()
    {
        // Content is always Markdown: a leading "<" must neither switch off the formatting nor pass through as HTML.
        var html = SolutionContent.ToHtml("<5 mA betyder trasig givare.\n\n- Byt givaren\n- **Kalibrera**");
        Assert.StartsWith("<p>&lt;5 mA betyder trasig givare.</p>", html);
        Assert.Contains("<li><strong>Kalibrera</strong></li>", html);
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)> står det i loggen.")]
    [InlineData("Loggen: <img src=x onerror=alert(1)>")]
    [InlineData("<svg onload=alert(1)></svg>")]
    [InlineData("<h2>Steg</h2><ol><li>Ett</li></ol>")]
    public void Html_tags_are_escaped_wherever_they_are(string markdown)
    {
        var html = SolutionContent.ToHtml(markdown);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<svg", html);
        Assert.DoesNotContain("<h2>", html);
        Assert.Contains("&lt;", html);
    }

    [Theory]
    [InlineData("==markerat==", "<mark>")]
    [InlineData("++infogat++", "<ins>")]
    [InlineData("x^2^", "<sup>")]
    [InlineData("H~2~O", "<sub>")]
    public void Only_the_markdown_the_editor_can_write_is_rendered(string markdown, string tag)
        => Assert.DoesNotContain(tag, SolutionContent.ToHtml(markdown));

    [Fact]
    public void Strikethrough_from_the_editor_is_rendered()
        => Assert.Contains("<del>gammalt</del>", SolutionContent.ToHtml("~~gammalt~~"));
    [Fact]
    public void Markdown_renders_tables_strike_and_task_lists()
    {
        var html = SolutionContent.ToHtml("| A | B |\n|---|---|\n| 1 | 2 |\n\n~~gammalt~~\n\n- [x] Klart\n- [ ] Kvar");
        Assert.Contains("<table>", html);
        Assert.Contains("<del>gammalt</del>", html);
        Assert.Contains("type=\"checkbox\"", html);
    }

    [Fact]
    public void Table_column_alignment_from_the_editor_is_rendered()
    {
        // What the editor writes after "Centrera" and "Höger" on the second and third columns.
        var html = SolutionContent.ToHtml("| Steg | Värde | Enhet |\n| :--- | :---: | ----: |\n| 1 | 20 | mA |");
        Assert.Contains("<td style=\"text-align: center;\">20</td>", html);
        Assert.Contains("<td style=\"text-align: right;\">mA</td>", html);
    }

    [Fact]
    public void A_table_with_only_its_header_row_still_renders()
    {
        // The editor always keeps one row under the header, but pasted Markdown can have just the header.
        var html = SolutionContent.ToHtml("| Steg | Värde |\n| :- | :- |");
        Assert.Contains("<th", html);
        Assert.Contains("Steg", html);
    }

    [Fact]
    public void Raw_html_in_markdown_is_escaped_not_run()
    {
        var html = SolutionContent.ToHtml("Hej\n\n<script>alert('x')</script>\n\nText med <img src=x onerror=alert(1)> inuti");
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("[klicka](javascript:alert(1))")]
    [InlineData("[klicka](JaVaScRiPt:alert(1))")]
    [InlineData("[klicka](java&#09;script:alert(1))")]
    [InlineData("[klicka](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[klicka](vbscript:msgbox)")]
    [InlineData("[klicka](//evil.example/x)")]
    [InlineData("Se <javascript:alert(1)> nu")]
    public void Unsafe_links_are_removed_but_their_text_is_kept(string markdown)
    {
        var html = SolutionContent.ToHtml(markdown);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("href", html);
        Assert.DoesNotContain("javascript:alert(1)\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsafe_link_text_survives()
        => Assert.Contains("klicka", SolutionContent.ToHtml("[klicka](javascript:alert(1))"));

    [Theory]
    [InlineData("[manual](https://example.com/a.pdf)", "https://example.com/a.pdf")]
    [InlineData("[manual](http://example.com/a.pdf)", "http://example.com/a.pdf")]
    [InlineData("[larm](/larm/12)", "/larm/12")]
    [InlineData("[larm](larm/12)", "larm/12")]
    public void Http_https_and_relative_links_are_kept(string markdown, string href)
        => Assert.Contains($"href=\"{href}\"", SolutionContent.ToHtml(markdown));

    [Fact]
    public void Images_with_an_unsafe_url_are_removed()
    {
        var html = SolutionContent.ToHtml("![bild](javascript:alert(1)) ![bild](data:image/png;base64,AAAA)");
        Assert.DoesNotContain("<img", html);
    }

    [Theory]
    [InlineData("![Byte av termoelement](film/termoelement.mp4)", "film/termoelement.mp4")]
    [InlineData("![Video](https://example.com/v/steg.WEBM?t=3)", "https://example.com/v/steg.WEBM?t=3")]
    public void An_mp4_or_webm_image_becomes_a_video_with_controls(string markdown, string src)
    {
        var html = SolutionContent.ToHtml(markdown);
        Assert.Contains("<video controls", html);
        Assert.Contains($"src=\"{src}\"", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("</video>", html);
    }

    [Fact]
    public void Video_alt_text_is_escaped()
    {
        var html = SolutionContent.ToHtml("![<b>x</b>](a.mp4)");
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void An_ordinary_image_stays_an_image()
    {
        var html = SolutionContent.ToHtml("![Skåp](bilder/skap.png)");
        Assert.Contains("<img src=\"bilder/skap.png\"", html);
        Assert.DoesNotContain("<video", html);
    }

    [Fact]
    public void A_video_link_that_is_not_an_image_stays_a_link()
    {
        var html = SolutionContent.ToHtml("[film](film.mp4)");
        Assert.Contains("<a href=\"film.mp4\"", html);
        Assert.DoesNotContain("<video", html);
    }

    [Theory]
    [InlineData("> [!CAUTION]\n> Het yta, risk för brännskada.", "markdown-alert-caution")]
    [InlineData("> [!WARNING]\n> Bryt strömmen innan skåpet öppnas!", "markdown-alert-warning")]
    [InlineData("> [!NOTE]\n> Ugnen behöver 10 minuter för att svalna.", "markdown-alert-note")]
    public void Fara_varning_and_info_boxes_render_as_alerts_without_marker_or_title(string markdown, string cssClass)
    {
        var html = SolutionContent.ToHtml(markdown);
        Assert.Contains($"class=\"markdown-alert {cssClass}\"", html);
        Assert.DoesNotContain("[!", html);
        Assert.DoesNotContain("markdown-alert-title", html);
        Assert.DoesNotContain("<svg", html);
        Assert.DoesNotContain("<blockquote", html);
    }

    [Fact]
    public void Html_inside_an_alert_is_escaped()
    {
        var html = SolutionContent.ToHtml("> [!NOTE]\n> <img src=x onerror=alert(1)> och **fet**");
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
        Assert.Contains("<strong>fet</strong>", html);
    }

    [Fact]
    public void A_plain_quote_stays_a_neutral_blockquote()
    {
        var html = SolutionContent.ToHtml("> Citat utan markering");
        Assert.Contains("<blockquote>", html);
        Assert.DoesNotContain("markdown-alert", html);
    }
}