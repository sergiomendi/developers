using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ExchangeRateUpdater.Domain.Entities;
using ExchangeRateUpdater.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;  


namespace ExchangeRateUpdater.Infrastructure.Http;

/// <summary>
/// CNB-specific HTTP adapter. Replaces with ECBHttpRepository without Domain changes.
/// HTTP details encapsulated.
/// </summary>
public class CnbHttpExchangeRateRepository : IExchangeRateRepository
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CnbHttpExchangeRateRepository> _logger;
    
    private const string CnbDailyRatesUrl = "https://www.cnb.cz/en/financial-markets/foreign-exchange-market/central-bank-exchange-rate-fixing/central-bank-exchange-rate-fixing/daily.txt";

    public CnbHttpExchangeRateRepository(HttpClient httpClient, ILogger<CnbHttpExchangeRateRepository> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mews-ExchangeRateUpdater/1.0");
    }

    public async Task<IReadOnlyDictionary<string, ExchangeRate>> GetCurrentRatesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching CNB daily exchange rates from {Url}", CnbDailyRatesUrl);
        
        try
        {
            var content = await _httpClient.GetStringAsync(CnbDailyRatesUrl, cancellationToken);
            var rates = ParseCnbDailyTxt(content);
            
            _logger.LogInformation("Successfully parsed {Count} exchange rates from CNB", rates.Count);
            return rates;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch CNB rates from {Url}", CnbDailyRatesUrl);
            throw new InvalidOperationException("CNB exchange rates unavailable", ex);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("CNB rates request cancelled");
            throw;
        }
    }

    private static Dictionary<string, ExchangeRate> ParseCnbDailyTxt(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Skip(2); // Skip date/header
        
        var rates = new Dictionary<string, ExchangeRate>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length == 5 && 
                int.TryParse(parts[2].Trim(), out int amount) &&
                decimal.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rawRate) &&
                amount > 0)
            {
                var normalizedRate = rawRate / amount;
                rates[parts[3].Trim()] = new ExchangeRate(parts[3].Trim(), normalizedRate, DateTime.UtcNow);
            }
        }
        
        return rates;
    }
}
