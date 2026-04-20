using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Bindings.ImGui;

using System.Drawing;
using System.Linq;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Memory;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Map = Lumina.Excel.Sheets.Map;
using VenueManager.UI;

namespace VenueManager.Tabs;

public class SettingsTab
{
  private Plugin plugin;
  private Configuration configuration;

  // Status line shown under Fetch Venues so users see success/failure of
  // button-press actions instead of a silent no-op. Updated by the async
  // Fetch* methods below. Default color is the muted overlay so a fresh
  // "Fetching…" line reads as in-progress, not success/failure.
  private string xivAppStatus = "";
  private Vector4 xivAppStatusColor = Colors.CatOverlay0;

  // UI-only toggle — not persisted. Keys are sensitive; default hidden so
  // screenshots/shares don't leak. Flipped by the eye icon next to the input.
  private bool showApiKey = false;

  private const string DefaultServerUrl = "https://xivvenuemanager.com";

  private static Vector4 StatusOk   => Colors.StatusOk;
  private static Vector4 StatusWarn => Colors.StatusWarn;
  private static Vector4 StatusErr  => Colors.StatusErr;

  public SettingsTab(Plugin plugin)
  {
    this.plugin = plugin;
    this.configuration = plugin.Configuration;
  }

  // Draw settings menu
  public unsafe void draw()
  {
    ImGui.BeginChild("SettingsRoot");
    ImGui.Indent(5);

    // XIV-App Sync is the plugin's primary workflow — surface it first so a
    // fresh install lands on the setup the user actually needs to do.
    DrawXivAppSettings();

    DrawSectionSeparator();
    DrawSectionHeader("Status Bar (DTR)");
    DrawDtrSettings();

    DrawSectionSeparator();
    DrawSectionHeader("Tab Visibility");
    DrawTabVisibility();

    DrawSectionSeparator();
    DrawSectionHeader("Patron List");
    DrawPatronListSettings();

    DrawSectionSeparator();
    DrawPatronChatAlerts();

    DrawSectionSeparator();
    DrawPatronSoundAlerts();

    DrawSectionSeparator();
    DrawDebugInfo();

    ImGui.Unindent();
    ImGui.EndChild();
  }

  // -- Section helpers ------------------------------------------------------

  // Single separator rhythm across the tab. Previously mixed Separator() and
  // Separator()+Spacing() — now every break is identical.
  private static void DrawSectionSeparator()
  {
    ImGui.Separator();
    ImGui.Spacing();
  }

  // Blue accent lets users scan section boundaries at a glance on a long tab.
  private static void DrawSectionHeader(string label)
  {
    ImGui.TextColored(Colors.CatBlue, label);
  }

  // -- Tab Visibility -------------------------------------------------------

  private void DrawTabVisibility()
  {
    var showGuestsTab = this.configuration.showGuestsTab;
    if (ImGui.Checkbox("Patrons Tab", ref showGuestsTab))
    {
      this.configuration.showGuestsTab = showGuestsTab;
      this.configuration.Save();
    }
    ImGui.Indent(20);

    // Guest tab sub settings
    if (!this.configuration.showGuestsTab) ImGui.BeginDisabled();
    ImGui.TextWrapped("Hiding the Patrons Tab will also disable all notifications around patrons entering or leaving.");
    if (!this.configuration.showGuestsTab) ImGui.EndDisabled();

    ImGui.Unindent();
    var showVenueTab = this.configuration.showVenueTab;
    if (ImGui.Checkbox("Venues Tab", ref showVenueTab))
    {
      this.configuration.showVenueTab = showVenueTab;
      this.configuration.Save();
    }

    if (!this.configuration.showGuestsTab && !this.configuration.showVenueTab)
    {
      ImGui.TextColored(Colors.StripMuted, "Both tabs hidden — re-enable at least one to see patron data.");
    }
  }

  // -- Status Bar (DTR) -----------------------------------------------------
  //
  // Lets users pick what (if anything) the plugin surfaces in the game's
  // world/time strip. Dalamud already handles show/hide + reordering via
  // /xlsettings → Server Info Bar, so we only own the *content*.
  private void DrawDtrSettings()
  {
    ImGui.TextWrapped("Show a short plugin status in the game's Server Info Bar (where world/time displays). Reorder or hide via Dalamud settings → Server Info Bar.");

    var mode = this.configuration.dtrDisplayMode;
    if (ImGui.BeginCombo("Display##dtrMode", DtrLabel(mode)))
    {
      foreach (var option in (DtrDisplayMode[])Enum.GetValues(typeof(DtrDisplayMode)))
      {
        var selected = option == mode;
        if (ImGui.Selectable(DtrLabel(option), selected))
        {
          this.configuration.dtrDisplayMode = option;
          this.configuration.Save();
          this.plugin.UpdateDtrBar(force: true);
        }
        if (selected) ImGui.SetItemDefaultFocus();
      }
      ImGui.EndCombo();
    }

    ImGui.TextColored(Colors.StripMuted, DtrDescription(mode));
  }

  private static string DtrLabel(DtrDisplayMode mode) => mode switch
  {
    DtrDisplayMode.Disabled     => "Disabled",
    DtrDisplayMode.PatronCount  => "Patron count",
    DtrDisplayMode.VenueName    => "Venue name",
    DtrDisplayMode.SessionSales => "Session sales",
    DtrDisplayMode.ShiftStatus  => "Shift status",
    DtrDisplayMode.Combined     => "Combined",
    _ => mode.ToString(),
  };

  private static string DtrDescription(DtrDisplayMode mode) => mode switch
  {
    DtrDisplayMode.Disabled     => "Nothing shown in the Server Info Bar.",
    DtrDisplayMode.PatronCount  => "Patron count while inside a venue, e.g. \"VM: 12 patrons\".",
    DtrDisplayMode.VenueName    => "Name of the current venue (falls back to ward/plot if unlinked).",
    DtrDisplayMode.SessionSales => "Running tally of sales logged this session.",
    DtrDisplayMode.ShiftStatus  => "Clock-in status: \"On shift 1h23m\", \"Shift in 45m\", or \"Off shift\".",
    DtrDisplayMode.Combined     => "Shift • patrons • venue • sales • snooze — whichever apply right now.",
    _ => "",
  };

  // -- Patron List ----------------------------------------------------------

  private void DrawPatronListSettings()
  {
    var sortCurrentVisitorsTop = this.configuration.sortCurrentVisitorsTop;
    if (ImGui.Checkbox("Pin current visitors to top", ref sortCurrentVisitorsTop))
    {
      this.configuration.sortCurrentVisitorsTop = sortCurrentVisitorsTop;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered()) {
      ImGui.SetTooltip("Pin current visitors to the top of the patron list");
    }

    var sortFriendsToTop = this.configuration.sortFriendsToTop;
    if (ImGui.Checkbox("Pin friends to top", ref sortFriendsToTop))
    {
      this.configuration.sortFriendsToTop = sortFriendsToTop;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered()) {
      ImGui.SetTooltip("Pin friends to the top of the patron list");
    }
  }

  // -- Chat Alerts ----------------------------------------------------------

  private void DrawPatronChatAlerts()
  {
    // Chat + sound alerts both depend on the Patrons tab being visible — the
    // whole subtree reads patron events that the tab owns.
    if (!this.configuration.showGuestsTab) ImGui.BeginDisabled();

    DrawSectionHeader("Patron Chat Alerts");
    var showChatAlerts = this.configuration.showChatAlerts;
    if (ImGui.Checkbox("Enabled##showChatAlerts", ref showChatAlerts))
    {
      this.configuration.showChatAlerts = showChatAlerts;
      this.configuration.Save();
    }

    if (!this.configuration.showChatAlerts) ImGui.BeginDisabled();
    ImGui.Indent(20);
    // Entry Alerts
    var showChatAlertEntry = this.configuration.showChatAlertEntry;
    if (ImGui.Checkbox("Entry Alerts", ref showChatAlertEntry))
    {
      this.configuration.showChatAlertEntry = showChatAlertEntry;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Display chat message when a patron enters a venue");
    }

    // Reentry Alerts
    var showChatAlertReentry = this.configuration.showChatAlertReentry;
    if (ImGui.Checkbox("Re-entry Alerts", ref showChatAlertReentry))
    {
      this.configuration.showChatAlertReentry = showChatAlertReentry;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Display chat message when a patron re-enters a venue after leaving");
    }

    // Current Visitor
    var showChatAlertAlreadyHere = this.configuration.showChatAlertAlreadyHere;
    if (ImGui.Checkbox("Current Visitors on Entry", ref showChatAlertAlreadyHere))
    {
      this.configuration.showChatAlertAlreadyHere = showChatAlertAlreadyHere;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Display chat message for all current patrons when re-entering a house");
    }

    // Leave Alerts
    var showChatAlertLeave = this.configuration.showChatAlertLeave;
    if (ImGui.Checkbox("Leave Alerts", ref showChatAlertLeave))
    {
      this.configuration.showChatAlertLeave = showChatAlertLeave;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Display chat message when a patron leaves");
    }

    // Visual break: "Include Plugin Name" is a formatting/display preference,
    // not an event toggle — separate it from the four event alerts above.
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();

    // Include plugin name in alerts
    var showPluginNameInChat = this.configuration.showPluginNameInChat;
    if (ImGui.Checkbox("Include Plugin Name", ref showPluginNameInChat))
    {
      this.configuration.showPluginNameInChat = showPluginNameInChat;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Prefix patron chat alerts with \"[XIV Venue Manager Sync]\" so they stand out from regular chat.");
    }

    if (!this.configuration.showChatAlerts) ImGui.EndDisabled();
    ImGui.Unindent();
    if (!this.configuration.showGuestsTab) ImGui.EndDisabled();
  }

  // -- Sound Alerts ---------------------------------------------------------

  private void DrawPatronSoundAlerts()
  {
    // Sound alerts are gated on showGuestsTab for the same reason as chat
    // alerts — the tab owns the patron-event source that fires the doorbell.
    // Grayed (not removed) when the tab is hidden so users can still see the
    // section exists and understand why it's inactive.
    if (!this.configuration.showGuestsTab) ImGui.BeginDisabled();

    DrawSectionHeader("Patron Sound Alerts");
    // Enable / Disable sound alerts
    var soundAlerts = this.configuration.soundAlerts;
    if (ImGui.Checkbox("Enabled##soundAlerts", ref soundAlerts))
    {
      this.configuration.soundAlerts = soundAlerts;
      this.configuration.Save();
    }
    if (!this.configuration.soundAlerts) ImGui.BeginDisabled();
    // Allow the user to select which doorbell sound they would like
    if (ImGui.BeginCombo("Doorbell sound", DoorbellSound.DoorbellSoundTypes[(int)configuration.doorbellType]))
    {
      var doorbells = (DOORBELL_TYPE[])Enum.GetValues(typeof(DOORBELL_TYPE));
      for (int i = 0; i < doorbells.Length; i++)
      {
        bool is_selected = configuration.doorbellType == doorbells[i];
        if (ImGui.Selectable(DoorbellSound.DoorbellSoundTypes[i], is_selected))
        {
          configuration.doorbellType = doorbells[i];
          configuration.Save();
          plugin.reloadDoorbell();
        }
        if (is_selected)
          ImGui.SetItemDefaultFocus();
      }
      ImGui.EndCombo();
    }
    ImGui.SameLine();
    if (ImGuiComponents.IconButton(FontAwesomeIcon.Music))
    {
      plugin.playDoorbell();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Play test sound");
    }
    var volume = this.configuration.soundVolume;
    if (ImGui.SliderFloat("Volume", ref volume, 0, 5))
    {
      this.configuration.soundVolume = volume;
      configuration.Save();
      plugin.reloadDoorbell();
    }

    if (!this.configuration.soundAlerts) ImGui.EndDisabled();
    if (!this.configuration.showGuestsTab) ImGui.EndDisabled();
  }

  // -- Debug Info -----------------------------------------------------------

  private unsafe void DrawDebugInfo()
  {
    // Support-surface content — only interesting when debugging a ticket, so
    // collapsed by default. InputTextMultiline ReadOnly gives users a copy-
    // paste target for bug reports instead of having to retype fields.
    if (!ImGui.CollapsingHeader("Debug Info"))
      return;

    string body;
    try {
      var housingManager = HousingManager.Instance();
      var mapData = Plugin.DataManager.GetExcelSheet<Map>().GetRow(AgentMap.Instance()->SelectedMapId);
      string[] parts = mapData.PlaceName.Value.Name.ExtractText().Split(" - ");
      string district = parts.Length == 2 ? parts[1] : "";

      body =
        $"Territory Id: {plugin.pluginState.territory}\n" +
        $"In House: {plugin.pluginState.userInHouse}\n\n" +
        $"HouseID: {housingManager->GetCurrentIndoorHouseId()}\n" +
        $"Plot: {housingManager->GetCurrentPlot() + 1}\n" +
        $"Ward: {housingManager->GetCurrentWard() + 1}\n" +
        $"Room: {housingManager->GetCurrentRoom()}\n" +
        $"Division: {housingManager->GetCurrentDivision()}\n" +
        $"District: {district}\n" +
        $"PlaceName: {mapData.PlaceName.Value.Name.ExtractText()}";
    } catch (Exception ex) {
      // Housing-manager reads fail outside houses. Swallow, but log once so
      // support tickets can correlate a blank Debug block with a real cause.
      Plugin.Log.Debug("Debug info read failed: {0}", ex.Message);
      body = $"(unavailable — not currently in a house)\n\n{ex.Message}";
    }

    ImGui.InputTextMultiline(
      "##debuginfo",
      ref body,
      4096,
      new Vector2(-1, ImGui.GetTextLineHeightWithSpacing() * 10),
      ImGuiInputTextFlags.ReadOnly);
  }

  // -- XIV-App Sync ---------------------------------------------------------

  public unsafe void DrawXivAppSettings()
  {
    DrawSectionHeader("XIV-App Sync");
    ImGui.TextColored(Colors.CatSubtext0, "Sync patrons, sales, and shifts with xivvenuemanager.com.");
    ImGui.Spacing();

    var syncEnabled = this.configuration.syncToXivApp;
    if (ImGui.Checkbox("Enable XIV-App Sync", ref syncEnabled))
    {
      this.configuration.syncToXivApp = syncEnabled;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Sync patron visits to XIV-App website");
    }

    if (!this.configuration.syncToXivApp) ImGui.BeginDisabled();

    ImGui.Indent(10);

    DrawApiKeyInput();
    DrawServerUrlInput();
    DrawFetchVenuesButton();
    DrawVenueSelector();

    ImGui.Unindent(10);

    if (!this.configuration.syncToXivApp) ImGui.EndDisabled();
  }

  // API Key input — masked by default, eye icon flips visibility. We trim on
  // every keystroke to strip the whitespace/newlines that regularly sneak in
  // from Discord copy-paste; those cause HttpClient.DefaultRequestHeaders.Add
  // to throw a FormatException and the key was silently never applied.
  private void DrawApiKeyInput()
  {
    var apiKey = this.configuration.xivAppApiKey ?? "";
    var flags = showApiKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
    if (ImGui.InputText("API Key", ref apiKey, 128, flags))
    {
      this.configuration.xivAppApiKey = apiKey.Trim();
      this.configuration.Save();
      ReconfigureXivAppClient();
    }
    // Auto-fetch once the user finishes editing the key (blur), so a paste +
    // click-elsewhere is enough — no separate Fetch Venues click required.
    if (ImGui.IsItemDeactivatedAfterEdit() && !string.IsNullOrEmpty(this.configuration.xivAppApiKey))
    {
      _ = FetchXivAppVenuesAsync();
    }
    ImGui.SameLine();
    if (ImGuiComponents.IconButton(showApiKey ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye))
    {
      showApiKey = !showApiKey;
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip(showApiKey ? "Hide API key" : "Show API key");
    }

    // Inline validation feedback so users aren't left guessing why Fetch
    // Venues does nothing when their key is malformed. Yellow (warn), not
    // red (error) — the user is mid-entry, nothing has failed yet.
    if (!string.IsNullOrEmpty(this.configuration.xivAppApiKey) && !this.configuration.xivAppApiKey.StartsWith("vm_"))
    {
      ImGui.TextColored(StatusWarn, "Key must start with 'vm_' — generate one at xivvenuemanager.com/dashboard/api-keys.");
    }
  }

  private void DrawServerUrlInput()
  {
    var serverUrl = this.configuration.xivAppServerUrl ?? "";
    if (ImGui.InputTextWithHint("Server URL", DefaultServerUrl, ref serverUrl, 256))
    {
      this.configuration.xivAppServerUrl = serverUrl.Trim();
      this.configuration.Save();
      ReconfigureXivAppClient();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip($"Leave blank to use the default ({DefaultServerUrl}). Override only for self-hosted instances.");
    }
    if (ImGui.IsItemDeactivatedAfterEdit() && !string.IsNullOrEmpty(this.configuration.xivAppApiKey))
    {
      _ = FetchXivAppVenuesAsync();
    }
  }

  private void DrawFetchVenuesButton()
  {
    if (ImGui.Button("Fetch Venues"))
    {
      _ = FetchXivAppVenuesAsync();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Refresh the list of venues your API key has access to. The plugin fetches automatically when you finish editing the key or URL, so you only need this after changing permissions on the website.");
    }
    // Reserve the row whether or not a status is active so appearing/
    // disappearing text doesn't shove the rest of the section up and down.
    if (!string.IsNullOrEmpty(xivAppStatus))
    {
      ImGui.TextColored(xivAppStatusColor, xivAppStatus);
    }
    else
    {
      ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeight()));
    }
  }

  private void DrawVenueSelector()
  {
    if (plugin.xivAppVenues.Count == 0) return;

    var selectedVenueId = this.configuration.selectedVenueId ?? "";
    var selectedVenue = plugin.xivAppVenues.FirstOrDefault(v => v.Id == selectedVenueId);
    var displayName = selectedVenue?.Name ?? "Select Venue";

    if (ImGui.BeginCombo("Venue", displayName))
    {
      foreach (var venue in plugin.xivAppVenues)
      {
        bool isSelected = venue.Id == selectedVenueId;
        if (ImGui.Selectable(venue.Name, isSelected))
        {
          this.configuration.selectedVenueId = venue.Id;
          this.configuration.Save();
          // Track the active venue separately from Configuration so other
          // code paths (service logging, role dropdowns) have a single
          // source of truth that updates the instant the user switches.
          plugin.currentXivAppVenueId = venue.Id;
          _ = LoadVenueDataWithFeedbackAsync(venue.Id, venue.Name);
        }
        if (isSelected)
          ImGui.SetItemDefaultFocus();
      }
      ImGui.EndCombo();
    }

    // Level-2 visibility: show what we actually fetched for the active
    // venue. Truncated with hover-to-reveal — a venue with many roles or
    // services wraps ugly otherwise, and users need to see Services for
    // the Sales tab workflow (what they can log).
    DrawTruncatedNameList("Roles", plugin.xivAppRoles.ConvertAll(r => r.Name));
    DrawTruncatedNameList("Services", plugin.availableServices.ConvertAll(s => s.Name));
  }

  // Shared formatter for the "Roles: …" / "Services: …" status lines under
  // the venue selector. Caps to maxShow and reveals the rest on hover.
  private static void DrawTruncatedNameList(string label, List<string> names, int maxShow = 3)
  {
    if (names.Count == 0)
    {
      ImGui.TextDisabled($"{label}: (none fetched yet)");
      return;
    }

    string display;
    if (names.Count <= maxShow)
    {
      display = string.Join(", ", names);
    }
    else
    {
      display = string.Join(", ", names.GetRange(0, maxShow)) + $", +{names.Count - maxShow} more";
    }
    ImGui.TextDisabled($"{label}: {display}");
    if (names.Count > maxShow && ImGui.IsItemHovered())
    {
      ImGui.SetTooltip(string.Join("\n", names));
    }
  }

  // Cascade wrapper: flips xivAppStatus so the user sees roles+services are
  // being fetched instead of a silent pause after picking a venue. Writes
  // terminal state ("✓ Loaded: …") regardless of per-fetch errors because
  // the individual Fetch*Async helpers already log + swallow; partial data
  // is still useful to surface.
  private async Task LoadVenueDataWithFeedbackAsync(string venueId, string venueName)
  {
    xivAppStatus = $"Loading roles + services for {venueName}…";
    xivAppStatusColor = Colors.CatOverlay0;

    await FetchXivAppRolesAsync(venueId);
    await FetchXivAppServicesAsync(venueId);

    xivAppStatus = $"✓ Loaded: {plugin.xivAppRoles.Count} roles, {plugin.availableServices.Count} services";
    xivAppStatusColor = StatusOk;
  }

  // Lazy-configure the XIV-App client from current Configuration. The client
  // itself is always non-null (created at Plugin load), so callers don't need
  // null checks — IsConfigured tells them whether a key+URL are set.
  private void ReconfigureXivAppClient()
  {
    if (plugin.xivAppClient == null) return;
    if (string.IsNullOrEmpty(this.configuration.xivAppApiKey)) return;
    try
    {
      // Fall back to the public host when the URL field is blank — users who
      // leave the hint in place expect it to "just work".
      var url = string.IsNullOrWhiteSpace(this.configuration.xivAppServerUrl)
        ? DefaultServerUrl
        : this.configuration.xivAppServerUrl;
      plugin.xivAppClient.Configure(this.configuration.xivAppApiKey, url);
    }
    catch (Exception ex)
    {
      xivAppStatus = $"Invalid key or URL: {ex.Message}";
      xivAppStatusColor = StatusErr;
      Plugin.Log.Error("XIV-App Configure failed: {0}", ex.Message);
    }
  }

  private async Task FetchXivAppVenuesAsync()
  {
    try {
      if (plugin.xivAppClient == null)
      {
        xivAppStatus = "XIV-App client not initialized — restart the plugin.";
        xivAppStatusColor = StatusErr;
        return;
      }
      // Self-heal: if a key was just pasted and ReconfigureXivAppClient hasn't
      // fired yet (or silently failed), configure again before the request.
      if (!plugin.xivAppClient.IsConfigured && !string.IsNullOrEmpty(this.configuration.xivAppApiKey))
      {
        ReconfigureXivAppClient();
      }
      if (!plugin.xivAppClient.IsConfigured)
      {
        xivAppStatus = "Enter your API key first (generate one at xivvenuemanager.com).";
        xivAppStatusColor = StatusWarn;
        return;
      }

      xivAppStatus = "Fetching…";
      xivAppStatusColor = Colors.CatOverlay0;

      plugin.xivAppVenues = await plugin.xivAppClient.GetVenuesAsync();
      Plugin.Log.Information("Fetched {Count} venues from XIV-App", plugin.xivAppVenues.Count);

      if (plugin.xivAppVenues.Count == 0)
      {
        xivAppStatus = "No venues found for this key — check that the key is scoped to a venue you own or staff.";
        xivAppStatusColor = StatusWarn;
        return;
      }

      xivAppStatus = $"✓ Fetched {plugin.xivAppVenues.Count} venue(s)";
      xivAppStatusColor = StatusOk;

      // Auto-select first venue if none selected, or re-hydrate roles/services
      // for a previously-selected venue so "(none fetched yet)" doesn't stick
      // on first load of an already-configured plugin.
      string? targetVenueId = null;
      string? targetVenueName = null;
      if (string.IsNullOrEmpty(this.configuration.selectedVenueId))
      {
        targetVenueId = plugin.xivAppVenues[0].Id;
        targetVenueName = plugin.xivAppVenues[0].Name;
        this.configuration.selectedVenueId = targetVenueId;
        this.configuration.Save();
      }
      else
      {
        var match = plugin.xivAppVenues.FirstOrDefault(v => v.Id == this.configuration.selectedVenueId);
        if (match != null)
        {
          targetVenueId = match.Id;
          targetVenueName = match.Name;
        }
      }

      if (targetVenueId != null)
      {
        plugin.currentXivAppVenueId = targetVenueId;
        await LoadVenueDataWithFeedbackAsync(targetVenueId, targetVenueName ?? "venue");
      }
    } catch (Exception ex) {
      xivAppStatus = $"✗ {ex.Message}";
      xivAppStatusColor = StatusErr;
      Plugin.Log.Error("Failed to fetch venues: {0}", ex.Message);
    }
  }

  private async Task FetchXivAppRolesAsync(string venueId)
  {
    try {
      if (plugin.xivAppClient == null || !plugin.xivAppClient.IsConfigured) return;

      var roles = await plugin.xivAppClient.GetRolesAsync(venueId);
      // Store the result so the Settings indicator (and any future role
      // dropdown) can read it. Previously we logged-and-discarded — that
      // was the "roles not updating" bug.
      plugin.xivAppRoles = roles;
      Plugin.Log.Information("Fetched {Count} roles for venue {VenueId}", roles.Count, venueId);
    } catch (Exception ex) {
      Plugin.Log.Error("Failed to fetch roles: {0}", ex.Message);
    }
  }

  private async Task FetchXivAppServicesAsync(string venueId)
  {
    try {
      if (plugin.xivAppClient == null || !plugin.xivAppClient.IsConfigured) return;

      var response = await plugin.xivAppClient.GetServicesAsync(venueId);
      if (response == null)
      {
        plugin.availableServices = new List<Service>();
        Plugin.Log.Warning("No services response for venue {VenueId}", venueId);
        return;
      }
      plugin.availableServices = response.Services;
      Plugin.Log.Information("Fetched {Count} services for venue {VenueId}", response.Services.Count, venueId);
    } catch (Exception ex) {
      Plugin.Log.Error("Failed to fetch services: {0}", ex.Message);
    }
  }
}
