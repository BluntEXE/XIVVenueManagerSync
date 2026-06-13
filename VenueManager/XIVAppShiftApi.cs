using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace VenueManager
{
  public class XIVAppShiftApi
  {
    private readonly XIVAppApiClient _client;

    internal XIVAppShiftApi(XIVAppApiClient client)
    {
      _client = client;
    }

    public async Task<ShiftsResponse> GetShiftsResponseAsync(string venueId)
    {
      var empty = new ShiftsResponse();
      if (!_client.IsConfigured) return empty;
      try
      {
        var response = await _client.Http.GetAsync(
          $"{_client.BaseUrl}/api/plugin/shifts?venueId={venueId}");
        if (!response.IsSuccessStatusCode)
        {
          Plugin.Log.Warning($"Failed to get shifts: {response.StatusCode}");
          return empty;
        }
        return await response.Content.ReadFromJsonAsync<ShiftsResponse>() ?? empty;
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error fetching shifts: {ex.Message}");
        return empty;
      }
    }

    public async Task<ClockResult> ClaimShiftAsync(string shiftId)
    {
      if (!_client.IsConfigured)
        return new ClockResult { Success = false, Error = "API not configured" };
      try
      {
        var payload = new { shiftId };
        var response = await _client.Http.PostAsJsonAsync(
          $"{_client.BaseUrl}/api/plugin/shifts/claim", payload);
        if (!response.IsSuccessStatusCode)
        {
          var body = await response.Content.ReadAsStringAsync();
          return new ClockResult { Success = false, Error = body };
        }
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var status = "CLAIMED";
        if (json.TryGetProperty("shift", out var shiftEl)
            && shiftEl.TryGetProperty("status", out var statusEl)
            && statusEl.ValueKind == JsonValueKind.String)
        {
          status = statusEl.GetString() ?? status;
        }
        var merged = json.TryGetProperty("merged", out var mergedEl) && mergedEl.ValueKind == JsonValueKind.True;
        return new ClockResult { Success = true, Status = status, Merged = merged };
      }
      catch (Exception ex)
      {
        return new ClockResult { Success = false, Error = ex.Message };
      }
    }

    public async Task<ClockResult> ClockInAsync(string shiftId)
    {
      if (!_client.IsConfigured)
        return new ClockResult { Success = false, Error = "API not configured" };
      try
      {
        var payload = new { shiftId };
        var response = await _client.Http.PostAsJsonAsync(
          $"{_client.BaseUrl}/api/plugin/shifts/clock-in", payload);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          return new ClockResult { Success = false, Error = error };
        }
        return new ClockResult { Success = true, Status = "ACTIVE" };
      }
      catch (Exception ex)
      {
        return new ClockResult { Success = false, Error = ex.Message };
      }
    }

    public async Task<ClockResult> ClockOutAsync(string shiftId)
    {
      if (!_client.IsConfigured)
        return new ClockResult { Success = false, Error = "API not configured" };
      try
      {
        var payload = new { shiftId };
        var response = await _client.Http.PostAsJsonAsync(
          $"{_client.BaseUrl}/api/plugin/shifts/clock-out", payload);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          return new ClockResult { Success = false, Error = error };
        }
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        double? hours = null;
        if (json.TryGetProperty("shift", out var shiftEl)
            && shiftEl.TryGetProperty("hoursWorked", out var hw)
            && hw.ValueKind == JsonValueKind.Number)
        {
          hours = hw.GetDouble();
        }
        return new ClockResult { Success = true, Status = "COMPLETED", HoursWorked = hours };
      }
      catch (Exception ex)
      {
        return new ClockResult { Success = false, Error = ex.Message };
      }
    }
  }
}
