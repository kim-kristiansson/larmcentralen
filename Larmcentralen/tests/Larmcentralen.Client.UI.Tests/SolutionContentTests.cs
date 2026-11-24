using Larmcentralen.Client.UI.Services;

namespace Larmcentralen.Client.UI.Tests;

public class SolutionContentTests
{
    private const char Nbsp = (char)0xA0;

    [Theory]
    [InlineData("<p>Steg</p>", true)]
    [InlineData("  \n <h2>Rubrik</h2>", true)]
    [InlineData("## Rubrik", false)]
    [InlineData("Byt säkring. Se <b>manualen</b>.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Legacy_html_is_content_that_starts_with_a_tag(string? content, bool expected)
        => Assert.Equal(expected, SolutionContent.IsLegacyHtml(content));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  \n\n ", true)]
    [InlineData("<p><br></p>", true)]
    [InlineData("<p></p>", true)]
    [InlineData("<p>Steg</p>", false)]
    [InlineData("1. Stäng av ugnen", false)]
    public void Empty_content_is_detected_for_markdown_and_html(string? content, bool expected)
        => Assert.Equal(expected, SolutionContent.IsEmpty(content));

    [Fact]
    public void Non_breaking_spaces_alone_count_as_empty()
        => Assert.True(SolutionContent.IsEmpty($" {Nbsp}{Nbsp} "));

    [Fact]
    public void Editor_gets_markdown_unchanged()
        => Assert.Equal("## Rubrik\n\nText", SolutionContent.ToEditorMarkdown("## Rubrik\n\nText"));

    [Fact]
    public void Editor_gets_an_empty_string_for_an_empty_quill_document()
        => Assert.Equal("", SolutionContent.ToEditorMarkdown("<p><br></p>"));

    [Fact]
    public void Quill_headings_become_markdown_headings()
    {
        var md = SolutionContent.HtmlToMarkdown("<h2>Felsökning</h2><h3>Kontrollera givaren</h3><p>Text</p>");
        Assert.Contains("## Felsökning", md);
        Assert.Contains("### Kontrollera givaren", md);
        Assert.Contains("Text", md);
    }

    [Fact]
    public void Quill_lists_become_markdown_lists()
    {
        var md = SolutionContent.HtmlToMarkdown(
            "<ol><li>Stäng av ugnen</li><li>Vänta 10 min</li></ol><ul><li>Handskar</li><li>Skyddsglasögon</li></ul>");
        Assert.Contains("1. Stäng av ugnen", md);
        Assert.Contains("2. Vänta 10 min", md);
        Assert.Matches(@"(?m)^[-*] Handskar$", md);
        Assert.Matches(@"(?m)^[-*] Skyddsglasögon$", md);
    }

    [Fact]
    public void Bold_italic_and_strike_are_kept()
    {
        var md = SolutionContent.HtmlToMarkdown("<p><strong>Viktigt</strong> och <em>kursivt</em> och <s>gammalt</s></p>");
        Assert.Contains("**Viktigt**", md);
        Assert.Matches(@"[*_]kursivt[*_]", md);
        Assert.Contains("~~gammalt~~", md);
    }

    [Fact]
    public void Blockquote_inline_code_and_code_block_are_kept()
    {
        var md = SolutionContent.HtmlToMarkdown(
            "<blockquote>Bryt strömmen först!</blockquote><p>Sätt <code>P-204</code> till 0.</p><pre data-language=\"plain\">\nRESET 1\nSTART\n</pre>");
        Assert.Contains("> Bryt strömmen först!", md);
        Assert.Contains("`P-204`", md);
        Assert.Contains("```", md);
        Assert.Contains("RESET 1", md);
        Assert.Contains("START", md);
    }

    [Fact]
    public void Links_are_kept()
    {
        var md = SolutionContent.HtmlToMarkdown("<p>Se <a href=\"https://example.com/manual.pdf\" rel=\"noopener noreferrer\" target=\"_blank\">manualen</a>.</p>");
        Assert.Contains("[manualen](https://example.com/manual.pdf)", md);
    }

    [Fact]
    public void Non_breaking_spaces_become_plain_spaces()
    {
        var md = SolutionContent.HtmlToMarkdown($"<p>Byt&nbsp;säkring&nbsp;F3{Nbsp}i skåpet</p>");
        Assert.Equal("Byt säkring F3 i skåpet", md);
    }

    [Fact]
    public void Tags_without_markdown_keep_their_text_but_leave_no_html()
    {
        var md = SolutionContent.HtmlToMarkdown("<p><u>Understruket</u> och <span class=\"x\">vanligt</span></p>");
        Assert.Contains("Understruket", md);
        Assert.Contains("vanligt", md);
        Assert.DoesNotContain("<", md);
    }

    [Fact]
    public void Converted_markdown_renders_back_to_the_same_structure()
    {
        var md = SolutionContent.HtmlToMarkdown("<h2>Steg</h2><ol><li>Ett</li><li><strong>Två</strong></li></ol><blockquote>Varm yta</blockquote>");
        var html = SolutionContent.ToHtml(md);
        Assert.Contains("<h2", html);
        Assert.Contains("<ol>", html);
        Assert.Contains("<strong>Två</strong>", html);
        Assert.Contains("<blockquote>", html);
    }

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

    [Fact]
    public void Legacy_html_is_shown_as_stored()
    {
        const string legacy = "<h2>Steg</h2><ol><li>Ett</li></ol>";
        Assert.Equal(legacy, SolutionContent.ToHtml(legacy));
    }
}
