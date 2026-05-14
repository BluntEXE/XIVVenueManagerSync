using System;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace VenueManager
{
  public class XIVAppPatronApi
  {
    private readonly XIVAppApiClient _client;

    internal XIVAppPatronApi(XIVAppApiClient client)
    {
      _client = client;
    }

    public async Task<bool> LogPatronVisitAsync(string venueId, string characterName, string world, string action)
    {
      if (!_client.IsConfigured)
        throw new XIVAppApiException("API not configured. Please set your API key in settings.");

      try
      {
        var request = new XIVAppPatronVisitRequest
        {
          VenueId = venueId,
          CharacterName = characterName,
          World = world,
          Action = action,
          Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
        var response = await _client.Http.PostAsJsonAsync($"{_client.BaseUrl}/api/plugin/patron-visits", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to log patron visit: {response.StatusCode} - {error}");
          return false;
        }
        return true;
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error logging patron visit: {ex.Message}");
        return false;
      }
    }

    public async Task<bool> LogServiceAsync(string venueId, string guestName, int amount, string? notes = null)
    {
      if (!_client.IsConfigured)
        throw new XIVAppApiException("API not configured. Please set your API key in settings.");

      try
      {
        var request = new XIVAppServiceRequest
        {
          VenueId = venueId,
          GuestName = guestName,
          Amount = amount,
          Notes = notes,
        };
        var response = await _client.Http.PostAsJsonAsync($"{_client.BaseUrl}/api/plugin/services", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to log service: {response.StatusCode} - {error}");
          return false;
        }
        return true;
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error logging service: {ex.Message}");
        return false;
      }
    }

    /// <summary>
    /// Log a sale at a venue. Posts to /api/plugin/transactions. The
    /// serviceId and notes are optional; customerName is optional but
    /// strongly encouraged so the webhook embed has a real name to show.
    /// </summary>
    public async Task<LogTransactionResult> LogTransactionAsync(
      string venueId,
      string? serviceId,
      decimal amount,
      string? customerName = null,
      string? notes = null)
    {
      if (!_client.IsConfigured)
        return new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." };

      try
      {
        var request = new XIVAppTransactionRequest
        {
          VenueId = venueId,
          ServiceId = serviceId,
          Amount = amount,
          CustomerName = customerName,
          Notes = notes,
        };
        var response = await _client.Http.PostAsJsonAsync($"{_client.BaseUrl}/api/plugin/transactions", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to log transaction: {response.StatusCode} - {error}");
          return new LogTransactionResult { Success = false, Error = error };
        }
        return new LogTransactionResult { Success = true };
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error logging transaction: {ex.Message}");
        return new LogTransactionResult { Success = false, Error = ex.Message };
      }
    }
  }
}
