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

    public Task<ShiftsResponse> GetShiftsResponseAsync(string venueId) =>
      _client.GetAsync<ShiftsResponse, ShiftsResponse>(
        $"/api/plugin/shifts?venueId={venueId}",
        r => r ?? new ShiftsResponse(),
        new ShiftsResponse(),
        "get shifts");

    public Task<ClockResult> ClaimShiftAsync(string shiftId)
    {
      var payload = new { shiftId };
      return _client.PostForResultAsync<object, ClockResult>(
        "/api/plugin/shifts/claim",
        payload,
        "claim shift",
        () => new ClockResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new ClockResult { Success = false, Error = error },
        async content =>
        {
          var json = await content.ReadFromJsonAsync<JsonElement>();
          var status = "CLAIMED";
          if (json.TryGetProperty("shift", out var shiftEl)
              && shiftEl.TryGetProperty("status", out var statusEl)
              && statusEl.ValueKind == JsonValueKind.String)
          {
            status = statusEl.GetString() ?? status;
          }
          var merged = json.TryGetProperty("merged", out var mergedEl) && mergedEl.ValueKind == JsonValueKind.True;
          return new ClockResult { Success = true, Status = status, Merged = merged };
        });
    }

    public Task<ClockResult> ClockInAsync(string shiftId)
    {
      var payload = new { shiftId };
      return _client.PostForResultAsync<object, ClockResult>(
        "/api/plugin/shifts/clock-in",
        payload,
        "clock in",
        () => new ClockResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new ClockResult { Success = false, Error = error },
        _ => Task.FromResult(new ClockResult { Success = true, Status = "ACTIVE" }));
    }

    public Task<ClockResult> ClockOutAsync(string shiftId)
    {
      var payload = new { shiftId };
      return _client.PostForResultAsync<object, ClockResult>(
        "/api/plugin/shifts/clock-out",
        payload,
        "clock out",
        () => new ClockResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new ClockResult { Success = false, Error = error },
        async content =>
        {
          var json = await content.ReadFromJsonAsync<JsonElement>();
          double? hours = null;
          if (json.TryGetProperty("shift", out var shiftEl)
              && shiftEl.TryGetProperty("hoursWorked", out var hw)
              && hw.ValueKind == JsonValueKind.Number)
          {
            hours = hw.GetDouble();
          }
          return new ClockResult { Success = true, Status = "COMPLETED", HoursWorked = hours };
        });
    }
  }
}
