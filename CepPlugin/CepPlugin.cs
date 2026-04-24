using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace CepPlugin;

public class CepPlugin
{
    private readonly HttpClient _http;

    public CepPlugin(HttpClient? http = null)
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

    [KernelFunction("GetCEPInfo")]
    [Description(
        "Gets information for a given CEP (Brazilian postal code).")]
    public async Task<string> GetCepInfoAsync(string cep, CancellationToken cancellationToken = default)
    {
        cep = cep.Replace("-", "").Trim();

        var response = await _http.GetAsync($"https://cep.awesomeapi.com.br/json/{cep}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"Error: Failed to fetch CEP. HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return content;
    }
}
