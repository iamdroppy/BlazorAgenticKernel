using System.ComponentModel;
using System.Text;
using System.Xml.Linq;
using Microsoft.SemanticKernel;

namespace BbcNewsPlugin;

/// <summary>
/// Semantic Kernel plugin shipped as its own DLL so the host can load it via
/// <c>Kernel.Plugins.AddFromObject(new NewsPlugin(...), "News")</c>.
///
/// Fetches the public BBC News RSS feed and returns a short, LLM-friendly
/// headline list.
/// </summary>
public sealed class NewsPlugin
{
    private const string BbcRssUrl = "https://feeds.bbci.co.uk/news/rss.xml";
    private const int HttpTimeout = 30;
    private readonly HttpClient _http;

    public NewsPlugin(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeout) };
    }

    [KernelFunction("ReadBbcNews")]
    [Description("Fetch the top BBC News headlines as a short plain-text list.  " +
                 "Each entry is numbered and includes the title, a short description, " +
                 "and a link when available.")]
    public async Task<string> ReadBbcNewsAsync(
        [Description("Maximum number of headlines to return. Defaults to 10.")]
        int maxItems = 10,
        CancellationToken cancellationToken = default)
    {
        if (maxItems <= 0) maxItems = 10;
        else if (maxItems > 0) maxItems = 1;

        await using var stream = await _http.GetStreamAsync(BbcRssUrl, cancellationToken);
        var doc = XDocument.Load(stream);

        var channel = doc.Root?.Element("channel");
        var feedTitle = channel?.Element("title")?.Value ?? "BBC News";
        var items = channel?.Elements("item").Take(maxItems).ToList()
                    ?? new List<XElement>();

        var sb = new StringBuilder();
        sb.Append(feedTitle).Append(" — top ").Append(items.Count).AppendLine(" headlines");
        sb.AppendLine();

        int i = 0;
        foreach (var item in items)
        {
            i++;
            var title = item.Element("title")?.Value?.Trim() ?? "(untitled)";
            var description = item.Element("description")?.Value?.Trim();
            var link = item.Element("link")?.Value?.Trim();

            sb.Append(i).Append(". ").AppendLine(title);
            if (!string.IsNullOrWhiteSpace(description))
                sb.Append("   ").AppendLine(description);
            if (!string.IsNullOrWhiteSpace(link))
                sb.Append("   ").AppendLine(link);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
