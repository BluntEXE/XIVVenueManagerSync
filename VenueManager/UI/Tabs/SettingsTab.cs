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

public class SettingsTab : ITab
{
  private Plugin plugin;
  private Configuration configuration;

  public string Name => "Settings";
  public FontAwesomeIcon Icon => FontAwesomeIcon.Cog;
  public string Tooltip => "Settings";
  public bool IsVisible => true;

  // Status line under Fetch Venues — set by the async Fetch* methods below
  private string xivAppStatus = "";
  private Vector4 xivAppStatusColor = Colors.XivOverlay0;

  // Not persisted; default hidden so screenshots/shares don't leak the key
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

  public unsafe void draw()
  {
    ImGui.BeginChild("SettingsRoot");
    ImGui.Indent(5);

    if (string.IsNullOrEmpty(this.configuration.xivAppApiKey))
      DrawWelcomeBanner();

    // Primary workflow — surface first so a fresh install lands on the setup it needs
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
    DrawGreeterSettings();

    DrawSectionSeparator();
    DrawPatronSoundAlerts();

    DrawSectionSeparator();
    DrawDebugInfo();

    DrawSectionSeparator();
    DrawAboutSection();

    ImGui.Unindent();
    ImGui.EndChild();
  }

  private void DrawAboutSection()
  {
    DrawSectionHeader("About");
    ImGui.TextColored(Colors.XivSubtext0, $"XIV Venue Manager Sync  v{plugin.PluginVersion}  by Ehno");
    ImGui.Spacing();
    float btnW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
    if (ImGui.Button("What's New", new Vector2(btnW, 0)))
      plugin.OpenChangelog();
    ImGui.SameLine();
    if (ImGui.Button("GitHub", new Vector2(btnW, 0)))
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
        FileName = "https://github.com/BluntEXE/XIVVenueManagerSync",
        UseShellExecute = true
      });
  }

  // -- Section helpers ------------------------------------------------------

  private static void DrawSectionSeparator()
  {
    ImGui.Separator();
    ImGui.Spacing();
  }

  private static void DrawSectionHeader(string label)
  {
    ImGui.TextColored(Colors.XivBlue, label);
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

    if (!this.configuration.showGuestsTab) ImGui.BeginDisabled();
    ImGui.TextWrapped("Hiding the Patrons Tab also disables chat/sound alerts and stops logging patron visits to the XVM Website entirely - no Live dashboard or Analytics data will be recorded while this is off.");
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
  // Only owns content — Dalamud handles show/hide + reordering via /xlsettings
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
    // Depends on Patrons tab being visible — it owns the patron events this subtree reads
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

    // "Include Plugin Name" is a display preference, not an event toggle — visually separated
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();

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

  // -- Greeter Automation ---------------------------------------------------

  private void DrawGreeterSettings()
  {
    if (!this.configuration.showGuestsTab) ImGui.BeginDisabled();

    DrawSectionHeader("Greeter Automation");
    ImGui.TextWrapped("Automatically sends a /tell to each patron when they enter the venue.");
    ImGui.Spacing();

    var enableGreeterMode = this.configuration.enableGreeterMode;
    if (ImGui.Checkbox("Enable Auto-Greeter##greeterMode", ref enableGreeterMode))
    {
      this.configuration.enableGreeterMode = enableGreeterMode;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Sends a /tell to each patron as they enter.\nOnly fires for new entries — not for players already inside when you arrive.");
    }

    if (enableGreeterMode)
    {
      ImGui.Indent(20);
      var greeterMessage = this.configuration.greeterMessage;
      if (ImGui.InputTextWithHint("First visit##greeterMsg", "Welcome! Let us know if you need anything ♥", ref greeterMessage, 400))
      {
        this.configuration.greeterMessage = greeterMessage;
        this.configuration.Save();
      }
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Sent on a patron's first entry this session.");
      ImGui.Unindent(20);
    }

    ImGui.Spacing();

    var enableReentryGreeter = this.configuration.enableReentryGreeter;
    if (ImGui.Checkbox("Re-entry Greeter##reentryGreeter", ref enableReentryGreeter))
    {
      this.configuration.enableReentryGreeter = enableReentryGreeter;
      this.configuration.Save();
    }
    if (ImGui.IsItemHovered())
      ImGui.SetTooltip("Sends a different /tell when a patron returns after leaving.\nUseful for DJ announcements or updated venue info.");

    if (enableReentryGreeter)
    {
      ImGui.Indent(20);
      var reentryMsg = this.configuration.reentryGreeterMessage;
      if (ImGui.InputTextWithHint("Re-entry##reentryMsg", "Welcome back!", ref reentryMsg, 400))
      {
        this.configuration.reentryGreeterMessage = reentryMsg;
        this.configuration.Save();
      }
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Sent every time a patron re-enters after leaving.");
      ImGui.Unindent(20);
    }

    if (!this.configuration.showGuestsTab) ImGui.EndDisabled();
  }

  // -- Sound Alerts ---------------------------------------------------------

  private void DrawPatronSoundAlerts()
  {
    // Gated on showGuestsTab like chat alerts; grayed not removed, so users see why it's inactive
    if (!this.configuration.showGuestsTab) ImGui.BeginDisabled();

    DrawSectionHeader("Patron Sound Alerts");
    var soundAlerts = this.configuration.soundAlerts;
    if (ImGui.Checkbox("Enabled##soundAlerts", ref soundAlerts))
    {
      this.configuration.soundAlerts = soundAlerts;
      this.configuration.Save();
    }
    if (!this.configuration.soundAlerts) ImGui.BeginDisabled();
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
    // Collapsed by default; ReadOnly field gives users a copy-paste target for bug reports
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
      // Housing-manager reads fail outside houses — log once for support ticket correlation
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

  // -- Welcome banner (first-run, no API key) --------------------------------

  private void DrawWelcomeBanner()
  {
    ImGui.TextColored(Colors.XivGreen, "Welcome to XIV Venue Manager Sync!");
    ImGui.TextWrapped("To get started, you'll need an API key from xivvenuemanager.com.");
    ImGui.Spacing();
    ImGui.TextWrapped("1. Log in with Discord at xivvenuemanager.com");
    ImGui.TextWrapped("2. Open your venue dashboard, click Settings in the left sidebar (not the top-right account menu), go to API Keys and create a key");
    ImGui.TextWrapped("3. Paste it into the API Key field below");
    ImGui.Spacing();
    if (ImGui.Button("Open xivvenuemanager.com"))
      Util.OpenLink("https://xivvenuemanager.com");
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();
  }

  // -- XIV-App Sync ---------------------------------------------------------

  public unsafe void DrawXivAppSettings()
  {
    DrawSectionHeader("XIV-App Sync");
    ImGui.TextColored(Colors.XivSubtext0, "Sync patrons, sales, and shifts with xivvenuemanager.com.");
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

  // Trim on every keystroke — stray whitespace/newlines from Discord copy-paste otherwise throw in HttpClient.DefaultRequestHeaders.Add
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
    // Auto-fetch on blur so paste + click-elsewhere is enough, no separate Fetch Venues click required
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

    // Warn not error — user is mid-entry, nothing has failed yet
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
    // Reserve the row either way so appearing/disappearing text doesn't shove the section
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
          // Separate from Configuration so other code paths get a single source of truth that updates instantly
          plugin.currentXivAppVenueId = venue.Id;
          _ = LoadVenueDataWithFeedbackAsync(venue.Id, venue.Name);
        }
        if (isSelected)
          ImGui.SetItemDefaultFocus();
      }
      ImGui.EndCombo();
    }

    // Truncated with hover-to-reveal — a venue with many roles/services wraps ugly otherwise
    DrawTruncatedNameList("Roles", plugin.xivAppRoles.ConvertAll(r => r.Name));
    DrawTruncatedNameList("Services", plugin.availableServices.ConvertAll(s => s.Name));
  }

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

  // Writes terminal "✓ Loaded: …" status regardless of per-fetch errors — Fetch*Async helpers already log+swallow, partial data is still useful
  private async Task LoadVenueDataWithFeedbackAsync(string venueId, string venueName)
  {
    xivAppStatus = $"Loading roles + services + bans for {venueName}…";
    xivAppStatusColor = Colors.XivOverlay0;

    await FetchXivAppRolesAsync(venueId);
    await FetchXivAppServicesAsync(venueId);
    await FetchXivAppBannedPatronsAsync(venueId);
    await FetchXivAppInventoryEnabledAsync(venueId);

    xivAppStatus = $"✓ Loaded: {plugin.xivAppRoles.Count} roles, {plugin.availableServices.Count} services, {plugin.xivAppBannedPatrons.Count} banned";
    xivAppStatusColor = StatusOk;
  }

  // Client is always non-null (created at Plugin load) — callers don't need null checks, just IsConfigured
  private void ReconfigureXivAppClient()
  {
    if (plugin.xivAppClient == null) return;
    if (string.IsNullOrEmpty(this.configuration.xivAppApiKey)) return;
    try
    {
      // Fall back to the public host when blank — users leaving the hint in place expect it to "just work"
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
      // Self-heal: key may have been pasted before ReconfigureXivAppClient fired
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
      xivAppStatusColor = Colors.XivOverlay0;

      plugin.xivAppVenues = await plugin.xivAppClient.Venue.GetVenuesAsync();
      Plugin.Log.Information("Fetched {Count} venues from XIV-App", plugin.xivAppVenues.Count);

      if (plugin.xivAppVenues.Count == 0)
      {
        xivAppStatus = "No venues found for this key — check that the key is scoped to a venue you own or staff.";
        xivAppStatusColor = StatusWarn;
        return;
      }

      xivAppStatus = $"✓ Fetched {plugin.xivAppVenues.Count} venue(s)";
      xivAppStatusColor = StatusOk;

      // Auto-select first venue, or re-hydrate a previously-selected one so "(none fetched yet)" doesn't stick
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

  private async Task FetchXivAppBannedPatronsAsync(string venueId)
  {
    try {
      if (plugin.xivAppClient == null || !plugin.xivAppClient.IsConfigured) return;

      var bannedPatrons = await plugin.xivAppClient.Venue.GetBannedPatronsAsync(venueId);
      plugin.xivAppBannedPatrons = bannedPatrons;
      Plugin.Log.Information("Fetched {Count} banned patron(s) for venue {VenueId}", bannedPatrons.Count, venueId);
    } catch (Exception ex) {
      Plugin.Log.Error("Failed to fetch banned patrons: {0}", ex.Message);
    }
  }

  private async Task FetchXivAppInventoryEnabledAsync(string venueId)
  {
    try {
      if (plugin.xivAppClient == null || !plugin.xivAppClient.IsConfigured) return;

      plugin.xivAppInventoryEnabled = await plugin.xivAppClient.Venue.GetInventoryEnabledAsync(venueId);
      Plugin.Log.Information("Fetched inventory-enabled={0} for venue {1}", plugin.xivAppInventoryEnabled, venueId);
    } catch (Exception ex) {
      Plugin.Log.Error("Failed to fetch inventory settings: {0}", ex.Message);
    }
  }

  private async Task FetchXivAppRolesAsync(string venueId)
  {
    try {
      if (plugin.xivAppClient == null || !plugin.xivAppClient.IsConfigured) return;

      var roles = await plugin.xivAppClient.Venue.GetRolesAsync(venueId);
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

      var response = await plugin.xivAppClient.Venue.GetServicesAsync(venueId);
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
