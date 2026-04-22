using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace CurrencyPlugin;

public sealed class BtcUsdPlugin
{
    private readonly HttpClient _http;

    public BtcUsdPlugin(HttpClient? http = null)
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

    [KernelFunction("GetBtcUsdRate")]
    [Description(
        "Gets the current BTC to USD exchange rate.")]
    public async Task<string> GetBtcUsdRateAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("https://economia.awesomeapi.com.br/last/BTC-USD", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"Error: Failed to fetch BTC-USD rate. HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return content;
    }
}
