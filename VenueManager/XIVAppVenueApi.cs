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

    public Task<ServicesResponse?> GetServicesAsync(string venueId) =>
      _client.GetAsync<ServicesResponse, ServicesResponse?>(
        $"/api/plugin/services?venueId={venueId}",
        r => r,
        null,
        "fetch services");

    public Task<List<Role>> GetRolesAsync(string venueId) =>
      _client.GetAsync<RolesResponse, List<Role>>(
        $"/api/plugin/roles?venueId={venueId}",
        r => r?.Roles ?? new List<Role>(),
        new List<Role>(),
        "get roles");

    public Task<List<VipPatron>> GetVipPatronsAsync(string venueId) =>
      _client.GetAsync<VipPatronsResponse, List<VipPatron>>(
        $"/api/plugin/patrons/vip?venueId={venueId}",
        r => r?.VipPatrons ?? new List<VipPatron>(),
        new List<VipPatron>(),
        "get VIP patrons");

    public Task<List<BannedPatron>> GetBannedPatronsAsync(string venueId) =>
      _client.GetAsync<BannedPatronsResponse, List<BannedPatron>>(
        $"/api/plugin/patrons/banned?venueId={venueId}",
        r => r?.BannedPatrons ?? new List<BannedPatron>(),
        new List<BannedPatron>(),
        "get banned patrons");

    public Task<ActiveEventResponse?> GetActiveEventAsync(string venueId) =>
      _client.GetAsync<ActiveEventResponse, ActiveEventResponse?>(
        $"/api/plugin/events/active?venueId={Uri.EscapeDataString(venueId)}",
        r => r,
        new ActiveEventResponse { Active = false },
        "fetch active event");

    public Task<List<Room>> GetRoomsAsync(string venueId) =>
      _client.GetAsync<RoomsResponse, List<Room>>(
        $"/api/plugin/rooms?venueId={venueId}",
        r => r?.Rooms ?? new List<Room>(),
        new List<Room>(),
        "get rooms");

    public Task<LogTransactionResult> SetRoomStatusAsync(string venueId, string roomId, bool isOccupied, string? note)
    {
      var request = new XIVAppSetRoomStatusRequest
      {
        VenueId = venueId,
        RoomId = roomId,
        IsOccupied = isOccupied,
        Note = note,
      };
      return _client.PostForResultAsync<XIVAppSetRoomStatusRequest, LogTransactionResult>(
        "/api/plugin/rooms/status",
        request,
        "set room status",
        () => new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new LogTransactionResult { Success = false, Error = error },
        _ => Task.FromResult(new LogTransactionResult { Success = true }));
    }

    public Task<LogTransactionResult> ReserveRoomAsync(string venueId, string roomId, int durationMinutes)
    {
      var request = new ReserveRoomRequest
      {
        VenueId = venueId,
        RoomId = roomId,
        DurationMinutes = durationMinutes,
      };
      return _client.PostForResultAsync<ReserveRoomRequest, LogTransactionResult>(
        "/api/plugin/rooms/reserve",
        request,
        "reserve room",
        () => new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new LogTransactionResult { Success = false, Error = error },
        _ => Task.FromResult(new LogTransactionResult { Success = true }));
    }

    public Task<LogTransactionResult> ReleaseRoomAsync(string venueId, string roomId)
    {
      var request = new ReleaseRoomRequest
      {
        VenueId = venueId,
        RoomId = roomId,
      };
      return _client.PostForResultAsync<ReleaseRoomRequest, LogTransactionResult>(
        "/api/plugin/rooms/release",
        request,
        "release room",
        () => new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new LogTransactionResult { Success = false, Error = error },
        _ => Task.FromResult(new LogTransactionResult { Success = true }));
    }

    public Task<bool> GetInventoryEnabledAsync(string venueId) =>
      _client.GetAsync<XIVAppInventorySettingsResponse, bool>(
        $"/api/plugin/inventory-settings?venueId={venueId}",
        r => r?.Enabled ?? false,
        false,
        "get inventory settings");

    public Task<LogTransactionResult> LinkItemAsync(string venueId, string serviceId, int itemId, string itemName, int? iconId)
    {
      var request = new XIVAppLinkItemRequest
      {
        VenueId = venueId,
        ServiceId = serviceId,
        ItemId = itemId,
        ItemName = itemName,
        IconId = iconId,
      };
      return _client.PostForResultAsync<XIVAppLinkItemRequest, LogTransactionResult>(
        "/api/plugin/inventory/link-item",
        request,
        "link item",
        () => new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new LogTransactionResult { Success = false, Error = error },
        _ => Task.FromResult(new LogTransactionResult { Success = true }));
    }

    public Task<LogTransactionResult> RestockAsync(string venueId, string serviceId, int stockCount)
    {
      var request = new XIVAppRestockRequest { VenueId = venueId, ServiceId = serviceId, StockCount = stockCount };
      return _client.PostForResultAsync<XIVAppRestockRequest, LogTransactionResult>(
        "/api/plugin/inventory/restock",
        request,
        "restock",
        () => new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new LogTransactionResult { Success = false, Error = error },
        _ => Task.FromResult(new LogTransactionResult { Success = true }));
    }
  }
}
