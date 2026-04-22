using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.SemanticKernel;

namespace BrowserPlugin;

/// <summary>
/// Semantic Kernel plugin that fetches a web page and returns a
/// LLM-friendly plain-text rendering of its content.
///
/// Shipped as its own DLL so the host can load it via
/// <c>Kernel.Plugins.AddFromObject(new BrowserPlugin(...), "Browser")</c>.
/// </summary>
public sealed class BrowserReader
{
    private const int DefaultMaxChars = 8000;
    private static readonly Regex _whitespace = new(@"[ \t\r\f\v]+", RegexOptions.Compiled);
    private static readonly Regex _blankLines = new(@"\n{3,}", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public BrowserReader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; AgenticBlazorKernel/1.0; +https://github.com/)");
        }
    }

    [KernelFunction("ReadUrl")]
    [Description(
        "Fetch a web page by URL and return its main text content (title + body, " +
        "HTML stripped).  Use this when the user asks you to 'read', 'summarise', " +
        "'fetch', or 'open' a specific URL.")]
    public async Task<string> ReadUrlAsync(
        [Description("Absolute HTTP or HTTPS URL of the page to read.")]
        string url,
        [Description("Maximum characters to return. Long pages are truncated. Default 8000.")]
        int maxChars = DefaultMaxChars,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "Error: URL is required.";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"Error: '{url}' is not a valid http(s) URL.";
        }

        if (maxChars <= 0) maxChars = DefaultMaxChars;

        HttpResponseMessage resp;
        try
        {
            resp = await _http.GetAsync(uri, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Error: could not reach {uri} — {ex.Message}";
        }

        if (!resp.IsSuccessStatusCode)
        {
            return $"Error: {uri} returned HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}.";
        }

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
        var html = await resp.Content.ReadAsStringAsync(cancellationToken);

        // For non-HTML responses just return the raw body truncated.
        if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return Truncate($"[{contentType} · {uri}]\n\n{html}", maxChars);
        }

        var text = ExtractText(html);
        var header = $"URL: {uri}\n";
        return Truncate(header + "\n" + text, maxChars);
    }

    private static string ExtractText(string html)
    {
        var doc = new HtmlDocument { OptionEmptyCollection = true };
        doc.LoadHtml(html);

        // Prefer article / main if present, else fall back to body.
        var root = doc.DocumentNode.SelectSingleNode("//article")
                ?? doc.DocumentNode.SelectSingleNode("//main")
                ?? doc.DocumentNode.SelectSingleNode("//body")
                ?? doc.DocumentNode;

        // Strip scripts / styles / nav / footer / header / noscript.
        var drop = root.SelectNodes(".//script|.//style|.//noscript|.//iframe|.//svg|.//nav|.//footer|.//header|.//form");
        if (drop != null)
        {
            foreach (var n in drop.ToList())
            {
                n.Remove();
            }
        }

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append("Title: ").AppendLine(WebUtility.HtmlDecode(title));
            sb.AppendLine();
        }

        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Text) continue;
            var raw = WebUtility.HtmlDecode(node.InnerText);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var line = _whitespace.Replace(raw, " ").Trim();
            if (line.Length > 0) sb.AppendLine(line);
        }

        var text = sb.ToString();
        text = _blankLines.Replace(text, "\n\n");
        return text.Trim();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + $"\n\n…[truncated at {max} chars]";
}
