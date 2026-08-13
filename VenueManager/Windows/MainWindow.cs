using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using VenueManager.Tabs;
using VenueManager.UI;

namespace VenueManager.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin plugin;
    private Configuration configuration;

    // salesTab and settingsTab keep dedicated typed fields alongside the
    // list below: salesTab because PrefillSale() needs its Prefill() method
    // (not part of ITab — a single caller doesn't earn an interface member),
    // settingsTab because it's pinned to the bottom of the sidebar and is
    // the first-run override target, both of which are unique to it.
    private SalesTab salesTab;
    private SettingsTab settingsTab;
    private List<ITab> tabs;

    private ITab _currentTab;

    // Sidebar layout constants
    private const float SidebarWidth  = 46f;
    private const float NavButtonSize = 38f;

    public MainWindow(Plugin plugin) : base(
        "XIV Venue Manager###XIVVMMain",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        this.Size          = new Vector2(480, 580);
        this.SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin        = plugin;
        this.configuration = plugin.Configuration;

        this.salesTab      = new SalesTab(plugin);
        this.settingsTab   = new SettingsTab(plugin);

        // Order here is nav-icon draw order — matches today's exact order.
        this.tabs = new List<ITab>
        {
            new GuestsTab(plugin),
            salesTab,
            new GuestLogTab(plugin),
            new ShiftsTab(plugin),
            new RoomsTab(plugin),
            new InventoryTab(plugin),
            new VenuesTab(plugin),
            settingsTab,
        };

        _currentTab = salesTab;
    }

    public void Dispose() { }

    // Called by slash commands to jump to a named tab.
    public void OpenTab(string name)
    {
        var match = tabs.FirstOrDefault(t => t.Name == name);
        if (match != null) _currentTab = match;
    }

    // Forward a prefill request to the Sales tab.
    public void PrefillSale(int? amount, string? customer, bool tip = false)
        => salesTab.Prefill(amount, customer, tip);

    public override void Draw()
    {
        using var theme = ThemeManager.Scope();
        try
        {
            // First-run: no API key → show Settings.
            if (string.IsNullOrEmpty(configuration.xivAppApiKey))
                _currentTab = settingsTab;

            drawHeader();
            drawSidebarAndContent();
        }
        catch (Exception e)
        {
            Plugin.Log.Error("Crash while drawing main window");
            Plugin.Log.Error(e.ToString());
        }
    }

    // ── Header ─────────────────────────────────────────────────────────────
    // Shows sync status, venue name, session totals, and version.
    private void drawHeader()
    {
        bool syncOn = plugin.xivAppClient != null
                   && plugin.xivAppClient.IsConfigured
                   && configuration.syncToXivApp;

        // Sync dot
        ImGui.TextColored(syncOn ? Colors.XivGreen : Colors.XivOverlay0, syncOn ? "●" : "○");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(syncOn ? "XIV-App sync active" : "XIV-App sync off or unconfigured");

        // Venue name
        ImGui.SameLine();
        string venueLabel = string.IsNullOrEmpty(plugin.pluginState.currentHouse.name)
            ? "(no venue)"
            : plugin.pluginState.currentHouse.name;
        ImGui.TextColored(Colors.XivBlue, venueLabel);

        // Right-aligned: [Web] session sales · version
        var venueUrl    = plugin.BuildVenueUrl();
        string sessText = $"{plugin.SessionSalesTotal:N0}g";
        string verText  = $"v{plugin.PluginVersion}";
        var style       = ImGui.GetStyle();

        float webW   = venueUrl != null ? ImGui.CalcTextSize("Web").X + style.FramePadding.X * 2 + style.ItemSpacing.X : 0f;
        float rightW = webW + ImGui.CalcTextSize(sessText).X + style.ItemSpacing.X + ImGui.CalcTextSize($"  {verText}").X;
        float targetX = ImGui.GetWindowWidth() - style.WindowPadding.X - rightW - style.ItemSpacing.X;
        if (targetX > ImGui.GetCursorPosX() + style.ItemSpacing.X) { ImGui.SameLine(); ImGui.SetCursorPosX(targetX); }

        if (venueUrl != null)
        {
            if (ImGui.SmallButton("Web")) Util.OpenLink(venueUrl);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Open {venueLabel} on xivvenuemanager.com");
            ImGui.SameLine();
        }

        ImGui.TextColored(Colors.XivGold,    sessText);
        ImGui.SameLine();
        ImGui.TextColored(Colors.XivOverlay0, $"  {verText}");

        // Patron count row
        ImGui.TextColored(Colors.XivSubtext0, $"{plugin.pluginState.playersInHouse} patrons live");

        ImGui.Separator();
    }

    // ── Sidebar + content ───────────────────────────────────────────────────
    private void drawSidebarAndContent()
    {
        float contentH = ImGui.GetContentRegionAvail().Y;

        // Sidebar — darker background, no inner border
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Colors.XivCrust);
        ImGui.PushStyleColor(ImGuiCol.Border,  Colors.XivSurface0);
        ImGui.BeginChild("##nav", new Vector2(SidebarWidth, contentH), true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor(2);

        drawNavIcons();

        ImGui.EndChild();

        ImGui.SameLine(0, 4f);

        // Content area — extra window padding gives text breathing room
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        ImGui.BeginChild("##content", new Vector2(-1, contentH), false);
        ImGui.PopStyleVar();
        drawTabContent();
        ImGui.EndChild();
    }

    private void drawNavIcons()
    {
        ImGui.Spacing();

        foreach (var tab in tabs)
        {
            if (tab == settingsTab) continue; // pinned to bottom, drawn separately below
            if (tab.IsVisible) navButton(tab);
        }

        // Settings pinned to bottom
        float iconH   = NavButtonSize + ImGui.GetStyle().ItemSpacing.Y;
        float spaceH  = ImGui.GetContentRegionAvail().Y - iconH;
        if (spaceH > 0) ImGui.Dummy(new Vector2(1f, spaceH));

        navButton(settingsTab);
    }

    private void navButton(ITab tab)
    {
        bool active = _currentTab == tab;

        // Transparent button bg; only icon color changes
        ImGui.PushStyleColor(ImGuiCol.Button,        Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Colors.XivSurface0);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  Colors.XivSurface1);
        ImGui.PushStyleColor(ImGuiCol.Text, active ? Colors.XivBlue : Colors.XivOverlay0);

        ImGui.PushFont(UiBuilder.IconFont);
        bool clicked = ImGui.Button(
            $"{tab.Icon.ToIconString()}##nav{tab.Name}",
            new Vector2(SidebarWidth - 8f, NavButtonSize));
        ImGui.PopFont();

        ImGui.PopStyleColor(4);

        if (clicked) _currentTab = tab;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tab.Tooltip);
    }

    // ── Tab content ─────────────────────────────────────────────────────────
    private void drawTabContent()
    {
        // Guard: if selected tab is hidden (e.g. its owning config toggle
        // was flipped off from Settings this frame), fall back to Sales.
        // Settings itself is exempt — it's always visible and is the
        // first-run override target.
        if (_currentTab != settingsTab && !_currentTab.IsVisible)
            _currentTab = salesTab;

        _currentTab.draw();
    }
}
