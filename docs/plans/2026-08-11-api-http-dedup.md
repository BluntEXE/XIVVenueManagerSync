# API HTTP Boilerplate Dedup (Roadmap Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dedupe the repeated `GetAsync`/`PostAsJsonAsync` + try/catch + config-check boilerplate across `XIVAppVenueApi.cs`, `XIVAppShiftApi.cs`, `XIVAppPatronApi.cs` (19 methods, ~700 combined lines) into 2 shared helpers on `XIVAppApiClient`, without changing any public method signature or observable behavior.

**Architecture:** Reading all 3 files (not trusting the earlier codebase scan, which undercounted the real pattern shape) found **3 distinct control-flow patterns**, not one:
1. **GET-with-fallback** (8 methods): not-configured/failure/exception all return a caller-supplied default value, optionally after logging a warning.
2. **POST-mutate-returning-result** (8 methods): not-configured/failure/exception all construct a typed failure result (`LogTransactionResult` or `ClockResult`), success constructs a typed success result.
3. **Throw-on-failure** (3 methods): `GetVenuesAsync`, `LogPatronVisitAsync`, `LogServiceAsync` — these throw `XIVAppApiException` when not configured, and (for the latter two) fall back to logging + returning `false` on request failure rather than throwing. This is a materially different control-flow shape used deliberately where the caller needs to react to a hard failure — **not migrated in this plan**, left exactly as-is. Forcing these into a shared helper would either lose the throw-semantics or require a third near-single-use helper for 3 methods with inconsistent internal behavior (see Task 0).

This plan adds `GetAsync<TResponse, TResult>` (pattern 1) and `PostForResultAsync<TRequest, TResult>` (pattern 2) to `XIVAppApiClient`, then migrates the 16 methods that fit those two patterns cleanly.

**Tech Stack:** C#, .NET, Dalamud plugin framework. **No test project exists in this repo** — verification is `dotnet build` (compiles = signatures preserved) plus manual in-game testing after a Release build. This plan cannot follow this codebase's usual TDD flow because there's nothing to run tests against; each task's "verify" step is a build + a careful side-by-side read of old vs. new behavior, and the whole plan needs one in-game pass before shipping (see the final section).

**Important — 2 deliberate, flagged behavior tightenings.** While mapping every method onto Pattern 1, two small pre-existing inconsistencies surfaced:
- `GetActiveEventAsync` currently returns `new ActiveEventResponse { Active = false }` on an HTTP failure but `null` on an exception — two different fallback values for two different failure modes. This plan unifies both to `new ActiveEventResponse { Active = false }` (a safer fallback for callers than `null`, since call sites likely check `.Active` without a null-guard).
- `GetInventoryEnabledAsync` currently logs a warning on exception but NOT on an HTTP failure (the only GET method with this gap — looks like an oversight, not intentional). This plan adds the missing warning log on the HTTP-failure path, matching every sibling method.

Both are one-line, low-risk, and are called out explicitly here and in their task/commit — not silently smuggled in as "just a refactor."

**Important — 1 deliberate message standardization.** `XIVAppShiftApi`'s 3 methods currently use `"API not configured"` as the not-configured error message; `XIVAppVenueApi`/`XIVAppPatronApi`'s methods use `"API not configured. Please set your API key in settings."`. This plan standardizes all 8 Pattern-2 methods on the fuller message (better UX, tells the user what to actually do). Flagged, not silent.

---

## Task 0: Confirm exclusions (no code change)

Three methods are explicitly **not** migrated in this plan — read this before starting any other task, so you don't "helpfully" fold them in:

- **`XIVAppVenueApi.GetVenuesAsync`** — throws `XIVAppApiException` on not-configured, and has 3 distinct catch clauses (`HttpRequestException`, `TaskCanceledException`, generic `Exception`) giving different messages per failure type. No sibling method has this granularity. Leave as-is.
- **`XIVAppPatronApi.LogPatronVisitAsync`** and **`LogServiceAsync`** — both throw on not-configured but swallow-and-return-`false` on request failure or exception. This throw/swallow split doesn't match either shared pattern. Leave as-is.

- [ ] **Step 1: No action needed** — confirmed above so a future reader doesn't re-flag these as missed work.

---

## Task 1: Add the two shared helpers to `XIVAppApiClient.cs`

**Files:**
- Modify: `VenueManager/XIVAppApiClient.cs`

- [ ] **Step 1: Read the current file in full** (already done above, reproduced here as the starting point — reconfirm nothing has changed before editing):

The current file ends with:
```csharp
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(BaseUrl);

    public void Dispose() => Http.Dispose();
  }
}
```

- [ ] **Step 2: Add the two helpers before the closing braces**

Insert after the `IsConfigured` property and before `Dispose()`:

```csharp
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
```

Add `using System.Net.Http.Json;` and `using System.Threading.Tasks;` to the top of the file if not already present (check current `using` block first — the file currently only has `using System;` and `using System.Net.Http;`).

Note both helpers are `internal`, not `public` — they're implementation details of the 3 sub-API classes (all in the same `VenueManager` namespace/assembly), not part of the plugin's own public surface.

- [ ] **Step 3: Build**

Run: `dotnet build` (from the repo root, or wherever the existing build workflow runs it — check `VenueManager.sln`/CLAUDE.md for the exact command this project uses before running; **ask the user before running a build**, per this project's established convention of confirming before `dotnet build` since it touches the live dev-plugin path).

Expected: clean build. At this point the helpers exist but are unused — nothing should behave differently yet.

- [ ] **Step 4: Commit**

```bash
git add VenueManager/XIVAppApiClient.cs
git commit -m "$(cat <<'EOF'
Add shared GET/POST result helpers to XIVAppApiClient

XIVAppVenueApi/ShiftApi/PatronApi each hand-roll the same
config-check + try/catch + log-and-fallback shape across 19 methods,
in 2 recognizable patterns (GET-with-fallback, POST-returning-typed-result).
This adds both as generic helpers; call sites are migrated in
follow-up commits, method by method, so each is easy to review
against its original behavior.
EOF
)"
```

---

## Task 2: Migrate `XIVAppVenueApi.cs` (9 of 11 methods)

**Files:**
- Modify: `VenueManager/XIVAppVenueApi.cs`

`GetVenuesAsync` is excluded per Task 0 — do not touch it. All 10 other methods migrate.

- [ ] **Step 1: `GetServicesAsync`**

Before:
```csharp
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
```

After:
```csharp
    public Task<ServicesResponse?> GetServicesAsync(string venueId) =>
      _client.GetAsync<ServicesResponse, ServicesResponse?>(
        $"/api/plugin/services?venueId={venueId}",
        r => r,
        null,
        "fetch services");
```

Note: the original didn't log a warning on the HTTP-failure branch for this one method (only on exception) — this migration ADDS a warning log there too, for consistency with every other GET method in this file. This is the `GetInventoryEnabledAsync`-style gap mentioned in the plan header, found in a second method while migrating — same fix, same reasoning, flag it the same way.

- [ ] **Step 2: `GetRolesAsync`**

Before:
```csharp
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
```

After:
```csharp
    public Task<List<Role>> GetRolesAsync(string venueId) =>
      _client.GetAsync<RolesResponse, List<Role>>(
        $"/api/plugin/roles?venueId={venueId}",
        r => r?.Roles ?? new List<Role>(),
        new List<Role>(),
        "get roles");
```

- [ ] **Step 3: `GetVipPatronsAsync`**

Before:
```csharp
    public async Task<List<VipPatron>> GetVipPatronsAsync(string venueId)
    {
      if (!_client.IsConfigured) return new List<VipPatron>();
      try
      {
        var response = await _client.Http.GetAsync($"{_client.BaseUrl}/api/plugin/patrons/vip?venueId={venueId}");
        if (!response.IsSuccessStatusCode)
        {
          Plugin.Log.Warning($"Failed to get VIP patrons: {response.StatusCode}");
          return new List<VipPatron>();
        }
        var result = await response.Content.ReadFromJsonAsync<VipPatronsResponse>();
        return result?.VipPatrons ?? new List<VipPatron>();
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error fetching VIP patrons: {ex.Message}");
        return new List<VipPatron>();
      }
    }
```

After:
```csharp
    public Task<List<VipPatron>> GetVipPatronsAsync(string venueId) =>
      _client.GetAsync<VipPatronsResponse, List<VipPatron>>(
        $"/api/plugin/patrons/vip?venueId={venueId}",
        r => r?.VipPatrons ?? new List<VipPatron>(),
        new List<VipPatron>(),
        "get VIP patrons");
```

- [ ] **Step 4: `GetBannedPatronsAsync`**

Before:
```csharp
    public async Task<List<BannedPatron>> GetBannedPatronsAsync(string venueId)
    {
      if (!_client.IsConfigured) return new List<BannedPatron>();
      try
      {
        var response = await _client.Http.GetAsync($"{_client.BaseUrl}/api/plugin/patrons/banned?venueId={venueId}");
        if (!response.IsSuccessStatusCode)
        {
          Plugin.Log.Warning($"Failed to get banned patrons: {response.StatusCode}");
          return new List<BannedPatron>();
        }
        var result = await response.Content.ReadFromJsonAsync<BannedPatronsResponse>();
        return result?.BannedPatrons ?? new List<BannedPatron>();
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error fetching banned patrons: {ex.Message}");
        return new List<BannedPatron>();
      }
    }
```

After:
```csharp
    public Task<List<BannedPatron>> GetBannedPatronsAsync(string venueId) =>
      _client.GetAsync<BannedPatronsResponse, List<BannedPatron>>(
        $"/api/plugin/patrons/banned?venueId={venueId}",
        r => r?.BannedPatrons ?? new List<BannedPatron>(),
        new List<BannedPatron>(),
        "get banned patrons");
```

- [ ] **Step 5: `GetActiveEventAsync`** (behavior tightening — see plan header)

Before:
```csharp
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
```

After:
```csharp
    public Task<ActiveEventResponse?> GetActiveEventAsync(string venueId) =>
      _client.GetAsync<ActiveEventResponse, ActiveEventResponse?>(
        $"/api/plugin/events/active?venueId={Uri.EscapeDataString(venueId)}",
        r => r,
        new ActiveEventResponse { Active = false },
        "fetch active event");
```

Two behavior changes here, both intentional and flagged in the plan header: (1) the HTTP-failure branch now logs at `Warning` level via the shared helper instead of `Debug` — matches every sibling method, this one was the odd one out; (2) the exception branch now also returns `new ActiveEventResponse { Active = false }` instead of `null`, unifying both failure modes to the same safe fallback.

- [ ] **Step 6: `GetRoomsAsync`**

Before:
```csharp
    public async Task<List<Room>> GetRoomsAsync(string venueId)
    {
      if (!_client.IsConfigured) return new List<Room>();
      try
      {
        var response = await _client.Http.GetAsync($"{_client.BaseUrl}/api/plugin/rooms?venueId={venueId}");
        if (!response.IsSuccessStatusCode)
        {
          Plugin.Log.Warning($"Failed to get rooms: {response.StatusCode}");
          return new List<Room>();
        }
        var result = await response.Content.ReadFromJsonAsync<RoomsResponse>();
        return result?.Rooms ?? new List<Room>();
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error fetching rooms: {ex.Message}");
        return new List<Room>();
      }
    }
```

After:
```csharp
    public Task<List<Room>> GetRoomsAsync(string venueId) =>
      _client.GetAsync<RoomsResponse, List<Room>>(
        $"/api/plugin/rooms?venueId={venueId}",
        r => r?.Rooms ?? new List<Room>(),
        new List<Room>(),
        "get rooms");
```

- [ ] **Step 7: `GetInventoryEnabledAsync`** (behavior tightening — see plan header)

Before:
```csharp
    public async Task<bool> GetInventoryEnabledAsync(string venueId)
    {
      if (!_client.IsConfigured) return false;
      try
      {
        var response = await _client.Http.GetAsync($"{_client.BaseUrl}/api/plugin/inventory-settings?venueId={venueId}");
        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<XIVAppInventorySettingsResponse>();
        return result?.Enabled ?? false;
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error fetching inventory settings: {ex.Message}");
        return false;
      }
    }
```

After:
```csharp
    public Task<bool> GetInventoryEnabledAsync(string venueId) =>
      _client.GetAsync<XIVAppInventorySettingsResponse, bool>(
        $"/api/plugin/inventory-settings?venueId={venueId}",
        r => r?.Enabled ?? false,
        false,
        "get inventory settings");
```

The HTTP-failure branch now logs a warning (it didn't before) — this is the specific gap called out in the plan header.

- [ ] **Step 8: `SetRoomStatusAsync`**

Before:
```csharp
    public async Task<LogTransactionResult> SetRoomStatusAsync(string venueId, string roomId, bool isOccupied, string? note)
    {
      if (!_client.IsConfigured)
        return new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." };

      try
      {
        var request = new XIVAppSetRoomStatusRequest
        {
          VenueId = venueId,
          RoomId = roomId,
          IsOccupied = isOccupied,
          Note = note,
        };
        var response = await _client.Http.PostAsJsonAsync($"{_client.BaseUrl}/api/plugin/rooms/status", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to set room status: {response.StatusCode} - {error}");
          return new LogTransactionResult { Success = false, Error = error };
        }
        return new LogTransactionResult { Success = true };
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error setting room status: {ex.Message}");
        return new LogTransactionResult { Success = false, Error = ex.Message };
      }
    }
```

After:
```csharp
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
```

- [ ] **Step 9: `LinkItemAsync`**

Before:
```csharp
    public async Task<LogTransactionResult> LinkItemAsync(string venueId, string serviceId, int itemId, string itemName, int? iconId)
    {
      if (!_client.IsConfigured)
        return new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." };

      try
      {
        var request = new XIVAppLinkItemRequest
        {
          VenueId = venueId,
          ServiceId = serviceId,
          ItemId = itemId,
          ItemName = itemName,
          IconId = iconId,
        };
        var response = await _client.Http.PostAsJsonAsync($"{_client.BaseUrl}/api/plugin/inventory/link-item", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to link item: {response.StatusCode} - {error}");
          return new LogTransactionResult { Success = false, Error = error };
        }
        return new LogTransactionResult { Success = true };
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error linking item: {ex.Message}");
        return new LogTransactionResult { Success = false, Error = ex.Message };
      }
    }
```

After:
```csharp
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
```

- [ ] **Step 10: `RestockAsync`**

Before:
```csharp
    public async Task<LogTransactionResult> RestockAsync(string venueId, string serviceId, int stockCount)
    {
      if (!_client.IsConfigured)
        return new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." };

      try
      {
        var request = new XIVAppRestockRequest { VenueId = venueId, ServiceId = serviceId, StockCount = stockCount };
        var response = await _client.Http.PostAsJsonAsync($"{_client.BaseUrl}/api/plugin/inventory/restock", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to restock: {response.StatusCode} - {error}");
          return new LogTransactionResult { Success = false, Error = error };
        }
        return new LogTransactionResult { Success = true };
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error restocking: {ex.Message}");
        return new LogTransactionResult { Success = false, Error = ex.Message };
      }
    }
```

After:
```csharp
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
```

- [ ] **Step 11: Build**

Run: `dotnet build` (ask before running, same as Task 1).
Expected: clean build. `GetVenuesAsync` untouched.

- [ ] **Step 12: Commit**

```bash
git add VenueManager/XIVAppVenueApi.cs
git commit -m "$(cat <<'EOF'
Migrate XIVAppVenueApi onto the shared GET/POST helpers

9 of 11 methods migrated (GetVenuesAsync excluded — throw-on-failure
semantics don't fit either shared pattern, left as-is).

Two small behavior tightenings while mapping every method onto one
pattern: GetActiveEventAsync now returns the same
ActiveEventResponse{Active=false} fallback on both HTTP failure and
exception (was null on exception, a real inconsistency); both
GetServicesAsync and GetInventoryEnabledAsync now log a warning on
HTTP failure like every sibling GET method does (previously silent).
EOF
)"
```

---

## Task 3: Migrate `XIVAppShiftApi.cs` (all 4 methods)

**Files:**
- Modify: `VenueManager/XIVAppShiftApi.cs`

- [ ] **Step 1: `GetShiftsResponseAsync`**

Before:
```csharp
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
```

After:
```csharp
    public Task<ShiftsResponse> GetShiftsResponseAsync(string venueId) =>
      _client.GetAsync<ShiftsResponse, ShiftsResponse>(
        $"/api/plugin/shifts?venueId={venueId}",
        r => r ?? new ShiftsResponse(),
        new ShiftsResponse(),
        "get shifts");
```

- [ ] **Step 2: `ClaimShiftAsync`**

Before:
```csharp
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
```

After:
```csharp
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
```

Note: the original's HTTP-failure branch here did NOT call `Plugin.Log.Warning` (unlike `SetRoomStatusAsync`/`LinkItemAsync`/etc., which do) — the shared `PostForResultAsync` helper always logs on HTTP failure (see Task 1's helper body), so this migration adds that warning log call for `ClaimShiftAsync` too. This is the same category of gap as the two GET methods in Task 2 — flag it in the commit, don't silently absorb it.

Also note the not-configured message changes from `"API not configured"` to `"API not configured. Please set your API key in settings."` — the standardization called out in the plan header.

- [ ] **Step 3: `ClockInAsync`**

Before:
```csharp
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
```

After:
```csharp
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
```

Same not-logged-on-failure gap and message-standardization as Step 2 — same fix, same reasoning.

- [ ] **Step 4: `ClockOutAsync`**

Before:
```csharp
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
```

After:
```csharp
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
```

Same gap/standardization as Steps 2-3.

- [ ] **Step 5: Build**

Run: `dotnet build` (ask before running).
Expected: clean build.

- [ ] **Step 6: Commit**

```bash
git add VenueManager/XIVAppShiftApi.cs
git commit -m "$(cat <<'EOF'
Migrate XIVAppShiftApi onto the shared GET/POST helpers

All 4 methods migrated. Two flagged tightenings, consistent with
Task 2: the "not configured" message now matches Venue/Patron's
fuller text instead of this file's shorter "API not configured";
all 3 POST methods now log a warning on HTTP failure (previously
silent here, unlike every Venue/Patron POST method).
EOF
)"
```

---

## Task 4: Migrate `XIVAppPatronApi.cs` (2 of 4 methods)

**Files:**
- Modify: `VenueManager/XIVAppPatronApi.cs`

`LogPatronVisitAsync` and `LogServiceAsync` are excluded per Task 0 — do not touch them. `LogTransactionAsync` and `BanPatronAsync` migrate.

- [ ] **Step 1: `LogTransactionAsync`**

Before:
```csharp
    public async Task<LogTransactionResult> LogTransactionAsync(
      string venueId,
      string? serviceId,
      decimal amount,
      string? customerName = null,
      string? notes = null,
      string? type = null)
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
          Type = type,
        };
        var response = await _client.Http.PostAsJsonAsync($"{_client.BaseUrl}/api/plugin/transactions", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to log transaction: {response.StatusCode} - {error}");
          return new LogTransactionResult { Success = false, Error = error };
        }
        var body = await response.Content.ReadFromJsonAsync<XIVAppTransactionResponse>();
        return new LogTransactionResult
        {
          Success = true,
          ServiceId = body?.Transaction?.ServiceId,
          ServiceStockCount = body?.Transaction?.ServiceStockCount,
        };
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error logging transaction: {ex.Message}");
        return new LogTransactionResult { Success = false, Error = ex.Message };
      }
    }
```

Keep the doc comment above this method (`/// Log a sale (or other transaction type)...`) exactly as-is — only the method body changes.

After:
```csharp
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
```

- [ ] **Step 2: `BanPatronAsync`**

Before:
```csharp
    public async Task<LogTransactionResult> BanPatronAsync(string venueId, string characterName, string world, string reason)
    {
      if (!_client.IsConfigured)
        return new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." };

      try
      {
        var request = new XIVAppBanPatronRequest
        {
          VenueId = venueId,
          CharacterName = characterName,
          World = world,
          Reason = reason,
        };
        var response = await _client.Http.PostAsJsonAsync($"{_client.BaseUrl}/api/plugin/patrons/ban", request);
        if (!response.IsSuccessStatusCode)
        {
          var error = await response.Content.ReadAsStringAsync();
          Plugin.Log.Warning($"Failed to ban patron: {response.StatusCode} - {error}");
          return new LogTransactionResult { Success = false, Error = error };
        }
        return new LogTransactionResult { Success = true };
      }
      catch (Exception ex)
      {
        Plugin.Log.Warning($"Error banning patron: {ex.Message}");
        return new LogTransactionResult { Success = false, Error = ex.Message };
      }
    }
```

Keep the doc comment above this method exactly as-is — only the method body changes.

After:
```csharp
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
```

- [ ] **Step 3: Build**

Run: `dotnet build` (ask before running).
Expected: clean build. `LogPatronVisitAsync`/`LogServiceAsync` untouched.

- [ ] **Step 4: Commit**

```bash
git add VenueManager/XIVAppPatronApi.cs
git commit -m "$(cat <<'EOF'
Migrate XIVAppPatronApi onto the shared GET/POST helpers

2 of 4 methods migrated (LogPatronVisitAsync, LogServiceAsync
excluded — throw-on-not-configured semantics don't fit the shared
pattern, left as-is per Task 0). No behavior gaps found in these two
methods; migration is a pure refactor, no tightening needed.
EOF
)"
```

---

## Final verification (required before considering this plan done)

This plan cannot be verified by an automated test suite — there isn't one in this repo. Before shipping:

1. `dotnet build` in Release config, confirm clean (per this project's established deploy workflow — build Release, don't rely on dev-plugin hot-loading).
2. **Manual in-game pass covering every migrated method's real trigger**, not just a build check:
   - Load the Venues tab (exercises `GetServicesAsync`, `GetRolesAsync`, `GetActiveEventAsync`, `GetRoomsAsync`, `GetInventoryEnabledAsync`).
   - Load the VIP/Ban list tabs (`GetVipPatronsAsync`, `GetBannedPatronsAsync`).
   - Toggle a room's occupied status (`SetRoomStatusAsync`).
   - Link an inventory item and restock it (`LinkItemAsync`, `RestockAsync`).
   - Load the Shifts tab (`GetShiftsResponseAsync`), claim a shift, clock in, clock out (`ClaimShiftAsync`, `ClockInAsync`, `ClockOutAsync`).
   - Log a transaction/sale (`LogTransactionAsync`), ban a test patron (`BanPatronAsync`).
   - **Also test at least one failure path deliberately** (e.g. temporarily point `BaseUrl` at an invalid host, or disconnect network) for at least one GET and one POST method, to confirm the fallback/error-result behavior still works and nothing throws unexpectedly.
3. Only after that pass, consider this plan complete — this is a live production plugin with real venue operators depending on it, and there is no CI safety net here the way there was for the web app plan.

## Self-review notes

- All 19 methods across 3 files accounted for: 16 migrated (Tasks 2-4), 3 explicitly excluded with reasoning (Task 0).
- 4 flagged behavior tightenings, each with a stated reason, none silent: `GetActiveEventAsync`'s fallback unification, `GetServicesAsync`'s and `GetInventoryEnabledAsync`'s missing warning logs, all 3 `XIVAppShiftApi` POST methods' missing warning logs + message standardization.
- Public method signatures are unchanged everywhere — this is purely an internal-implementation refactor, so no caller anywhere in the plugin (UI tabs, command handlers) needs to change.
- Helper names (`GetAsync<TResponse,TResult>`, `PostForResultAsync<TRequest,TResult>`) are used identically between Task 1's definition and Tasks 2-4's call sites — verified consistent.

---

## Follow-up (not in this plan, flagged during review): dedupe the repeated `PostForResultAsync` lambdas

5 of the 8 migrated POST call sites (`SetRoomStatusAsync`, `LinkItemAsync`, `RestockAsync` in `XIVAppVenueApi.cs`; `BanPatronAsync` in `XIVAppPatronApi.cs`; `ClockInAsync` in `XIVAppShiftApi.cs`) pass the identical `notConfigured`/`onFailure`/plain-`onSuccess` lambda trio:

```csharp
() => new LogTransactionResult { Success = false, Error = "API not configured. Please set your API key in settings." },
error => new LogTransactionResult { Success = false, Error = error },
_ => Task.FromResult(new LogTransactionResult { Success = true }));
```

Only `path`, `request`, and `errorContext` actually differ between these 5. The 3 methods that need real response-body parsing (`ClaimShiftAsync`, `ClockOutAsync`, `LogTransactionAsync`) still need the full 3-lambda form and should keep using it.

Fix: add a convenience overload of `PostForResultAsync` for the "simple success, no body parsing" shape — something like `PostForResultAsync<TRequest>(path, request, errorContext)` that builds the 3 standard `LogTransactionResult` cases itself, called by the 5 simple sites; leave the full 3-lambda overload for the 3 methods that need it.

Flagged by 2 independent code-quality reviews during the original plan (Tasks 2 and 3), both explicitly non-blocking — small, and reworking the helper mid-plan would have meant re-touching everything already migrated. Worth a standalone follow-up now that the main plan is done.
