using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VenueManager
{
  // API Models for XIV-App backend
  public class XIVAppVenue
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("addresses")]
    public List<string> Addresses { get; set; } = new();
    
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
  }

  public class Service
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("price")]
    public string Price { get; set; } = "";
    
    [JsonPropertyName("category")]
    public string? Category { get; set; }
  }

  public class ServicesResponse
  {
    [JsonPropertyName("services")]
    public List<Service> Services { get; set; } = new();
    
    [JsonPropertyName("userRole")]
    public string? UserRole { get; set; }
  }

  public class Role
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
  }

  public class RolesResponse
  {
    [JsonPropertyName("roles")]
    public List<Role> Roles { get; set; } = new();
  }

  public class XIVAppVenuesResponse
  {
    [JsonPropertyName("venues")]
    public List<XIVAppVenue> Venues { get; set; } = new();
  }

  public class XIVAppPatronVisitRequest
  {
    [JsonPropertyName("venueId")]
    public string VenueId { get; set; } = "";
    
    [JsonPropertyName("characterName")]
    public string CharacterName { get; set; } = "";
    
    [JsonPropertyName("world")]
    public string World { get; set; } = "";
    
    [JsonPropertyName("action")]
    public string Action { get; set; } = ""; // "enter" or "leave"
    
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";
  }

  public class XIVAppServiceRequest
  {
    [JsonPropertyName("venueId")]
    public string VenueId { get; set; } = "";
    
    [JsonPropertyName("guestName")]
    public string GuestName { get; set; } = "";
    
    [JsonPropertyName("amount")]
    public int Amount { get; set; }
    
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
  }

  public class XIVAppTransactionRequest
  {
    [JsonPropertyName("venueId")]
    public string VenueId { get; set; } = "";

    [JsonPropertyName("serviceId")]
    public string? ServiceId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
  }

  // Result wrapper for LogTransactionAsync — lets the UI tab show the
  // server error inline on failure instead of a generic "failed".
  public class LogTransactionResult
  {
    public bool Success { get; set; }
    public string? Error { get; set; }
  }

  public class XIVAppApiException : Exception
  {
    public XIVAppApiException(string message) : base(message) { }
    public XIVAppApiException(string message, Exception inner) : base(message, inner) { }
  }

  public class XIVAppApiClient : IDisposable
  {
    private readonly HttpClient _httpClient;
    private string _apiKey = "";
    private string _baseUrl = "";

    public XIVAppApiClient()
    {
      _httpClient = new HttpClient();
      _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public void Configure(string apiKey, string serverUrl)
    {
      _apiKey = apiKey;
      _baseUrl = serverUrl.TrimEnd('/');
      
      _httpClient.DefaultRequestHeaders.Clear();
      _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_baseUrl);

    public async Task<List<XIVAppVenue>> GetVenuesAsync()
    {
      if (!IsConfigured)
      {
        throw new XIVAppApiException("API not configured. Please set your API key in settings.");
      }

      try
      {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/plugin/venues");
        
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          throw new XIVAppApiException($"Failed to get venues: {response.StatusCode} - {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<XIVAppVenuesResponse>();
        return result?.Venues ?? new List<XIVAppVenue>();
      }
      catch (HttpRequestException ex)
      {
        throw new XIVAppApiException($"Network error connecting to server: {ex.Message}", ex);
      }
      catch (TaskCanceledException)
      {
        throw new XIVAppApiException("Request timed out. Please check your connection.");
      }
      catch (Exception ex) when (ex is not XIVAppApiException)
      {
        throw new XIVAppApiException($"Error fetching venues: {ex.Message}", ex);
      }
    }

    public async Task<ServicesResponse?> GetServicesAsync(string venueId)
    {
      if (!IsConfigured)
      {
        return null;
      }

      try
      {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/plugin/services?venueId={venueId}");
        
        if (!response.IsSuccessStatusCode)
        {
          return null;
        }

        return await response.Content.ReadFromJsonAsync<ServicesResponse>();
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error fetching services: {ex.Message}");
        return null;
      }
    }

    public async Task<List<Role>> GetRolesAsync(string venueId)
    {
      if (!IsConfigured)
      {
        return new List<Role>();
      }

      try
      {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/plugin/roles?venueId={venueId}");
        
        if (!response.IsSuccessStatusCode)
        {
          Plugin.Log.Warning($"Failed to get roles: {response.StatusCode}");
          return new List<Role>();
        }

        var result = await response.Content.ReadFromJsonAsync<RolesResponse>();
        return result?.Roles ?? new List<Role>();
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error fetching roles: {ex.Message}");
        return new List<Role>();
      }
    }

    public async Task<bool> LogPatronVisitAsync(string venueId, string characterName, string world, string action)
    {
      if (!IsConfigured)
      {
        throw new XIVAppApiException("API not configured. Please set your API key in settings.");
      }

      try
      {
        var request = new XIVAppPatronVisitRequest
        {
          VenueId = venueId,
          CharacterName = characterName,
          World = world,
          Action = action,
          Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/plugin/patron-visits", request);
        
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
      if (!IsConfigured)
      {
        throw new XIVAppApiException("API not configured. Please set your API key in settings.");
      }

      try
      {
        var request = new XIVAppServiceRequest
        {
          VenueId = venueId,
          GuestName = guestName,
          Amount = amount,
          Notes = notes
        };

        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/plugin/services", request);
        
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
      if (!IsConfigured)
      {
        return new LogTransactionResult
        {
          Success = false,
          Error = "API not configured. Please set your API key in settings.",
        };
      }

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

        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/plugin/transactions", request);

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

    public void Dispose()
    {
      _httpClient.Dispose();
    }
  }
}