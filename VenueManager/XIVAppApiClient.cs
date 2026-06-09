using System;
using System.Net.Http;

namespace VenueManager
{
  /// <summary>
  /// Owns the shared HttpClient and API key configuration. Domain logic
  /// lives in the three sub-API classes accessible as properties:
  ///   xivAppClient.Venue   — venues, roles, services, active event
  ///   xivAppClient.Patron  — patron visits, services log, transactions
  ///   xivAppClient.Shift   — shifts, clock-in, clock-out
  /// </summary>
  public class XIVAppApiClient : IDisposable
  {
    internal readonly HttpClient Http;
    internal string BaseUrl = "";
    private string _apiKey = "";

    public XIVAppVenueApi Venue { get; }
    public XIVAppPatronApi Patron { get; }
    public XIVAppShiftApi Shift { get; }

    public XIVAppApiClient()
    {
      Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
      Http.DefaultRequestHeaders.UserAgent.ParseAdd("XIVVenueManager/1.0");
      Venue  = new XIVAppVenueApi(this);
      Patron = new XIVAppPatronApi(this);
      Shift  = new XIVAppShiftApi(this);
    }

    public void Configure(string apiKey, string serverUrl)
    {
      _apiKey = apiKey?.Trim() ?? "";
      BaseUrl = (serverUrl ?? "").Trim().TrimEnd('/');

      Http.DefaultRequestHeaders.Clear();
      if (!string.IsNullOrEmpty(_apiKey))
      {
        // TryAddWithoutValidation avoids FormatException on stray whitespace
        // or control chars that snuck past the UI trim (e.g. zero-width
        // characters from Discord). A malformed key will still 401 at the
        // server — but now the client won't throw before the request.
        Http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", _apiKey);
      }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(BaseUrl);

    public void Dispose() => Http.Dispose();
  }
}
