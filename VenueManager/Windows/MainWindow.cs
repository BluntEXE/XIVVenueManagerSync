using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
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

  public override void Draw()
  {
    try
    {
      drawDashboardStrip();
      ImGui.BeginTabBar("Tabs");
      // Render Patrons tab if selected. (Historically named "Guests" —
      // the UI label is "Patrons" to match site vocabulary, but the
      // internal type/field names are still Guest* until the internal
      // naming pass lands with its data-migration shim.)
      if (this.configuration.showGuestsTab)
      {
        if (ImGui.BeginTabItem("Patrons"))
        {
          this.guestsTab.draw();

          ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Logs"))
        {
          this.guestLogTab.draw();

          ImGui.EndTabItem();
        }
      }
      // Sales tab — plugin-first surface for logging a sale at the
      // active XIV-App venue. Always shown; the tab itself gates on
      // API key + selected venue and shows a helpful message when not
      // configured.
      if (ImGui.BeginTabItem("Sales"))
      {
        this.salesTab.draw();
        ImGui.EndTabItem();
      }
      // Render Venues Tab
      if (this.configuration.showVenueTab)
      {
        if (ImGui.BeginTabItem("Venues"))
        {
          venuesTab.draw();
          ImGui.EndTabItem();
        }
      }
      // Render Settings Tab if selected 
      if (ImGui.BeginTabItem("Settings"))
      {
        this.settingsTab.draw();
        ImGui.EndTabItem();
      }
      ImGui.EndTabBar();
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

    // Right-aligned venue name + version. Measure the text and push
    // the cursor to (rightEdge - width) so it hugs the right border
    // regardless of window width.
    string venueLabel = string.IsNullOrEmpty(plugin.pluginState.currentHouse.name)
      ? "(no venue)"
      : plugin.pluginState.currentHouse.name;
    string rightText = $"{venueLabel}   v{plugin.PluginVersion}";
    var rightSize = ImGui.CalcTextSize(rightText);
    float avail = ImGui.GetContentRegionAvail().X;
    ImGui.SameLine();
    // Nudge cursor to the right edge. Clamp to 0 so tiny windows still
    // render the label inline rather than wrapping negatively.
    float offset = avail - rightSize.X - ImGui.GetStyle().ItemSpacing.X;
    if (offset > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
    ImGui.TextColored(StripMuted, rightText);

    ImGui.Separator();
  }
}
