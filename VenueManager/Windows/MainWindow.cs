using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using VenueManager.Tabs;

namespace VenueManager.Windows;

public class MainWindow : Window, IDisposable
{
  private Plugin plugin;
  private Configuration configuration;

  private VenuesTab venuesTab;
  private SettingsTab settingsTab;
  private GuestsTab guestsTab;
  private GuestLogTab guestLogTab;
  private SalesTab salesTab;

  // One-shot tab focus request. Set by OpenTab() (e.g. from slash
  // commands), consumed on the next Draw() frame by the tab whose label
  // matches. Cleared after the tab bar cycle so normal user clicks are
  // not stomped on subsequent frames.
  private string? pendingTab = null;

  // Strip palette. Mauve / emerald / peach chosen to loosely track the
  // Catppuccin Mocha vocab the website uses so plugin + site feel like
  // the same product.
  private static readonly Vector4 StripSyncOn  = new(0.54f, 0.80f, 0.52f, 1f); // emerald
  private static readonly Vector4 StripSyncOff = new(0.55f, 0.55f, 0.60f, 1f); // grey
  private static readonly Vector4 StripMuted   = new(0.70f, 0.70f, 0.75f, 1f);
  private static readonly Vector4 StripAccent  = new(0.95f, 0.78f, 0.40f, 1f); // peach/gold for gil

  public MainWindow(Plugin plugin) : base(
      "XIV Venue Manager Sync", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
  {
    this.SizeConstraints = new WindowSizeConstraints
    {
      MinimumSize = new Vector2(250, 300),
      MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
    };

    this.plugin = plugin;
    this.configuration = plugin.Configuration;
    this.venuesTab = new VenuesTab(plugin);
    this.settingsTab = new SettingsTab(plugin);
    this.guestsTab = new GuestsTab(plugin);
    this.guestLogTab = new GuestLogTab(plugin);
    this.salesTab = new SalesTab(plugin);
  }

  public void Dispose()
  {
  }

  // Request that a named tab be focused on the next Draw frame. Called
  // from slash command handlers and any future "jump to tab" plumbing.
  // Safe to call when the window is closed — Draw() runs only when the
  // window is open, so the flag sits until then.
  public void OpenTab(string name)
  {
    this.pendingTab = name;
  }

  // Forward a prefill request to the Sales tab. Lives here so callers
  // (Plugin.OnCommand) don't need direct handles to every tab instance.
  public void PrefillSale(int? amount, string? customer)
  {
    this.salesTab.Prefill(amount, customer);
  }

  // Helper: return the flag a tab should pass to BeginTabItem this
  // frame to win a pending focus race. Only the matching tab gets
  // SetSelected; everyone else gets None. The caller (Draw) clears
  // pendingTab after EndTabBar to make this a one-shot.
  private ImGuiTabItemFlags flagFor(string name)
  {
    return (pendingTab == name) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
  }

  public override void Draw()
  {
    try
    {
      drawDashboardStrip();
      ImGui.BeginTabBar("Tabs");
      // Tab order (left to right): Patrons │ Sales │ History │ Venues │ Settings
      // The hot path is "who's here now" → "log a sale", so those two
      // ride leftmost. History (formerly "Logs") is the past-visit
      // lookup surface and will be retired once Arc 3 ships the web
      // timeline; keeping it parked here until then.
      //
      // Historical note: internal identifiers are still Guest* (class
      // names, disk file format, config key `showGuestsTab`) until the
      // internal naming pass lands with its data-migration shim.
      if (this.configuration.showGuestsTab)
      {
        if (ImGui.BeginTabItem("Patrons", flagFor("Patrons")))
        {
          this.guestsTab.draw();
          ImGui.EndTabItem();
        }
      }
      // Sales tab — plugin-first surface for logging a sale at the
      // active XIV-App venue. Always shown; the tab itself gates on
      // API key + selected venue and shows a helpful message when not
      // configured.
      if (ImGui.BeginTabItem("Sales", flagFor("Sales")))
      {
        this.salesTab.draw();
        ImGui.EndTabItem();
      }
      // History — per-venue past visit lookup. Shares the same config
      // toggle as Patrons since they're the same concept at different
      // time scopes (now vs. past).
      if (this.configuration.showGuestsTab)
      {
        if (ImGui.BeginTabItem("History", flagFor("History")))
        {
          this.guestLogTab.draw();
          ImGui.EndTabItem();
        }
      }
      // Render Venues Tab
      if (this.configuration.showVenueTab)
      {
        if (ImGui.BeginTabItem("Venues", flagFor("Venues")))
        {
          venuesTab.draw();
          ImGui.EndTabItem();
        }
      }
      // Render Settings Tab if selected
      if (ImGui.BeginTabItem("Settings", flagFor("Settings")))
      {
        this.settingsTab.draw();
        ImGui.EndTabItem();
      }
      ImGui.EndTabBar();
      // One-shot: whichever tab consumed pendingTab this frame is done.
      this.pendingTab = null;
    }
    catch (Exception e)
    {
      Plugin.Log.Error("Crash while drawing main window");
      Plugin.Log.Error(e.ToString());
    }
  }

  // Compact always-visible readout rendered above the tab bar. Shows
  // at-a-glance state the user would otherwise have to tab-hop for:
  // is sync alive, how busy is the venue, how much gil this session,
  // which venue we're pointed at, what version we're on.
  private void drawDashboardStrip()
  {
    // Sync dot — binary green/grey for now. A three-state healthy /
    // degraded / offline readout is on the Arc 1 caveat list
    // (xivAppClient.IsDegraded) and will ride in once we add a
    // last-success timestamp on the client.
    bool syncOn = plugin.xivAppClient != null
                  && plugin.xivAppClient.IsConfigured
                  && configuration.syncToXivApp;
    var dotColor = syncOn ? StripSyncOn : StripSyncOff;
    ImGui.TextColored(dotColor, syncOn ? "●" : "○");
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip(syncOn
        ? "XIV-App sync is configured and enabled"
        : "XIV-App sync is off or unconfigured");
    }

    // Live patron count
    ImGui.SameLine();
    ImGui.TextColored(StripMuted, $"{plugin.pluginState.playersInHouse} live");

    // Session sales tally (gold accent because gil)
    ImGui.SameLine();
    ImGui.TextColored(StripMuted, "│");
    ImGui.SameLine();
    ImGui.TextColored(StripAccent,
      $"{plugin.SessionSalesTotal:N0}g session");
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip(plugin.SessionSalesCount == 1
        ? "1 sale logged this session"
        : $"{plugin.SessionSalesCount} sales logged this session");
    }

    // Right-aligned: [Web] venue-name  vN.N.N
    // Measure all elements so we can push the cursor right.
    string venueLabel = string.IsNullOrEmpty(plugin.pluginState.currentHouse.name)
      ? "(no venue)"
      : plugin.pluginState.currentHouse.name;
    string versionText = $"v{plugin.PluginVersion}";
    var venueUrl = plugin.BuildVenueUrl();
    bool hasUrl = venueUrl != null;

    // Measure widths: button + spacing + venue label + spacing + version
    var style = ImGui.GetStyle();
    float btnWidth = hasUrl
      ? ImGui.CalcTextSize("Web").X + style.FramePadding.X * 2 + style.ItemSpacing.X
      : 0;
    float textWidth = ImGui.CalcTextSize($"{venueLabel}   {versionText}").X;
    float totalRight = btnWidth + textWidth;

    float avail = ImGui.GetContentRegionAvail().X;
    ImGui.SameLine();
    float offset = avail - totalRight - style.ItemSpacing.X;
    if (offset > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

    if (hasUrl)
    {
      if (ImGui.SmallButton("Web"))
      {
        Util.OpenLink(venueUrl!);
      }
      if (ImGui.IsItemHovered())
      {
        ImGui.SetTooltip($"Open {venueLabel} on xivvenuemanager.com");
      }
      ImGui.SameLine();
    }
    ImGui.TextColored(StripMuted, $"{venueLabel}   {versionText}");

    ImGui.Separator();
  }
}
