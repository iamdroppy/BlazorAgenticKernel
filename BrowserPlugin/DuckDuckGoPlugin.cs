using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Net;
namespace BrowserPlugin;

/// <summary>
/// Semantic Kernel plugin that fetches a Serpapi plugin and returns a
/// LLM-friendly plain-text rendering of its content.
/// </summary>
public sealed class DuckDuckGoPlugin
{
    private readonly HttpClient _http;

    public DuckDuckGoPlugin(HttpClient? http = null)
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

    [KernelFunction("SearchWeb")]
    [Description(
        "Searches the web using duckduckgo API and returns the results.")]
    public async Task<string> SearchWebAsync(
        [Description("Query to search for.")]
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query is required.";
        string url = $"https://duckduckgo.com/?q={WebUtility.UrlEncode(query)}&ia=web";
        return await _http.GetStringAsync(url, cancellationToken);

    }
}