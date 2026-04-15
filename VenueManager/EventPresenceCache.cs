using System;
using System.Collections.Generic;

namespace VenueManager
{
  /// <summary>
  /// Per-venue TTL cache for "is an event currently active at this venue?"
  /// backed by GET /api/plugin/events/active. Patron visits are gated on
  /// this when <c>syncOnlyDuringEvents</c> is true, and the plugin can see
  /// dozens of player arrivals per minute during a busy shift — caching
  /// keeps us from hammering the endpoint once per arrival.
  ///
  /// TTL is deliberately short (60s) so scheduled events start/stop syncing
  /// within a minute of their boundary without the user having to reload.
  /// </summary>
  public class EventPresenceCache
  {
    public class Entry
    {
      public bool Active;
      public string? EventId;
      public DateTime FetchedAt;
    }

    private readonly Dictionary<string, Entry> _cache = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Returns the cached entry for this venue, or null if missing/stale.
    /// Callers should refresh via the API on a null return.
    /// </summary>
    public Entry? Get(string venueId)
    {
      if (string.IsNullOrEmpty(venueId)) return null;
      if (!_cache.TryGetValue(venueId, out var entry)) return null;
      if (DateTime.UtcNow - entry.FetchedAt > _ttl) return null;
      return entry;
    }

    public void Set(string venueId, bool active, string? eventId)
    {
      if (string.IsNullOrEmpty(venueId)) return;
      _cache[venueId] = new Entry
      {
        Active = active,
        EventId = eventId,
        FetchedAt = DateTime.UtcNow,
      };
    }

    public void Invalidate(string venueId)
    {
      _cache.Remove(venueId);
    }

    public void Clear() => _cache.Clear();
  }
}
