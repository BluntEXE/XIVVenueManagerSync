using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

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
      Venue  = new XIVAppVenueApi(this);
      Patron = new XIVAppPatronApi(this);
      Shift  = new XIVAppShiftApi(this);
    }

    public void Configure(string apiKey, string serverUrl)
    {
      _apiKey = apiKey?.Trim() ?? "";
      BaseUrl = (serverUrl ?? "").Trim().TrimEnd('/');

      Http.DefaultRequestHeaders.Clear();
      // Re-add static headers after Clear() — these survive API key changes
      Http.DefaultRequestHeaders.UserAgent.ParseAdd("XIVVenueManager/1.0");
      Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, */*");
      Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
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

    /// <summary>
    /// GET a JSON response and extract a result from it, returning
    /// <paramref name="fallback"/> if not configured, the request fails,
    /// or an exception is thrown. Used by read-only endpoints where the
    /// caller always wants a usable default rather than an exception.
    /// </summary>
    internal async Task<TResult> GetAsync<TResponse, TResult>(
      string path,
      Func<TResponse?, TResult> extract,
      TResult fallback,
      string errorContext)
    {
      if (!IsConfigured) return fallback;
      try
      {
        var response = await Http.GetAsync($"{BaseUrl}{path}");
        if (!response.IsSuccessStatusCode)
        {
          Plugin.Log.Warning($"Failed to {errorContext}: {response.StatusCode}");
          return fallback;
        }
        var body = await response.Content.ReadFromJsonAsync<TResponse>();
        return extract(body);
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error {errorContext}: {ex.Message}");
        return fallback;
      }
    }

    /// <summary>
    /// POST a JSON request and produce a typed result, calling
    /// <paramref name="notConfigured"/>/<paramref name="onFailure"/>/<paramref name="onSuccess"/>
    /// to construct the appropriate result for each outcome. Used by
    /// mutating endpoints where the caller always wants a typed
    /// success/failure result rather than an exception.
    /// </summary>
    internal async Task<TResult> PostForResultAsync<TRequest, TResult>(
      string path,
      TRequest request,
      string errorContext,
      Func<TResult> notConfigured,
      Func<string, TResult> onFailure,
      Func<HttpContent, Task<TResult>> onSuccess)
    {
      if (!IsConfigured) return notConfigured();
      try
      {
        var response = await Http.PostAsJsonAsync($"{BaseUrl}{path}", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to {errorContext}: {response.StatusCode} - {error}");
          return onFailure(error);
        }
        return await onSuccess(response.Content);
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error {errorContext}: {ex.Message}");
        return onFailure(ex.Message);
      }
    }

    public void Dispose() => Http.Dispose();
  }
}
