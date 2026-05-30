using System;
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

    private VenuesTab venuesTab;
    private SettingsTab settingsTab;
    private GuestsTab guestsTab;
    private GuestLogTab guestLogTab;
    private SalesTab salesTab;
    private ShiftsTab shiftsTab;

    private enum Tab { Patrons, Sales, History, Shift, Venues, Settings }
    private Tab _currentTab = Tab.Sales;

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
        this.venuesTab     = new VenuesTab(plugin);
        this.settingsTab   = new SettingsTab(plugin);
        this.guestsTab     = new GuestsTab(plugin);
        this.guestLogTab   = new GuestLogTab(plugin);
        this.salesTab      = new SalesTab(plugin);
        this.shiftsTab     = new ShiftsTab(plugin);
    }

    public void Dispose() { }

    // Called by slash commands to jump to a named tab.
    public void OpenTab(string name)
    {
        _currentTab = name switch
        {
            "Patrons"  => Tab.Patrons,
            "Sales"    => Tab.Sales,
            "History"  => Tab.History,
            "My Shift" => Tab.Shift,
            "Venues"   => Tab.Venues,
            "Settings" => Tab.Settings,
            _          => _currentTab,
        };
    }

    // Forward a prefill request to the Sales tab.
    public void PrefillSale(int? amount, string? customer)
        => salesTab.Prefill(amount, customer);

    public override void Draw()
    {
        using var theme = ThemeManager.Scope();
        try
        {
            // First-run: no API key → show Settings.
            if (string.IsNullOrEmpty(configuration.xivAppApiKey))
                _currentTab = Tab.Settings;

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

        if (configuration.showGuestsTab)
            navButton(Tab.Patrons,  FontAwesomeIcon.UserFriends, "Patrons");

        navButton(Tab.Sales,   FontAwesomeIcon.DollarSign,      "Sales");

        if (configuration.showGuestsTab)
            navButton(Tab.History, FontAwesomeIcon.History, "History");

        navButton(Tab.Shift,   FontAwesomeIcon.CalendarCheck,   "My Shift");

        if (configuration.showVenueTab)
            navButton(Tab.Venues, FontAwesomeIcon.Building, "Venues");

        // Settings pinned to bottom
        float iconH   = NavButtonSize + ImGui.GetStyle().ItemSpacing.Y;
        float spaceH  = ImGui.GetContentRegionAvail().Y - iconH;
        if (spaceH > 0) ImGui.Dummy(new Vector2(1f, spaceH));

        navButton(Tab.Settings, FontAwesomeIcon.Cog, "Settings");
    }

    private void navButton(Tab tab, FontAwesomeIcon icon, string tooltip)
    {
        bool active = _currentTab == tab;

        // Transparent button bg; only icon color changes
        ImGui.PushStyleColor(ImGuiCol.Button,        Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Colors.XivSurface0);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  Colors.XivSurface1);
        ImGui.PushStyleColor(ImGuiCol.Text, active ? Colors.XivBlue : Colors.XivOverlay0);

        ImGui.PushFont(UiBuilder.IconFont);
        bool clicked = ImGui.Button(
            $"{icon.ToIconString()}##nav{tab}",
            new Vector2(SidebarWidth - 8f, NavButtonSize));
        ImGui.PopFont();

        ImGui.PopStyleColor(4);

        if (clicked) _currentTab = tab;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    // ── Tab content ─────────────────────────────────────────────────────────
    private void drawTabContent()
    {
        // Guard: if selected tab is hidden, fall back to Sales.
        if (_currentTab == Tab.Patrons  && !configuration.showGuestsTab) _currentTab = Tab.Sales;
        if (_currentTab == Tab.History  && !configuration.showGuestsTab) _currentTab = Tab.Sales;
        if (_currentTab == Tab.Venues   && !configuration.showVenueTab)  _currentTab = Tab.Sales;

        switch (_currentTab)
        {
            case Tab.Patrons:  guestsTab.draw();    break;
            case Tab.Sales:    salesTab.draw();     break;
            case Tab.History:  guestLogTab.draw();  break;
            case Tab.Shift:    shiftsTab.draw();    break;
            case Tab.Venues:   venuesTab.draw();    break;
            case Tab.Settings: settingsTab.draw();  break;
        }
    }
}
