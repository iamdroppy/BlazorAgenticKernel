using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace CurrencyPlugin;

public sealed class LiveCurrency
{
    private readonly HttpClient _http;

    public LiveCurrency(HttpClient? http = null)
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

    [KernelFunction("HowMuchExchangeRateToBrl")]
    public async Task<string> GetBrlPriceAsync(
        [Description("Currency to be compared to BRL")] string currency,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("https://economia.awesomeapi.com.br/USD/1");
        if (!response.IsSuccessStatusCode)
        {
            return $"Error: Failed to fetch BTC-USD rate. HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return content;
    }
}
