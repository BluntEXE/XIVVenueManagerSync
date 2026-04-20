using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui.Dtr;
using VenueManager.Windows;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Text;
using System.Diagnostics;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;
using System;
using System.Globalization;
using System.Threading.Tasks;
using VenueManager.UI;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Bindings.ImGui;
using Map = Lumina.Excel.Sheets.Map;

namespace VenueManager
{
  public sealed class Plugin : IDalamudPlugin
  {
    public string Name => "XIV Venue Manager Sync";
    private const string CommandName = "/venue";
    private const string CommandNameAlias = "/vm";
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    // Game Objects 
    [PluginService] public static IObjectTable Objects { get; private set; } = null!;
    [PluginService] public static IPlayerState PlayerState  { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IDtrBar DtrBar { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public PluginState pluginState { get; init; }
    public VenueList venueList { get; init; }
    public Dictionary<long, GuestList> guestLists = new();

    // Windows
    public WindowSystem WindowSystem = new("VenueManager");
    private MainWindow MainWindow { get; init; }
    private NotesWindow NotesWindow { get; init; }

    private Stopwatch stopwatch = new();
    private DoorbellSound doorbell;

    // Server Info Bar entry. Created once in ctor, text refreshed from the
    // framework-update tick (throttled to ~2s — DTR is at-a-glance, no need
    // to allocate a new SeString every frame). Disposed on unload via Remove().
    private IDtrBarEntry? dtrEntry;
    private long dtrLastUpdateMs = 0;

    // XIV-App API Client. Public so UI tabs (SettingsTab, SalesTab, ...)
    // can access it directly — the plugin has no cross-assembly boundary
    // concerns, and every tab that needs to hit the server needs the
    // same client instance.
    public XIVAppApiClient? xivAppClient;
    public List<XIVAppVenue> xivAppVenues = new();
    public List<Role> xivAppRoles = new();
    public List<Service> availableServices = new();
    public string? currentXivAppVenueId;

    // Event-presence cache used to gate patron-visit sync when the user
    // has opted into "sync only during events". 60s TTL per venue — see
    // EventPresenceCache for the reasoning.
    public EventPresenceCache eventPresence = new();

    // Session-scoped sales counters — reset on plugin reload. Drive the
    // dashboard strip's session tally. Not persisted via Configuration
    // because "session" = plugin lifetime, not calendar day. Incremented
    // in SalesTab.LogSaleAsync success branch and by any future slash
    // subcommand paths (e.g. /vm sale!).
    public int SessionSalesTotal = 0;
    public int SessionSalesCount = 0;

    // Build a deep-link URL into the XIV-App website for the currently
    // selected venue. Returns null if no venue is selected or the slug
    // is missing (pre-Foundation venue data).
    public string? BuildVenueUrl(string? subpath = null)
    {
      if (string.IsNullOrEmpty(currentXivAppVenueId)) return null;
      var venue = xivAppVenues.Find(v => v.Id == currentXivAppVenueId);
      if (venue == null || string.IsNullOrEmpty(venue.Slug)) return null;
      var baseUrl = Configuration.xivAppServerUrl.TrimEnd('/');
      var path = $"/dashboard/{venue.Slug}";
      if (!string.IsNullOrEmpty(subpath))
        path += "/" + subpath.TrimStart('/');
      return baseUrl + path;
    }

    // Cached version string pulled from the loaded assembly. Plugin.cs,
    // XIVVenueManagerSync.json and repo.json are kept in lockstep by the
    // build + ship ritual, so reading from the running assembly means the
    // dashboard strip auto-follows whatever version the user installed.
    public string PluginVersion { get; } =
      typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "?";

    // True for the first loop that a player enters a house 
    private bool justEnteredHouse = false;

    private bool running = false;

    public Plugin()
    {
      this.pluginState = new PluginState();
      this.venueList = new VenueList();
      this.venueList.load();

      // Default guest list
      this.guestLists.Add(0, new GuestList());
      this.guestLists[0].load();

      this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
      this.Configuration.Initialize(PluginInterface);

      // XIV-App API Client is always instantiated so the Settings tab can
      // lazy-configure it the moment the user pastes a key. Previously this
      // was gated on the key already being present, which made first-time
      // setup require a game restart before Fetch Venues would work.
      xivAppClient = new XIVAppApiClient();
      if (!string.IsNullOrEmpty(Configuration.xivAppApiKey))
      {
          xivAppClient.Configure(Configuration.xivAppApiKey, Configuration.xivAppServerUrl);
          Log.Information("XIV-App API Client configured with server: {0}", Configuration.xivAppServerUrl);
      }

      MainWindow = new MainWindow(this);
      NotesWindow = new NotesWindow(this);

      WindowSystem.AddWindow(MainWindow);
      WindowSystem.AddWindow(NotesWindow);

      CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Open venue manager interface to see patrons list and manage venues" });
      CommandManager.AddHandler(CommandNameAlias, new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Alias for /venue" });
      var SnoozeHandler = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Pause alerts until leaving the house." };
      var SnoozeHandlerAlias = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Alias for /venue snooze" };
      CommandManager.AddHandler(CommandName + " snooze", SnoozeHandler);
      CommandManager.AddHandler(CommandNameAlias + " snooze", SnoozeHandlerAlias);
      // Sale subcommand sugar. Dalamud dispatches on the parent command
      // (OnCommand receives args="sale 500 Ehno") — these AddHandler
      // calls exist purely to surface each subcommand in /xlhelp. The
      // actual routing lives in OnCommand's args parser.
      var SaleHelp    = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Open Sales tab. Usage: /vm sale [amount] [customer]" };
      var SaleBangHelp = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Log a sale without opening UI. Usage: /vm sale! <amount> [customer]" };
      var TargetHelp  = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Open Sales tab prefilled with current target as customer. Usage: /vm target [amount]" };
      var TargetBangHelp = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Log a sale for your current target without opening UI. Usage: /vm target! <amount>" };
      var StartHelp      = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Clock into your current shift. Usage: /vm start" };
      var EndHelp        = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Clock out of your active shift. Usage: /vm end" };
      CommandManager.AddHandler(CommandNameAlias + " sale",    SaleHelp);
      CommandManager.AddHandler(CommandNameAlias + " sale!",   SaleBangHelp);
      CommandManager.AddHandler(CommandNameAlias + " target",  TargetHelp);
      CommandManager.AddHandler(CommandNameAlias + " target!", TargetBangHelp);
      CommandManager.AddHandler(CommandNameAlias + " start",   StartHelp);
      CommandManager.AddHandler(CommandNameAlias + " end",     EndHelp);

      // DTR bar entry. Always created — visibility is driven by the
      // display-mode config, not by Get/Remove churn. Clicking opens the
      // main window, matching what users expect from a plugin tray.
      try
      {
        dtrEntry = DtrBar.Get("XIV Venue Manager");
        dtrEntry.OnClick = _ => MainWindow.Toggle();
        dtrEntry.Tooltip = "XIV Venue Manager — click to open";
        dtrEntry.Shown = Configuration.dtrDisplayMode != DtrDisplayMode.Disabled;
        UpdateDtrBar(force: true);
      }
      catch (Exception ex)
      {
        Log.Warning($"Failed to register DTR entry: {ex.Message}");
      }

      PluginInterface.UiBuilder.Draw += DrawUI;

      // Bind territory changed listener to client 
      ClientState.TerritoryChanged += OnTerritoryChanged;
      Framework.Update += OnFrameworkUpdate;
      ClientState.Logout += OnLogout;

      // Load Sound 
      doorbell = new DoorbellSound(this, Configuration.doorbellType);
      doorbell.load();

      // Run territory change one time on boot to register current location 
      OnTerritoryChanged(ClientState.TerritoryType);

      // This adds a button to the plugin installer entry of this plugin which allows
      // to toggle the display status of the configuration ui
      PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

      // Adds another button that is doing the same but for the main ui of the plugin
      PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
    }

    public void Dispose()
    {
      // Remove framework listener on close 
      Framework.Update -= OnFrameworkUpdate;
      // Remove territory change listener 
      ClientState.TerritoryChanged -= OnTerritoryChanged;

      // Dispose our sound file
      doorbell.disposeFile();

      // Remove DTR entry so the strip doesn't keep a stale slot after unload.
      try { dtrEntry?.Remove(); } catch { /* Dalamud already tore it down */ }
      dtrEntry = null;

      this.WindowSystem.RemoveAllWindows();

      MainWindow.Dispose();
      NotesWindow.Dispose();

      CommandManager.RemoveHandler(CommandName);
      CommandManager.RemoveHandler(CommandNameAlias);
      CommandManager.RemoveHandler(CommandName + " snooze");
      CommandManager.RemoveHandler(CommandNameAlias + " snooze");
      CommandManager.RemoveHandler(CommandNameAlias + " sale");
      CommandManager.RemoveHandler(CommandNameAlias + " sale!");
      CommandManager.RemoveHandler(CommandNameAlias + " target");
      CommandManager.RemoveHandler(CommandNameAlias + " target!");
      CommandManager.RemoveHandler(CommandNameAlias + " start");
      CommandManager.RemoveHandler(CommandNameAlias + " end");
    }

    private void OnSnooze()
    {
      if (pluginState.snoozed)
      {
        pluginState.snoozed = false;
        Chat.Print((this.Configuration.showPluginNameInChat ? $"[{Name}] " : "") + "Alerts unpaused");
      }
      else if (!pluginState.userInHouse)
      {
        Chat.Print((this.Configuration.showPluginNameInChat ? $"[{Name}] " : "") + "You must be in a house to pause alerts");
      }
      else
      {
        pluginState.snoozed = true;
        Chat.Print((this.Configuration.showPluginNameInChat ? $"[{Name}] " : "") + "Alerts paused until leaving the current house");
      }
    }

    private void OnCommand(string command, string args)
    {
      if (args == "snooze")
      {
        OnSnooze();
        return;
      }

      // Sale subcommand family. Split on whitespace, first token is the
      // verb, second token is the amount (integer), the rest is a free
      // text customer name (may contain spaces).
      //
      // /vm sale                    → open Sales tab, no prefill
      // /vm sale 500                → open Sales tab, amount=500
      // /vm sale 500 Ehno Smith     → open Sales tab, amount=500, customer="Ehno Smith"
      // /vm sale! 500 Ehno          → log immediately, no UI shown, chat toast on result
      // /vm target                  → open Sales tab with current target prefilled
      // /vm target 500              → open Sales tab with current target + amount
      if (args.StartsWith("sale") || args.StartsWith("target"))
      {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0] : "";

        int? parsedAmount = null;
        if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) && a > 0)
        {
          parsedAmount = a;
        }

        string? customerFromArgs = null;
        if (parts.Length >= 3)
        {
          customerFromArgs = string.Join(' ', parts, 2, parts.Length - 2);
        }

        if (verb == "target!")
        {
          string prefix = this.Configuration.showPluginNameInChat ? $"[{Name}] " : "";
          if (parsedAmount == null)
          {
            Chat.Print(prefix + "Usage: /vm target! <amount>");
            return;
          }
          var t = TargetManager.Target;
          var targetName = t?.Name.TextValue;
          if (string.IsNullOrEmpty(targetName))
          {
            Chat.Print(prefix + "No target selected.");
            return;
          }
          _ = LogSaleSilentAsync(parsedAmount.Value, targetName);
          return;
        }

        if (verb == "target")
        {
          // For /vm target the "customer" override is the game target,
          // not an args field. If the player has no target we fall
          // through with null and let the Sales tab's own Use Target
          // flow handle it next frame.
          var t = TargetManager.Target;
          var targetName = t?.Name.TextValue;
          MainWindow.OpenTab("Sales");
          MainWindow.PrefillSale(parsedAmount, targetName);
          MainWindow.IsOpen = true;
          return;
        }

        if (verb == "sale!")
        {
          if (parsedAmount == null)
          {
            Chat.Print((this.Configuration.showPluginNameInChat ? $"[{Name}] " : "") + "Usage: /vm sale! <amount> [customer]");
            return;
          }
          _ = LogSaleSilentAsync(parsedAmount.Value, customerFromArgs);
          return;
        }

        // Plain "sale" — open Sales tab with whatever prefill is available.
        MainWindow.OpenTab("Sales");
        MainWindow.PrefillSale(parsedAmount, customerFromArgs);
        MainWindow.IsOpen = true;
        return;
      }

      if (args == "start")
      {
        _ = ShiftClockInSilentAsync();
        return;
      }

      if (args == "end")
      {
        _ = ShiftClockOutSilentAsync();
        return;
      }

      // in response to the slash command, just display our main ui
      MainWindow.IsOpen = true;
    }

    // Fire-and-forget silent sale log used by `/vm sale!`. Bypasses the
    // Sales tab form state entirely — writes straight through to the
    // XIV-App API and posts a chat toast on result. Success increments
    // the dashboard session tally so the strip readout stays consistent
    // regardless of which code path logged the sale.
    public async Task LogSaleSilentAsync(int amount, string? customer)
    {
      string prefix = this.Configuration.showPluginNameInChat ? $"[{Name}] " : "";

      if (xivAppClient == null || !xivAppClient.IsConfigured)
      {
        Chat.Print(prefix + "XIV-App is not configured. Add your API key in Settings first.");
        return;
      }
      if (string.IsNullOrEmpty(currentXivAppVenueId))
      {
        Chat.Print(prefix + "No venue selected. Pick one in Settings.");
        return;
      }

      try
      {
        string? trimmedName = string.IsNullOrWhiteSpace(customer) ? null : customer!.Trim();
        var result = await xivAppClient.LogTransactionAsync(
          currentXivAppVenueId,
          null,          // no service id from slash path
          (decimal)amount,
          trimmedName,
          null           // no notes from slash path
        );

        if (result.Success)
        {
          SessionSalesTotal += amount;
          SessionSalesCount++;
          Chat.Print(prefix + (trimmedName != null
            ? $"Logged {amount}g from {trimmedName}"
            : $"Logged {amount}g"));
        }
        else
        {
          Chat.Print(prefix + $"Sale failed: {result.Error ?? "unknown error"}");
        }
      }
      catch (Exception ex)
      {
        Log.Error($"LogSaleSilentAsync exception: {ex}");
        Chat.Print(prefix + $"Sale error: {ex.Message}");
      }
    }

    // Fire-and-forget clock-in used by `/vm start`. Finds the first
    // SCHEDULED shift within its clock-in window and clocks in.
    public async Task ShiftClockInSilentAsync()
    {
      string prefix = this.Configuration.showPluginNameInChat ? $"[{Name}] " : "";

      if (xivAppClient == null || !xivAppClient.IsConfigured)
      {
        Chat.Print(prefix + "XIV-App is not configured. Add your API key in Settings first.");
        return;
      }
      if (string.IsNullOrEmpty(currentXivAppVenueId))
      {
        Chat.Print(prefix + "No venue selected. Pick one in Settings.");
        return;
      }

      try
      {
        var shifts = await xivAppClient.GetMyShiftsAsync(currentXivAppVenueId);
        var scheduled = shifts.Find(s => s.Status == "SCHEDULED");

        if (scheduled == null)
        {
          Chat.Print(prefix + "No scheduled shift found to clock into.");
          return;
        }

        var result = await xivAppClient.ClockInAsync(scheduled.Id);
        if (result.Success)
          Chat.Print(prefix + "Clocked in. Shift is now active.");
        else
          Chat.Print(prefix + $"Clock-in failed: {result.Error ?? "unknown error"}");
      }
      catch (Exception ex)
      {
        Log.Error($"ShiftClockInSilentAsync exception: {ex}");
        Chat.Print(prefix + $"Clock-in error: {ex.Message}");
      }
    }

    // Fire-and-forget clock-out used by `/vm end`. Finds the first
    // ACTIVE shift and clocks out, reporting hours worked.
    public async Task ShiftClockOutSilentAsync()
    {
      string prefix = this.Configuration.showPluginNameInChat ? $"[{Name}] " : "";

      if (xivAppClient == null || !xivAppClient.IsConfigured)
      {
        Chat.Print(prefix + "XIV-App is not configured. Add your API key in Settings first.");
        return;
      }
      if (string.IsNullOrEmpty(currentXivAppVenueId))
      {
        Chat.Print(prefix + "No venue selected. Pick one in Settings.");
        return;
      }

      try
      {
        var shifts = await xivAppClient.GetMyShiftsAsync(currentXivAppVenueId);
        var active = shifts.Find(s => s.Status == "ACTIVE");

        if (active == null)
        {
          Chat.Print(prefix + "No active shift found to clock out of.");
          return;
        }

        var result = await xivAppClient.ClockOutAsync(active.Id);
        if (result.Success)
        {
          var hoursMsg = result.HoursWorked.HasValue
            ? $" ({result.HoursWorked.Value:F1}h worked)"
            : "";
          Chat.Print(prefix + $"Clocked out.{hoursMsg}");
        }
        else
          Chat.Print(prefix + $"Clock-out failed: {result.Error ?? "unknown error"}");
      }
      catch (Exception ex)
      {
        Log.Error($"ShiftClockOutSilentAsync exception: {ex}");
        Chat.Print(prefix + $"Clock-out error: {ex.Message}");
      }
    }

    private void DrawUI()
    {
      this.WindowSystem.Draw();
    }

    public void ShowNotesWindow(Venue venue)
    {
      NotesWindow.venue = venue;
      NotesWindow.IsOpen = true;
    }

    private void OnLogout(int type, int code)
    {
      // Erase territory state 
      pluginState.territory = 0;

      leftHouse();
    }

    public void ToggleConfigUI() => MainWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();

    private unsafe void OnTerritoryChanged(ushort territory)
    {
      // Save current user territory
      pluginState.territory = territory;

      bool inHouse = false;
      try
      {
        var housingManager = HousingManager.Instance();
        inHouse = housingManager->IsInside();
      }
      catch (Exception ex) {
        Log.Warning("Could not get housing state on territory change. " + ex.Message);
      }

      // Player has entered a house 
      if (inHouse)
      {
        justEnteredHouse = true;
        pluginState.userInHouse = true;
        startTimers();
      }
      // Player has left a house 
      else if (pluginState.userInHouse)
      {
        leftHouse();
      }

      this.Configuration.Save();
    }

    public void startTimers()
    {
      stopwatch.Start();
    }

    public void stopTimers()
    {
      stopwatch.Stop();
    }

    private void leftHouse()
    {
      pluginState.userInHouse = false;
      pluginState.currentHouse = new Venue(); // Erase venue when leaving
      stopwatch.Stop();
      // Unsnooze if leaving a house when snoozed
      if (pluginState.snoozed) OnSnooze();
      // Refresh DTR immediately so "Outside" replaces the venue name without
      // waiting for the 2s throttle.
      UpdateDtrBar(force: true);
    }

    // Refreshes the Server Info Bar entry text. Called every framework tick,
    // but the body throttles to ~2s so we don't re-allocate an SeString 60×/s
    // for a strip the player only glances at. Call with force=true to push
    // an immediate update on state transitions (mode change, entering/leaving
    // a house) so the UI feels responsive instead of lagged.
    public void UpdateDtrBar(bool force = false)
    {
      if (dtrEntry == null) return;
      var mode = Configuration.dtrDisplayMode;
      dtrEntry.Shown = mode != DtrDisplayMode.Disabled;
      if (!dtrEntry.Shown) return;

      var nowMs = Environment.TickCount64;
      if (!force && nowMs - dtrLastUpdateMs < 2000) return;
      dtrLastUpdateMs = nowMs;

      string text;
      switch (mode)
      {
        case DtrDisplayMode.PatronCount:
          text = pluginState.userInHouse
            ? $"VM: {pluginState.playersInHouse} patrons"
            : "VM: —";
          break;
        case DtrDisplayMode.VenueName:
          text = BuildVenueLabel();
          break;
        case DtrDisplayMode.SessionSales:
          text = SessionSalesCount > 0
            ? $"VM: {SessionSalesCount} sales / {SessionSalesTotal:N0}g"
            : "VM: no sales yet";
          break;
        case DtrDisplayMode.Combined:
          var parts = new List<string>();
          if (pluginState.userInHouse) parts.Add($"{pluginState.playersInHouse}p");
          var venue = BuildVenueLabel(prefix: false);
          if (!string.IsNullOrEmpty(venue) && venue != "Outside") parts.Add(venue);
          if (SessionSalesCount > 0) parts.Add($"{SessionSalesCount}s/{SessionSalesTotal:N0}g");
          if (pluginState.snoozed) parts.Add("zzz");
          text = parts.Count > 0 ? "VM: " + string.Join(" • ", parts) : "VM: idle";
          break;
        default:
          text = "VM";
          break;
      }
      dtrEntry.Text = text;
    }

    // Resolves the current venue's display name: xiv-app linked name first
    // (nice branding like "Rose Garden"), falling back to the raw
    // ward/plot tag (functional but dry). Returns "Outside" when the player
    // is not in a house.
    private string BuildVenueLabel(bool prefix = true)
    {
      var p = prefix ? "VM: " : "";
      if (!pluginState.userInHouse) return p + "Outside";

      var houseId = pluginState.currentHouse.houseId;
      if (houseId != 0 && Configuration.houseToXivAppVenue.TryGetValue(houseId, out var vid))
      {
        var v = xivAppVenues.Find(x => x.Id == vid);
        if (v != null && !string.IsNullOrEmpty(v.Name)) return p + v.Name;
      }
      if (venueList.venues.TryGetValue(houseId, out var local) && !string.IsNullOrEmpty(local.name))
        return p + local.name;

      var h = pluginState.currentHouse;
      return p + $"W{h.ward} P{h.plot}";
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
      if (running) {
        Log.Warning("Skipping processing while already running.");
        return;
      }
      running = true;
      try
      {
        UpdateDtrBar();

        // Every second we are in a house. Process players and see what has changed
        if (pluginState.userInHouse && stopwatch.ElapsedMilliseconds > 1000)
        {
          // Fetch updated house information
          if (pluginState.userInHouse)
          {
            try
            {
              var housingManager = HousingManager.Instance();
              var worldId = PlayerState.CurrentWorld.Value.RowId;
              // If the user has transitioned into a new house. Store that house information. Ensure we have a world to set it to 
              if (pluginState.currentHouse.houseId != (long)housingManager->GetCurrentIndoorHouseId().Id)
              {
                pluginState.currentHouse.houseId = (long)housingManager->GetCurrentIndoorHouseId().Id;
                pluginState.currentHouse.plot = housingManager->GetCurrentPlot() + 1; // Game stores plot as -1 
                pluginState.currentHouse.ward = housingManager->GetCurrentWard() + 1; // Game stores ward as -1 
                pluginState.currentHouse.room = housingManager->GetCurrentRoom();
                pluginState.currentHouse.type = (ushort)HousingManager.GetOriginalHouseTerritoryTypeId();
                pluginState.currentHouse.district = TerritoryUtils.getDistrict((long)housingManager->GetCurrentIndoorHouseId().Id);

                // Load current guest list from disk if player has entered a saved venue 
                if (venueList.venues.ContainsKey(pluginState.currentHouse.houseId))
                {
                  var venue = venueList.venues[pluginState.currentHouse.houseId];
                  GuestList venueGuestList = new GuestList(venue.houseId, venue);
                  venueGuestList.load();
                  guestLists.Add(venue.houseId, venueGuestList);
                }
              }
            }
            catch
            {
              // Typically fails first time after entering a house 
              running = false;
              return;
            }
          }

          if (!Configuration.showGuestsTab) {
            running = false;
            return;
          }

          bool guestListUpdated = false;
          bool playerArrived = false;
          int playerCount = 0;

          // Object to track seen players 
          Dictionary<string, bool> seenPlayers = new();
          foreach (var o in Objects)
          {
            // Reject non player objects 
            if (o is not IPlayerCharacter pc) continue;
            var player = Player.fromCharacter(pc);

            // Skip player characters that do not have a name. 
            // Portrait and Adventure plates show up with this. 
            if (pc.Name.TextValue.Length == 0) continue;
            // Im not sure what this means, but it seems that 4 is for players
            if (o.SubKind != 4) continue;
            playerCount++;

            // Add player to seen map 
            if (seenPlayers.ContainsKey(player.Name))
              seenPlayers[player.Name] = true;
            else
              seenPlayers.Add(player.Name, true);

            // Is the new player the current user 
            
            var isSelf = PlayerState.CharacterName == player.Name;

            // Store Player name 
            if (PlayerState.CharacterName != null && PlayerState.CharacterName.Length > 0) pluginState.playerName = PlayerState.CharacterName ?? "";

            // New Player has entered the house
            if (!getCurrentGuestList().guests.ContainsKey(player.Name))
            {
              guestListUpdated = true;
              getCurrentGuestList().guests.Add(player.Name, player);
              if (!isSelf) playerArrived = true;
              showGuestEnterChatAlert(getCurrentGuestList().guests[player.Name], isSelf);
              TryLogPatronVisit(player.Name, player.WorldName, "enter");
            }
            // Mark the player as re-entering the venue
            else if (!getCurrentGuestList().guests[player.Name].inHouse)
            {
              guestListUpdated = true;
              getCurrentGuestList().guests[player.Name].inHouse = true;
              getCurrentGuestList().guests[player.Name].latestEntry = DateTime.Now;
              getCurrentGuestList().guests[player.Name].timeCursor = DateTime.Now;
              getCurrentGuestList().guests[player.Name].entryCount++;
              showGuestEnterChatAlert(getCurrentGuestList().guests[player.Name], isSelf);
              TryLogPatronVisit(player.Name, player.WorldName, "enter");
            }
            // Current user just entered house
            else if (justEnteredHouse)
            {
              getCurrentGuestList().guests[player.Name].timeCursor = DateTime.Now;
              // setting is enabled to notify them on existing users. 
              if (this.Configuration.showChatAlertAlreadyHere) 
                showGuestEnterChatAlert(getCurrentGuestList().guests[player.Name], isSelf);
            }
            
            // Re-mark as friend incase status changed 
            getCurrentGuestList().guests[player.Name].isFriend = pc.StatusFlags.HasFlag(StatusFlags.Friend);

            // Mark last seen 
            getCurrentGuestList().guests[player.Name].lastSeen = DateTime.Now;

            // Mark last time current player enter house 
            if (justEnteredHouse && isSelf)
            {
              getCurrentGuestList().guests[player.Name].latestEntry = DateTime.Now;
            }
          }

          // Check for guests that have left the house 
          foreach (var guest in getCurrentGuestList().guests)
          {
            // Guest is marked as in the house 
            if (guest.Value.inHouse) 
            {
              // Guest was not seen this loop
              if (!seenPlayers.ContainsKey(guest.Value.Name))
              {
                guest.Value.onLeaveVenue();
                guestListUpdated = true;
                showGuestLeaveChatAlert(guest.Value);
                TryLogPatronVisit(guest.Value.Name, guest.Value.WorldName, "leave");
              }
              // Guest was seen this loop 
              else 
              {
                guest.Value.onAccumulateTime();
              }
            }
            
          }

          // Only play doorbell sound once if there were one or more new people
          if (Configuration.soundAlerts && playerArrived && !pluginState.snoozed)
          {
            doorbell.play();
          }

          // Save number of players seen this update 
          pluginState.playersInHouse = playerCount;

          // Save config if we saw new players
          if (guestListUpdated) getCurrentGuestList().save();

          justEnteredHouse = false;
          stopwatch.Restart();
        }
      }
      catch (Exception e)
      {
        Log.Error("Venue Manager Failed during framework update");
        Log.Error(e.ToString());
      }
      running = false;
    }

    public void playDoorbell()
    {
      doorbell.play();
    }

    public void reloadDoorbell()
    {
      doorbell.setType(Configuration.doorbellType);
      doorbell.load();
    }

    private void showGuestEnterChatAlert(Player player, bool isSelf)
    {
      var messageBuilder = new SeStringBuilder();
      var knownVenue = venueList.venues.ContainsKey(pluginState.currentHouse.houseId);

      // Show text alert for self if the venue is known
      if (isSelf)
      {
        if (knownVenue)
        {
          var venue = venueList.venues[pluginState.currentHouse.houseId];
          if (this.Configuration.showPluginNameInChat) messageBuilder.AddText($"[{Name}] ");
          messageBuilder.AddText("You have entered " + venue.name);
          Chat.Print(new XivChatEntry() { Message = messageBuilder.Build() });
        }
        return;
      }

      // Don't show alerts if snoozed 
      if (pluginState.snoozed) return;
      // Don't show if chat alerts disabled 
      if (!Configuration.showChatAlerts) return;

      // Alert type is already here 
      bool isAlreadyHere = justEnteredHouse && this.Configuration.showChatAlertAlreadyHere;

      // Return if not showing already here alerts
      if (justEnteredHouse && !this.Configuration.showChatAlertAlreadyHere) return;

      // Return if reentry alerts are disabled. (We need to ignore this check for already here alerts)
      if (player.entryCount > 1 && !Configuration.showChatAlertReentry && !isAlreadyHere) return;
      // Return if entry alerts are disabled . (We need to ignore this check for already here alerts)
      if (player.entryCount == 1 && !Configuration.showChatAlertEntry && !isAlreadyHere) return;

      // Show text alert for guests
      if (this.Configuration.showPluginNameInChat) messageBuilder.AddText($"[{Name}] ");

      // Player Color 
      messageBuilder.AddUiForeground(Colors.getChatColor(player, true));

      // Add player message 
      messageBuilder.Add(new PlayerPayload(player.Name, player.homeWorld));
      messageBuilder.AddUiForegroundOff();

      // Message Color 
      messageBuilder.AddUiForeground(Colors.getChatColor(player, false));

      // Current player has re-entered the house
      if (justEnteredHouse)
      {
        messageBuilder.AddText(" is already inside");
      }
      // Player enters house while you are already inside
      else
      {
        messageBuilder.AddText(" has entered");
        if (player.entryCount > 1)
          messageBuilder.AddText(" (" + player.entryCount + ")");
      }

      // Venue Name
      if (knownVenue)
      {
        var venue = venueList.venues[pluginState.currentHouse.houseId];
        messageBuilder.AddText(" " + venue.name);
      }
      else
      {
        messageBuilder.AddText(" the " + TerritoryUtils.getHouseType(pluginState.territory));
      }

      messageBuilder.AddUiForegroundOff();
      Chat.Print(new XivChatEntry() { Message = messageBuilder.Build() });
    }

    private void showGuestLeaveChatAlert(Player player)
    {
      if (!Configuration.showChatAlerts) return;
      if (!Configuration.showChatAlertLeave) return;
      // Don't show alerts if snoozed 
      if (pluginState.snoozed) return;

      var isSelf = PlayerState.CharacterName == player.Name;
      if (isSelf) return;
      // Don't show leave alerts if user just entered the building
      if (justEnteredHouse) return;

      var messageBuilder = new SeStringBuilder();
      var knownVenue = venueList.venues.ContainsKey(pluginState.currentHouse.houseId);

      // Add plugin name 
      if (this.Configuration.showPluginNameInChat) messageBuilder.AddText($"[{Name}] ");

      // Add Player name 
      messageBuilder.Add(new PlayerPayload(player.Name, player.homeWorld));
      messageBuilder.AddText(" has left");

      // Add Venue info
      if (knownVenue)
      {
        var venue = venueList.venues[pluginState.currentHouse.houseId];
        messageBuilder.AddText(" " + venue.name);
      }
      else
      {
        messageBuilder.AddText(" the " + TerritoryUtils.getHouseType(pluginState.territory));
      }

      var entry = new XivChatEntry() { Message = messageBuilder.Build() };
      Chat.Print(entry);
    }

    public GuestList getCurrentGuestList()
    {
      if (pluginState.userInHouse)
      {
        if (guestLists.ContainsKey(pluginState.currentHouse.houseId))
        {
          return guestLists[pluginState.currentHouse.houseId];
        }
      }
      return guestLists[0];
    }

    // Post a clickable player link in chat
    public void chatPlayerLink(Player player)
    {

      var messageBuilder = new SeStringBuilder();
      messageBuilder.Add(new PlayerPayload(player.Name, player.homeWorld));
      var entry = new XivChatEntry() { Message = messageBuilder.Build() };
      Chat.Print(entry);
    }

    /// <summary>
    /// Fire-and-forget patron-visit sync. Every enter/re-entry/leave the
    /// plugin observes at the current house is routed through here. We
    /// intentionally DO NOT filter out the plugin user's own character —
    /// staff who are off-duty (no active shift) count as patrons visiting
    /// their own venue, and the server classifies via wasWorking on insert.
    ///
    /// Gating order (cheapest first):
    ///   1. Sync enabled + API key present + client configured.
    ///   2. Current house → xiv-app venueId mapping exists.
    ///   3. If syncOnlyDuringEvents, the cached event-presence flag is true
    ///      (or, on cache miss, we fetch it async and bail for this arrival
    ///      — the next arrival within the TTL will go through).
    ///   4. Post.
    /// All failures log at Debug and swallow — we never surface a sync
    /// hiccup in chat during live service.
    /// </summary>
    public void TryLogPatronVisit(string characterName, string worldName, string action)
    {
      if (!Configuration.syncToXivApp) return;
      if (string.IsNullOrEmpty(Configuration.xivAppApiKey)) return;
      if (xivAppClient == null || !xivAppClient.IsConfigured) return;
      if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(worldName)) return;

      var houseId = pluginState.currentHouse.houseId;
      if (houseId == 0) return;
      if (!Configuration.houseToXivAppVenue.TryGetValue(houseId, out var venueId) || string.IsNullOrEmpty(venueId))
      {
        // No link configured for this house — silent skip. The VenuesTab
        // linking UI is the user-facing remedy.
        return;
      }

      _ = Task.Run(async () =>
      {
        try
        {
          if (Configuration.syncOnlyDuringEvents)
          {
            var cached = eventPresence.Get(venueId);
            if (cached == null)
            {
              var fresh = await xivAppClient.GetActiveEventAsync(venueId);
              if (fresh == null) return; // transport error — try again next arrival
              eventPresence.Set(venueId, fresh.Active, fresh.EventId);
              if (!fresh.Active) return;
            }
            else if (!cached.Active)
            {
              return;
            }
          }

          await xivAppClient.LogPatronVisitAsync(venueId, characterName, worldName, action);
        }
        catch (Exception ex)
        {
          Log.Debug($"TryLogPatronVisit failed: {ex.Message}");
        }
      });
    }

  } // Plugin
}
