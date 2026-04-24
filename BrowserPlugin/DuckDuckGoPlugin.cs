using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Collections;
using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
namespace BrowserPlugin;

/// <summary>
/// Semantic Kernel plugin that fetches a Serpapi plugin and returns a
/// LLM-friendly plain-text rendering of its content.
/// </summary>
public sealed class DuckDuckGoPlugin
{
    private readonly ILogger<DuckDuckGoPlugin> _logger;
    private readonly HttpClient _http;

    public DuckDuckGoPlugin(ILogger<DuckDuckGoPlugin> logger, HttpClient? http = null)
    {
        _logger = logger;
        _http = http ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        foreach (var item in _http.DefaultRequestHeaders.UserAgent)
        {
            _http.DefaultRequestHeaders.UserAgent.Remove(item);
        }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Linux; Android 11) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.50 Mobile Safari/537.36");
    }
    [KernelFunction("AccessSite")]
    public async Task<object> AccessSiteAsync(
        [Description("The URL of the site to request.")]
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                return "Error: url is required.";
            string requestUrl = url;
            var html = await _http.GetStringAsync(requestUrl, cancellationToken);

            return new
            {
                Success = true,
                Found = true,
                Format = "html",
                Parse = "Parse to human readable content",
                Content = html
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not access {url}: {ex.Message}");
            return new
            {
                Success = false,
                Found = false,
                Format = "text",
                Parse = "Error message",
                Content = ex.Message
            };
        }
    }

    [KernelFunction("SearchWeb")]
    public async Task<object> SearchWebAsync(
     [Description("Query to search for.")]
        string query,
     CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query is required.";
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/search");
        request.Headers.Add("X-API-KEY", "7cc8a4b40698fc13700f19f4b27e541d2df76777");
        var content = new StringContent($"{{\"q\":\"{query}\"}}", null, "application/json");
        request.Content = content;
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();

    }

}