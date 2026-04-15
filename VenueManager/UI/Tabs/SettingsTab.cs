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

namespace VenueManager.Tabs;

public class SettingsTab
{
  private Plugin plugin;
  private Configuration configuration;

  // Status line shown under Fetch Venues so users see success/failure of
  // button-press actions instead of a silent no-op. Updated by the async
  // Fetch* methods below.
  private string xivAppStatus = "";
  private Vector4 xivAppStatusColor = new Vector4(0.7f, 0.7f, 0.7f, 1f);

  private static readonly Vector4 StatusOk   = new Vector4(0.4f, 0.9f, 0.5f, 1f);
  private static readonly Vector4 StatusWarn = new Vector4(1.0f, 0.8f, 0.3f, 1f);
  private static readonly Vector4 StatusErr  = new Vector4(1.0f, 0.4f, 0.4f, 1f);

  public SettingsTab(Plugin plugin)
  {
    this.plugin = plugin;
    this.configuration = plugin.Configuration;
  }

  // Draw settings menu 
  public unsafe void draw()
  {
    ImGui.BeginChild(1);
    ImGui.Indent(5);

    ImGui.Text("Tab Visibility");
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
      ImGui.TextColored(new Vector4(0.9f, 0, 1f, 1f), "So Empty :(");
    }

    // XIV-App Sync Settings
    DrawXivAppSettings();

    // =============================================================================
    ImGui.Separator();
    ImGui.Spacing();
    ImGui.Text("Patron List");
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

    // =============================================================================
    ImGui.Separator();
    ImGui.Spacing();

    if (!this.configuration.showGuestsTab) ImGui.BeginDisabled();

    ImGui.Text("Patron Chat Alerts");
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

    // Include plugin name in alerts 
    var showPluginNameInChat = this.configuration.showPluginNameInChat;
    if (ImGui.Checkbox("Include Plugin Name", ref showPluginNameInChat))
    {
      this.configuration.showPluginNameInChat = showPluginNameInChat;
      this.configuration.Save();
    }
    if (!this.configuration.showChatAlerts) ImGui.EndDisabled();
    ImGui.Unindent();

    // =============================================================================
    ImGui.Separator();
    ImGui.Spacing();

    ImGui.Text("Patron Sound Alerts");
    // Enable / Disable sound allerts 
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
    if (ImGuiComponents.IconButton(FontAwesomeIcon.Music))
    {
      plugin.playDoorbell();
    }
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Test Sound");
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

    // =============================================================================
    ImGui.Separator();
    ImGui.Spacing();
  
    try {
      var housingManager = HousingManager.Instance();

      var mapData = Plugin.DataManager.GetExcelSheet<Map>().GetRow(AgentMap.Instance()->SelectedMapId);
      string[] parts = mapData.PlaceName.Value.Name.ExtractText().Split(" - ");
      string district = parts.Length == 2 ? parts[1] : "";

    ImGui.Text($@"Debug Info

Territory Id: {plugin.pluginState.territory}
In House: {plugin.pluginState.userInHouse}

HouseID: {housingManager->GetCurrentIndoorHouseId()}
Plot: {housingManager->GetCurrentPlot() + 1}
Ward: {housingManager->GetCurrentWard() + 1}
Room: {housingManager->GetCurrentRoom()}
Division: {housingManager->GetCurrentDivision()}
District: {district}
PlaceName: {mapData.PlaceName.Value.Name.ExtractText()}
");
    } catch {

    }

    ImGui.Unindent();
    ImGui.EndChild();
  }

  // XIV-App API Section
  public unsafe void DrawXivAppSettings()
  {
    ImGui.Separator();
    ImGui.Spacing();
    ImGui.Text("XIV-App Sync");
    
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
    
    // API Key input. We trim on every keystroke to strip the whitespace/
    // newlines that regularly sneak in from Discord copy-paste — those
    // cause HttpClient.DefaultRequestHeaders.Add to throw a FormatException
    // and the key was silently never applied.
    var apiKey = this.configuration.xivAppApiKey ?? "";
    if (ImGui.InputText("API Key", ref apiKey, 128))
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
    // Inline validation feedback so users aren't left guessing why Fetch
    // Venues does nothing when their key is malformed.
    if (!string.IsNullOrEmpty(this.configuration.xivAppApiKey) && !this.configuration.xivAppApiKey.StartsWith("vm_"))
    {
      ImGui.TextColored(StatusErr, "Key must start with 'vm_' — generate one at xivvenuemanager.com/dashboard/api-keys.");
    }

    // Server URL input
    var serverUrl = this.configuration.xivAppServerUrl ?? "";
    if (ImGui.InputText("Server URL", ref serverUrl, 256))
    {
      this.configuration.xivAppServerUrl = serverUrl.Trim();
      this.configuration.Save();
      ReconfigureXivAppClient();
    }
    if (ImGui.IsItemDeactivatedAfterEdit() && !string.IsNullOrEmpty(this.configuration.xivAppApiKey))
    {
      _ = FetchXivAppVenuesAsync();
    }

    // Fetch Venues button
    if (ImGui.Button("Fetch Venues"))
    {
      _ = FetchXivAppVenuesAsync();
    }
    if (!string.IsNullOrEmpty(xivAppStatus))
    {
      ImGui.TextColored(xivAppStatusColor, xivAppStatus);
    }
    
    // Venue dropdown
    if (plugin.xivAppVenues.Count > 0)
    {
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
            _ = FetchXivAppRolesAsync(venue.Id);
            _ = FetchXivAppServicesAsync(venue.Id);
          }
          if (isSelected)
            ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
      }

      // Level-2 visibility: show what we actually fetched for the active
      // venue. Grayed text so it feels like a status line, not a control.
      if (plugin.xivAppRoles.Count > 0)
      {
        var roleNames = string.Join(", ", plugin.xivAppRoles.ConvertAll(r => r.Name));
        ImGui.TextDisabled($"Roles: {roleNames}");
      }
      else
      {
        ImGui.TextDisabled("Roles: (none fetched yet)");
      }
      ImGui.TextDisabled($"Services: {plugin.availableServices.Count}");
    }
    
    ImGui.Unindent(10);
    
    if (!this.configuration.syncToXivApp) ImGui.EndDisabled();
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
      plugin.xivAppClient.Configure(this.configuration.xivAppApiKey, this.configuration.xivAppServerUrl);
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
      xivAppStatusColor = new Vector4(0.7f, 0.7f, 0.7f, 1f);

      plugin.xivAppVenues = await plugin.xivAppClient.GetVenuesAsync();
      Plugin.Log.Information("Fetched {Count} venues from XIV-App", plugin.xivAppVenues.Count);

      if (plugin.xivAppVenues.Count == 0)
      {
        xivAppStatus = "No venues found for this key — check that the key is scoped to a venue you own or staff.";
        xivAppStatusColor = StatusWarn;
      }
      else
      {
        xivAppStatus = $"✓ Fetched {plugin.xivAppVenues.Count} venue(s)";
        xivAppStatusColor = StatusOk;
      }

      // Auto-select first venue if none selected
      if (string.IsNullOrEmpty(this.configuration.selectedVenueId) && plugin.xivAppVenues.Count > 0) {
        this.configuration.selectedVenueId = plugin.xivAppVenues[0].Id;
        this.configuration.Save();
        plugin.currentXivAppVenueId = this.configuration.selectedVenueId;
        await FetchXivAppRolesAsync(this.configuration.selectedVenueId);
        await FetchXivAppServicesAsync(this.configuration.selectedVenueId);
      }
      // If a venue was already selected from a previous session, make sure
      // currentXivAppVenueId + role/service state are populated on first
      // fetch — otherwise we render "(none fetched yet)" forever.
      else if (!string.IsNullOrEmpty(this.configuration.selectedVenueId))
      {
        plugin.currentXivAppVenueId = this.configuration.selectedVenueId;
        await FetchXivAppRolesAsync(this.configuration.selectedVenueId);
        await FetchXivAppServicesAsync(this.configuration.selectedVenueId);
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
