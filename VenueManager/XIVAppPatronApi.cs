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

    public Task<bool> LogPatronVisitAsync(string venueId, string characterName, string world, string action)
    {
      var request = new XIVAppPatronVisitRequest
      {
        VenueId = venueId,
        CharacterName = characterName,
        World = world,
        Action = action,
        Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
      };
      return _client.PostForResultAsync<XIVAppPatronVisitRequest, bool>(
        "/api/plugin/patron-visits",
        request,
        "log patron visit",
        () => throw new XIVAppApiException("API not configured. Please set your API key in settings."),
        _ => false,
        _ => Task.FromResult(true));
    }

    public Task<bool> LogServiceAsync(string venueId, string guestName, int amount, string? notes = null)
    {
      var request = new XIVAppServiceRequest
      {
        VenueId = venueId,
        GuestName = guestName,
        Amount = amount,
        Notes = notes,
      };
      return _client.PostForResultAsync<XIVAppServiceRequest, bool>(
        "/api/plugin/services",
        request,
        "log service",
        () => throw new XIVAppApiException("API not configured. Please set your API key in settings."),
        _ => false,
        _ => Task.FromResult(true));
    }

    /// <summary>
    /// Log a sale (or other transaction type) at a venue. Posts to
    /// /api/plugin/transactions. The serviceId and notes are optional;
    /// customerName is optional but strongly encouraged so the webhook
    /// embed has a real name to show. type defaults to "SALE" server-side
    /// when omitted - pass "TIP" to log a tip instead.
    /// </summary>
    public Task<LogTransactionResult> LogTransactionAsync(
      string venueId,
      string? serviceId,
      decimal amount,
      string? customerName = null,
      string? notes = null,
      string? type = null)
    {
      var request = new XIVAppTransactionRequest
      {
        VenueId = venueId,
        ServiceId = serviceId,
        Amount = amount,
        CustomerName = customerName,
        Notes = notes,
        Type = type,
      };
      return _client.PostForResultAsync<XIVAppTransactionRequest, LogTransactionResult>(
        "/api/plugin/transactions",
        request,
        "log transaction",
        () => new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new LogTransactionResult { Success = false, Error = error },
        async content =>
        {
          var body = await content.ReadFromJsonAsync<XIVAppTransactionResponse>();
          return new LogTransactionResult
          {
            Success = true,
            ServiceId = body?.Transaction?.ServiceId,
            ServiceStockCount = body?.Transaction?.ServiceStockCount,
          };
        });
    }

    /// <summary>
    /// Ban a patron at a venue, with a required reason. Posts to
    /// /api/plugin/patrons/ban. Used by /xvm ban! — finds or creates the
    /// Patron row server-side, so this works even for a character with
    /// no prior visit history.
    /// </summary>
    public Task<LogTransactionResult> BanPatronAsync(string venueId, string characterName, string world, string reason)
    {
      var request = new XIVAppBanPatronRequest
      {
        VenueId = venueId,
        CharacterName = characterName,
        World = world,
        Reason = reason,
      };
      return _client.PostForResultAsync<XIVAppBanPatronRequest, LogTransactionResult>(
        "/api/plugin/patrons/ban",
        request,
        "ban patron",
        () => new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." },
        error => new LogTransactionResult { Success = false, Error = error },
        _ => Task.FromResult(new LogTransactionResult { Success = true }));
    }
  }
}
