using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace VenueManager
{
  public class XIVAppVenueApi
  {
    private readonly XIVAppApiClient _client;

    internal XIVAppVenueApi(XIVAppApiClient client)
    {
      _client = client;
    }

    public async Task<List<XIVAppVenue>> GetVenuesAsync()
    {
      if (!_client.IsConfigured)
        throw new XIVAppApiException("API not configured. Please set your API key in settings.");

      try
      {
        var response = await _client.Http.GetAsync($"{_client.BaseUrl}/api/plugin/venues");
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
      if (!_client.IsConfigured) return null;
      try
      {
        var response = await _client.Http.GetAsync($"{_client.BaseUrl}/api/plugin/services?venueId={venueId}");
        if (!response.IsSuccessStatusCode) return null;
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
      if (!_client.IsConfigured) return new List<Role>();
      try
      {
        var response = await _client.Http.GetAsync($"{_client.BaseUrl}/api/plugin/roles?venueId={venueId}");
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

    public async Task<ActiveEventResponse?> GetActiveEventAsync(string venueId)
    {
      if (!_client.IsConfigured) return null;
      try
      {
        var response = await _client.Http.GetAsync(
          $"{_client.BaseUrl}/api/plugin/events/active?venueId={Uri.EscapeDataString(venueId)}");
        if (!response.IsSuccessStatusCode)
        {
          Plugin.Log.Debug($"GetActiveEventAsync {venueId}: {response.StatusCode}");
          return new ActiveEventResponse { Active = false };
        }
        return await response.Content.ReadFromJsonAsync<ActiveEventResponse>();
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error fetching active event: {ex.Message}");
        return null;
      }
    }
  }
}
