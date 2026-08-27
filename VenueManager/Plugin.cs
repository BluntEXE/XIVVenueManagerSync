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
using System.Diagnostics.CodeAnalysis;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using System.Collections.Generic;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VenueManager.UI;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Bindings.ImGui;

namespace VenueManager
{
  public sealed class Plugin : IDalamudPlugin
  {
    public string Name => "XIV Venue Manager Sync";
    private const string CommandName = "/xvenue";
    private const string CommandNameAlias = "/xvm";
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
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
    public LifestreamIpc LifestreamIpc { get; private set; } = null!;

    public WindowSystem WindowSystem = new("VenueManager");
    private MainWindow MainWindow { get; init; }
    private NotesWindow NotesWindow { get; init; }
    private ChangelogWindow ChangelogWindow { get; init; }

    private Stopwatch stopwatch = new();
    private DoorbellSound doorbell;

    // Exterior-entry poll — see ARCHITECTURE.md § Housing/location detection
    private Stopwatch entryPollStopwatch = Stopwatch.StartNew();

    // Server Info Bar entry — see ARCHITECTURE.md § Status bar (DTR)
    private IDtrBarEntry? dtrEntry;
    private long dtrLastUpdateMs = 0;

    // Active shift cache, populated by background poll — see ARCHITECTURE.md § Shift tracking
    public volatile ShiftDto? activeShift = null;
    private long lastShiftPollMs = 0;
    private volatile bool shiftPollInFlight = false;
    private long lastBannedPollMs = 0;
    private volatile bool bannedPollInFlight = false;
    private volatile string? _shiftReminderShiftId = null;
    private long _shiftReminderLastMs = 0;
    // Non-blocking try-acquire mutex shared by chat-command and UI clock actions
    public readonly SemaphoreSlim clockSem = new SemaphoreSlim(1, 1);

    // Public so UI tabs can access the same client instance directly
    public XIVAppApiClient? xivAppClient;
    public List<XIVAppVenue> xivAppVenues = new();
    public List<Role> xivAppRoles = new();
    public List<Service> availableServices = new();
    public bool xivAppInventoryEnabled = false;
    public List<BannedPatron> xivAppBannedPatrons = new();
    public string? currentXivAppVenueId;

    // Gates patron-visit sync for "sync only during events" — see ARCHITECTURE.md § Patron sync & chat alerts
    public EventPresenceCache eventPresence = new();

    // Session-scoped sales tally (plugin lifetime, not persisted)
    public int SessionSalesTotal = 0;
    public int SessionSalesCount = 0;

    // Deep-link URL into the XIV-App website for the selected venue
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

    // Startup hydration so the user doesn't need to click Fetch Venues every launch — see ARCHITECTURE.md § Startup/configuration
    public async Task AutoLoadXivAppDataAsync()
    {
      if (xivAppClient == null || !xivAppClient.IsConfigured) return;
      try
      {
        xivAppVenues = await xivAppClient.Venue.GetVenuesAsync();
        Log.Information("Auto-loaded {Count} venue(s) on startup", xivAppVenues.Count);
        if (xivAppVenues.Count == 0) return;

        // Mirrors the manual Fetch button's selection logic
        var preferred = xivAppVenues.FirstOrDefault(v => v.Id == Configuration.selectedVenueId);
        var target = preferred ?? xivAppVenues[0];
        currentXivAppVenueId = target.Id;
        if (preferred == null)
        {
          Configuration.selectedVenueId = target.Id;
          Configuration.Save();
        }

        var roles = await xivAppClient.Venue.GetRolesAsync(target.Id);
        xivAppRoles = roles;
        Log.Information("Auto-loaded {Count} role(s) for venue {VenueId}", roles.Count, target.Id);

        var servicesResp = await xivAppClient.Venue.GetServicesAsync(target.Id);
        availableServices = servicesResp?.Services ?? new List<Service>();
        Log.Information("Auto-loaded {Count} service(s) for venue {VenueId}", availableServices.Count, target.Id);

        xivAppBannedPatrons = await xivAppClient.Venue.GetBannedPatronsAsync(target.Id);
        Log.Information("Auto-loaded {Count} banned patron(s) for venue {VenueId}", xivAppBannedPatrons.Count, target.Id);

        xivAppInventoryEnabled = await xivAppClient.Venue.GetInventoryEnabledAsync(target.Id);
        Log.Information("Auto-loaded inventory-enabled={0} for venue {VenueId}", xivAppInventoryEnabled, target.Id);
      }
      catch (Exception ex)
      {
        Log.Warning("XIV-App auto-load failed (manual Fetch Venues button still available): {0}", ex.Message);
      }
    }

    // Read from the running assembly so the dashboard strip always matches the installed version
    public string PluginVersion { get; } =
      typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "?";

    // True for the first loop that a player enters a house
    private bool justEnteredHouse = false;

    // Debounces leftHouse() against a single transient misread — see ARCHITECTURE.md § Housing/location detection
    private long? notAtPlotSinceMs = null;

    private bool running = false;

    public Plugin()
    {
      this.pluginState = new PluginState();
      this.venueList = new VenueList();
      this.venueList.load();

      this.guestLists.Add(0, new GuestList());
      this.guestLists[0].load();

      this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
      this.Configuration.Initialize(PluginInterface);

      // Always instantiated so the Settings tab can lazy-configure it on paste — see ARCHITECTURE.md § Startup/configuration
      xivAppClient = new XIVAppApiClient();
      if (!string.IsNullOrEmpty(Configuration.xivAppApiKey))
      {
          xivAppClient.Configure(Configuration.xivAppApiKey, Configuration.xivAppServerUrl);
          Log.Information("XIV-App API Client configured with server: {0}", Configuration.xivAppServerUrl);

          // Fire-and-forget: a server outage at launch must not block plugin init
          _ = AutoLoadXivAppDataAsync();
      }

      LifestreamIpc  = new LifestreamIpc();
      MainWindow     = new MainWindow(this);
      NotesWindow    = new NotesWindow(this);
      ChangelogWindow = new ChangelogWindow();

      WindowSystem.AddWindow(MainWindow);
      WindowSystem.AddWindow(NotesWindow);
      WindowSystem.AddWindow(ChangelogWindow);

      if (Configuration.LastSeenVersion != PluginVersion)
      {
        Configuration.LastSeenVersion = PluginVersion;
        Configuration.Save();
        ChangelogWindow.IsOpen = true;
      }

      CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Open venue manager interface to see patrons list and manage venues" });
      CommandManager.AddHandler(CommandNameAlias, new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Alias for /xvenue" });
      var SnoozeHandler = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Pause alerts until leaving the house." };
      var SnoozeHandlerAlias = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Alias for /xvenue snooze" };
      CommandManager.AddHandler(CommandName + " snooze", SnoozeHandler);
      CommandManager.AddHandler(CommandNameAlias + " snooze", SnoozeHandlerAlias);
      // These AddHandler calls exist purely to surface subcommands in /xlhelp — routing lives in OnCommand's args parser
      var SaleHelp    = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Open Sales tab. Usage: /xvm sale [amount] [customer]" };
      var SaleBangHelp = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Log a sale without opening UI. Usage: /xvm sale! <amount> [customer]" };
      var TipHelp     = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Open Sales tab, Tip selected. Usage: /xvm tip [amount] [customer]" };
      var TipBangHelp = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Log a tip without opening UI. Usage: /xvm tip! <amount> [customer]" };
      var TargetHelp  = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Open Sales tab prefilled with current target as customer. Usage: /xvm target [amount]" };
      var TargetBangHelp = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Log a sale for your current target without opening UI. Usage: /xvm target! <amount>" };
      var BanBangHelp    = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Ban your current target with a reason. Usage: /xvm ban! <reason>" };
      var StartHelp      = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Clock into your current shift. Usage: /xvm start" };
      var EndHelp        = new CommandInfo(OnCommand) { ShowInHelp = true, HelpMessage = "Clock out of your active shift. Usage: /xvm end" };
      CommandManager.AddHandler(CommandNameAlias + " sale",    SaleHelp);
      CommandManager.AddHandler(CommandNameAlias + " sale!",   SaleBangHelp);
      CommandManager.AddHandler(CommandNameAlias + " tip",     TipHelp);
      CommandManager.AddHandler(CommandNameAlias + " tip!",    TipBangHelp);
      CommandManager.AddHandler(CommandNameAlias + " target",  TargetHelp);
      CommandManager.AddHandler(CommandNameAlias + " target!", TargetBangHelp);
      CommandManager.AddHandler(CommandNameAlias + " ban!",    BanBangHelp);
      CommandManager.AddHandler(CommandNameAlias + " start",   StartHelp);
      CommandManager.AddHandler(CommandNameAlias + " end",     EndHelp);

      // Always created; visibility driven by display-mode config, not Get/Remove churn
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

      ClientState.TerritoryChanged += OnTerritoryChanged;
      Framework.Update += OnFrameworkUpdate;
      ClientState.Logout += OnLogout;

      doorbell = new DoorbellSound(this, Configuration.doorbellType);
      doorbell.load();

      // Register current location on boot
      OnTerritoryChanged(ClientState.TerritoryType);

      PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
      PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
    }

    public void Dispose()
    {
      PluginInterface.UiBuilder.Draw -= DrawUI;
      PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
      PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
      ClientState.Logout -= OnLogout;
      LifestreamIpc?.Dispose();
      xivAppClient?.Dispose();

      Framework.Update -= OnFrameworkUpdate;
      ClientState.TerritoryChanged -= OnTerritoryChanged;

      doorbell.disposeFile();

      // Remove DTR entry so the strip doesn't keep a stale slot after unload.
      try { dtrEntry?.Remove(); } catch { /* Dalamud already tore it down */ }
      dtrEntry = null;

      this.WindowSystem.RemoveAllWindows();

      MainWindow.Dispose();
      NotesWindow.Dispose();
      ChangelogWindow.Dispose();

      CommandManager.RemoveHandler(CommandName);
      CommandManager.RemoveHandler(CommandNameAlias);
      CommandManager.RemoveHandler(CommandName + " snooze");
      CommandManager.RemoveHandler(CommandNameAlias + " snooze");
      CommandManager.RemoveHandler(CommandNameAlias + " sale");
      CommandManager.RemoveHandler(CommandNameAlias + " sale!");
      CommandManager.RemoveHandler(CommandNameAlias + " tip");
      CommandManager.RemoveHandler(CommandNameAlias + " tip!");
      CommandManager.RemoveHandler(CommandNameAlias + " target");
      CommandManager.RemoveHandler(CommandNameAlias + " target!");
      CommandManager.RemoveHandler(CommandNameAlias + " ban!");
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

      // Sale subcommand family — see ARCHITECTURE.md § Slash commands
      if (args.StartsWith("sale") || args.StartsWith("tip") || args.StartsWith("target"))
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
            Chat.Print(prefix + "Usage: /xvm target! <amount>");
            return;
          }
          var t = TargetManager.Target;
          var targetName = t?.Name.TextValue;
          if (string.IsNullOrEmpty(targetName))
          {
            Chat.Print(prefix + "No target selected.");
            return;
          }
          _ = LogSaleOrTipSilentAsync(parsedAmount.Value, targetName, isTip: false);
          return;
        }

        if (verb == "target")
        {
          // Customer override is the game target, not an args field; falls through to null if no target
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
            Chat.Print((this.Configuration.showPluginNameInChat ? $"[{Name}] " : "") + "Usage: /xvm sale! <amount> [customer]");
            return;
          }
          _ = LogSaleOrTipSilentAsync(parsedAmount.Value, customerFromArgs, isTip: false);
          return;
        }

        if (verb == "tip!")
        {
          if (parsedAmount == null)
          {
            Chat.Print((this.Configuration.showPluginNameInChat ? $"[{Name}] " : "") + "Usage: /xvm tip! <amount> [customer]");
            return;
          }
          _ = LogSaleOrTipSilentAsync(parsedAmount.Value, customerFromArgs, isTip: true);
          return;
        }

        if (verb == "tip")
        {
          MainWindow.OpenTab("Sales");
          MainWindow.PrefillSale(parsedAmount, customerFromArgs, tip: true);
          MainWindow.IsOpen = true;
          return;
        }

        MainWindow.OpenTab("Sales");
        MainWindow.PrefillSale(parsedAmount, customerFromArgs);
        MainWindow.IsOpen = true;
        return;
      }

      // /xvm ban! <reason>  → ban your current target with a reason, chat toast on result
      if (args.StartsWith("ban!"))
      {
        string prefix = this.Configuration.showPluginNameInChat ? $"[{Name}] " : "";
        var reason = args.Length > 4 ? args.Substring(4).Trim() : "";
        if (string.IsNullOrWhiteSpace(reason))
        {
          Chat.Print(prefix + "Usage: /xvm ban! <reason>");
          return;
        }

        var target = TargetManager.Target;
        if (target == null || string.IsNullOrEmpty(target.Name.TextValue))
        {
          Chat.Print(prefix + "No target selected.");
          return;
        }
        if (target is not IPlayerCharacter targetCharacter)
        {
          Chat.Print(prefix + "Target must be a player character.");
          return;
        }

        var targetName = targetCharacter.Name.TextValue;
        var homeWorldName = targetCharacter.HomeWorld.Value.Name.ToString();
        var targetWorld = string.IsNullOrEmpty(homeWorldName) || homeWorldName == "Unknown"
          ? targetCharacter.CurrentWorld.Value.Name.ToString()
          : homeWorldName;

        _ = BanPatronSilentAsync(targetName, targetWorld, reason);
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

      MainWindow.IsOpen = true;
    }

    // Used by `/xvm sale!` — see ARCHITECTURE.md § Slash commands
    // Computes the chat prefix and checks XIV-App is configured with a venue selected,
    // printing the appropriate error and returning false if not ready.
    [MemberNotNullWhen(true, nameof(xivAppClient), nameof(currentXivAppVenueId))]
    private bool TryGetChatPrefix(out string prefix)
    {
      prefix = this.Configuration.showPluginNameInChat ? $"[{Name}] " : "";

      if (xivAppClient == null || !xivAppClient.IsConfigured)
      {
        Chat.Print(prefix + "XIV-App is not configured. Add your API key in Settings first.");
        return false;
      }
      if (string.IsNullOrEmpty(currentXivAppVenueId))
      {
        Chat.Print(prefix + "No venue selected. Pick one in Settings.");
        return false;
      }
      return true;
    }

    // Used by `/xvm sale!` and `/xvm tip!` — identical shape, differing only in the
    // transaction type tag and the "sale"/"tip" wording in chat output.
    public async Task LogSaleOrTipSilentAsync(int amount, string? customer, bool isTip)
    {
      if (!TryGetChatPrefix(out string prefix)) return;

      string label = isTip ? "Tip" : "Sale";
      try
      {
        string? trimmedName = string.IsNullOrWhiteSpace(customer) ? null : customer!.Trim();
        var result = await xivAppClient.Patron.LogTransactionAsync(
          currentXivAppVenueId,
          null,
          (decimal)amount,
          trimmedName,
          null,
          isTip ? "TIP" : null
        );

        if (result.Success)
        {
          SessionSalesTotal += amount;
          SessionSalesCount++;
          string suffix = isTip ? " tip" : "";
          Chat.Print(prefix + (trimmedName != null
            ? $"Logged {amount}g{suffix} from {trimmedName}"
            : $"Logged {amount}g{suffix}"));
        }
        else
        {
          Chat.Print(prefix + $"{label} failed: {result.Error ?? "unknown error"}");
        }
      }
      catch (Exception ex)
      {
        Log.Error($"LogSaleOrTipSilentAsync exception: {ex}");
        Chat.Print(prefix + $"{label} error: {ex.Message}");
      }
    }

    // Used by `/xvm ban!` — server finds-or-creates the Patron row
    public async Task BanPatronSilentAsync(string characterName, string world, string reason)
    {
      if (!TryGetChatPrefix(out string prefix)) return;

      try
      {
        var result = await xivAppClient.Patron.BanPatronAsync(currentXivAppVenueId, characterName, world, reason);

        if (result.Success)
        {
          Chat.Print(prefix + $"Banned {characterName}: {reason}");
        }
        else
        {
          Chat.Print(prefix + $"Ban failed: {result.Error ?? "unknown error"}");
        }
      }
      catch (Exception ex)
      {
        Log.Error($"BanPatronSilentAsync exception: {ex}");
        Chat.Print(prefix + $"Ban error: {ex.Message}");
      }
    }

    // Runs `action` while holding clockSem (non-blocking try-acquire), releasing
    // it afterward regardless of outcome. Chat/error-message wording and the
    // clock-in-vs-out domain logic stay in each caller - only the wait/release
    // scaffolding is shared.
    private async Task WithClockLockAsync(string prefix, Func<Task> action)
    {
      if (!await clockSem.WaitAsync(0))
      {
        Chat.Print(prefix + "A clock action is already in progress.");
        return;
      }
      try
      {
        await action();
      }
      finally
      {
        clockSem.Release();
      }
    }

    // Used by `/xvm start` — clocks into the first SCHEDULED shift
    public Task ShiftClockInSilentAsync()
    {
      if (!TryGetChatPrefix(out string prefix)) return Task.CompletedTask;

      return WithClockLockAsync(prefix, async () =>
      {
        try
        {
          var shifts = (await xivAppClient.Shift.GetShiftsResponseAsync(currentXivAppVenueId)).Shifts;
          var scheduled = shifts.Find(s => s.Status == "SCHEDULED");

          if (scheduled == null)
          {
            Chat.Print(prefix + "No scheduled shift found to clock into.");
            return;
          }

          var result = await xivAppClient.Shift.ClockInAsync(scheduled.Id);
          if (result.Success)
          {
            Chat.Print(prefix + "Clocked in. Shift is now active.");
            lastShiftPollMs = 0;
          }
          else
            Chat.Print(prefix + $"Clock-in failed: {result.Error ?? "unknown error"}");
        }
        catch (Exception ex)
        {
          Log.Error($"ShiftClockInSilentAsync exception: {ex}");
          Chat.Print(prefix + $"Clock-in error: {ex.Message}");
        }
      });
    }

    // Used by `/xvm end` — clocks out of the first ACTIVE shift
    public Task ShiftClockOutSilentAsync()
    {
      if (!TryGetChatPrefix(out string prefix)) return Task.CompletedTask;

      return WithClockLockAsync(prefix, async () =>
      {
        try
        {
          var shifts = (await xivAppClient.Shift.GetShiftsResponseAsync(currentXivAppVenueId)).Shifts;
          var active = shifts.Find(s => s.Status == "ACTIVE");

          if (active == null)
          {
            Chat.Print(prefix + "No active shift found to clock out of.");
            return;
          }

          var result = await xivAppClient.Shift.ClockOutAsync(active.Id);
          if (result.Success)
          {
            var hoursMsg = result.HoursWorked.HasValue
              ? $" ({result.HoursWorked.Value:F1}h worked)"
              : "";
            Chat.Print(prefix + $"Clocked out.{hoursMsg}");
            lastShiftPollMs = 0;
          }
          else
            Chat.Print(prefix + $"Clock-out failed: {result.Error ?? "unknown error"}");
        }
        catch (Exception ex)
        {
          Log.Error($"ShiftClockOutSilentAsync exception: {ex}");
          Chat.Print(prefix + $"Clock-out error: {ex.Message}");
        }
      });
    }

    // Forces a fresh shift poll next tick so the DTR label updates within one frame of a clock action
    public void InvalidateShiftPollCache() => lastShiftPollMs = 0;

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
      pluginState.territory = 0;
      leftHouse();
    }

    public void ToggleConfigUI() => MainWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
    public void OpenChangelog()  => ChangelogWindow.IsOpen = true;

    private unsafe void OnTerritoryChanged(uint territory)
    {
      pluginState.territory = (ushort)territory;

      // "In house" = interior instance OR plot exterior — see ARCHITECTURE.md § Housing/location detection
      bool inHouse = false;
      try
      {
        var housingManager = HousingManager.Instance();
        inHouse = housingManager->IsInside() || housingManager->IsOutside();
      }
      catch (Exception ex) {
        Log.Warning("Could not get housing state on territory change. " + ex.Message);
      }

      if (inHouse)
      {
        justEnteredHouse = true;
        pluginState.userInHouse = true;
        startTimers();
      }
      else if (pluginState.userInHouse)
      {
        leftHouse();
      }

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
      pluginState.currentHouse = new Venue();
      stopwatch.Stop();
      if (pluginState.snoozed) OnSnooze();
      // Force-refresh so "Outside" replaces the venue name without waiting for the 2s throttle
      UpdateDtrBar(force: true);
    }

    // Re-keys a house from the legacy interior-only id to the composite id — see ARCHITECTURE.md § Housing/location detection
    private bool MigrateLegacyHouseId(long legacyHouseId, long newHouseId)
    {
      if (!venueList.venues.TryGetValue(legacyHouseId, out var venue)) return false;

      Log.Information("Migrating house id {0} -> {1} ({2})", legacyHouseId, newHouseId, venue.name);

      venueList.venues.Remove(legacyHouseId);
      venue.houseId = newHouseId;
      venueList.venues[newHouseId] = venue;
      venueList.save();

      if (Configuration.houseToXivAppVenue.Remove(legacyHouseId, out var linkedVenueId))
      {
        Configuration.houseToXivAppVenue[newHouseId] = linkedVenueId;
        Configuration.Save();
      }

      var legacyFile = FileStore.GetFileInfo(legacyHouseId + "-guests.json");
      if (legacyFile.Exists)
      {
        var legacyGuestList = new GuestList(legacyHouseId, new Venue());
        legacyGuestList.load();
        legacyGuestList.houseId = newHouseId;
        legacyGuestList.venue = venue;
        legacyGuestList.save();
        legacyFile.Delete();
      }

      if (guestLists.Remove(legacyHouseId, out var cachedGuestList))
      {
        cachedGuestList.houseId = newHouseId;
        guestLists[newHouseId] = cachedGuestList;
      }

      return true;
    }

    // Re-keys by physical location match — covers the exterior-first case MigrateLegacyHouseId() can't
    private bool MigrateVenueByLocation(long newHouseId, uint worldId, int ward, int plot, int room, ushort type)
    {
      var match = venueList.venues.Values.FirstOrDefault(v =>
        v.houseId != newHouseId &&
        v.worldId == worldId && v.ward == ward && v.plot == plot && v.room == room && v.type == type);
      if (match == null) return false;

      return MigrateLegacyHouseId(match.houseId, newHouseId);
    }

    // Throttled to ~2s per framework tick — force=true for immediate updates on state transitions
    public void UpdateDtrBar(bool force = false)
    {
      if (dtrEntry == null) return;
      var mode = Configuration.dtrDisplayMode;
      dtrEntry.Shown = mode != DtrDisplayMode.Disabled;
      if (!dtrEntry.Shown) return;

      var nowMs = Environment.TickCount64;
      if (mode == DtrDisplayMode.ShiftStatus || mode == DtrDisplayMode.Combined)
        PollActiveShiftAsync();

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
        case DtrDisplayMode.ShiftStatus:
          text = BuildShiftLabel();
          break;
        case DtrDisplayMode.Combined:
          var parts = new List<string>();
          var shift = BuildShiftLabel(prefix: false, compact: true);
          if (!string.IsNullOrEmpty(shift)) parts.Add(shift);
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

    // xiv-app linked name first, falls back to raw ward/plot tag; "Outside" when not in a house
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

    // ACTIVE → "On shift 1h23m", SCHEDULED (within 2h) → "Shift in 45m", else "Off shift"/"" (compact)
    private string BuildShiftLabel(bool prefix = true, bool compact = false)
    {
      var p = prefix ? "VM: " : "";
      var s = activeShift;

      if (s != null && s.Status == "ACTIVE" && !string.IsNullOrEmpty(s.ActualStart))
      {
        if (DateTime.TryParse(s.ActualStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
        {
          var elapsed = DateTime.UtcNow - started.ToUniversalTime();
          if (elapsed.TotalSeconds < 0) elapsed = TimeSpan.Zero;
          return p + "On shift " + FormatDuration(elapsed);
        }
        return p + "On shift";
      }

      if (s != null && s.Status == "SCHEDULED" && !string.IsNullOrEmpty(s.ScheduledStart))
      {
        if (DateTime.TryParse(s.ScheduledStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startsAt))
        {
          var delta = startsAt.ToUniversalTime() - DateTime.UtcNow;
          if (delta.TotalMinutes > 0 && delta.TotalHours <= 2)
            return p + "Shift in " + FormatDuration(delta);
        }
      }

      return compact ? "" : p + "Off shift";
    }

    private static string FormatDuration(TimeSpan t)
    {
      if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h{t.Minutes:D2}m";
      if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m";
      return $"{(int)t.TotalSeconds}s";
    }

    // Reminds when an ACTIVE shift runs past scheduled end; repeats every 15 min — see ARCHITECTURE.md § Shift tracking
    private void CheckShiftEndReminder(ShiftDto? pick)
    {
      if (pick == null || pick.Status != "ACTIVE" || string.IsNullOrEmpty(pick.ScheduledEnd))
      {
        _shiftReminderShiftId = null;
        _shiftReminderLastMs = 0;
        return;
      }

      if (!DateTime.TryParse(pick.ScheduledEnd, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var endDt))
        return;

      var overBy = DateTime.UtcNow - endDt.ToUniversalTime();
      if (overBy.TotalSeconds <= 0)
      {
        if (_shiftReminderShiftId == pick.Id) { _shiftReminderShiftId = null; _shiftReminderLastMs = 0; }
        return;
      }

      var nowMs = Environment.TickCount64;
      bool firstReminder = _shiftReminderShiftId != pick.Id;
      bool intervalElapsed = nowMs - _shiftReminderLastMs >= 15 * 60 * 1000;

      if (!firstReminder && !intervalElapsed) return;

      string prefix = Configuration.showPluginNameInChat ? $"[{Name}] " : "";
      string overMsg = overBy.TotalMinutes < 2 ? "just ended" : $"ended {FormatDuration(overBy)} ago";
      Chat.Print(prefix + $"Your shift {overMsg} — type /xvm end when you're done.");

      _shiftReminderShiftId = pick.Id;
      _shiftReminderLastMs = nowMs;
    }

    // Polls every 30s so VIP/banned status set on the dashboard reaches the plugin without a manual resync
    private void PollBannedPatronsAsync()
    {
      if (bannedPollInFlight) return;
      var nowMs = Environment.TickCount64;
      if (nowMs - lastBannedPollMs < 30_000) return;
      if (xivAppClient == null || !xivAppClient.IsConfigured) return;
      if (string.IsNullOrEmpty(currentXivAppVenueId)) return;

      bannedPollInFlight = true;
      lastBannedPollMs = nowMs;
      var venueId = currentXivAppVenueId;

      _ = Task.Run(async () =>
      {
        try
        {
          xivAppBannedPatrons = await xivAppClient.Venue.GetBannedPatronsAsync(venueId);
        }
        catch (Exception ex)
        {
          Log.Warning($"Banned patron poll failed: {ex.Message}");
        }
        finally
        {
          bannedPollInFlight = false;
        }
      });
    }

    // Picks ACTIVE shift, else earliest future SCHEDULED, else null; errors swallowed ("Off shift" is a truthful fallback)
    private void PollActiveShiftAsync()
    {
      if (shiftPollInFlight) return;
      var nowMs = Environment.TickCount64;
      if (nowMs - lastShiftPollMs < 30_000) return;
      if (xivAppClient == null || !xivAppClient.IsConfigured) return;
      if (string.IsNullOrEmpty(currentXivAppVenueId)) return;

      lastShiftPollMs = nowMs;
      shiftPollInFlight = true;
      _ = Task.Run(async () =>
      {
        try
        {
          var list = (await xivAppClient.Shift.GetShiftsResponseAsync(currentXivAppVenueId)).Shifts;
          ShiftDto? pick = null;
          foreach (var s in list)
          {
            if (s.Status == "ACTIVE") { pick = s; break; }
          }
          if (pick == null)
          {
            DateTime? bestStart = null;
            foreach (var s in list)
            {
              if (s.Status != "SCHEDULED" || string.IsNullOrEmpty(s.ScheduledStart)) continue;
              if (!DateTime.TryParse(s.ScheduledStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) continue;
              var utc = dt.ToUniversalTime();
              if (utc < DateTime.UtcNow) continue;
              if (bestStart == null || utc < bestStart.Value) { bestStart = utc; pick = s; }
            }
          }
          activeShift = pick;
          CheckShiftEndReminder(pick);
        }
        catch (Exception ex)
        {
          Log.Warning($"DTR shift poll failed: {ex.Message}");
        }
        finally
        {
          shiftPollInFlight = false;
        }
      });
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
        PollBannedPatronsAsync();

        // Exterior-entry poll — see ARCHITECTURE.md § Housing/location detection
        if (!pluginState.userInHouse && entryPollStopwatch.ElapsedMilliseconds > 1000)
        {
          entryPollStopwatch.Restart();
          try
          {
            var housingManager = HousingManager.Instance();
            if (housingManager != null && Objects[0] is IPlayerCharacter pollSelf)
            {
              var polledHouseId = HouseIdentity.Current(housingManager, pollSelf.CurrentWorld.Value.RowId);
              if (polledHouseId != null)
              {
                justEnteredHouse = true;
                pluginState.userInHouse = true;
                startTimers();
              }
            }
          }
          catch (Exception ex)
          {
            Log.Verbose("Exterior entry poll failed (expected away from housing): " + ex.Message);
          }
        }

        // Every second we are in a house. Process players and see what has changed
        if (pluginState.userInHouse && stopwatch.ElapsedMilliseconds > 1000)
        {
          if (pluginState.userInHouse)
          {
            try
            {
              var housingManager = HousingManager.Instance();

              // Bail and retry next tick rather than risk a stale/zero world id corrupting the computed house identity
              if (Objects[0] is not IPlayerCharacter selfCharacter)
              {
                running = false;
                return;
              }
              uint currentWorldId = selfCharacter.CurrentWorld.Value.RowId;

              var computedHouseId = HouseIdentity.Current(housingManager, currentWorldId);
              if (computedHouseId == null)
              {
                // Walked off plot exterior without a territory change; debounced via notAtPlotSinceMs
                notAtPlotSinceMs ??= Environment.TickCount64;
                if (pluginState.userInHouse && Environment.TickCount64 - notAtPlotSinceMs.Value > 2000)
                {
                  leftHouse();
                  notAtPlotSinceMs = null;
                }
                running = false;
                return;
              }
              notAtPlotSinceMs = null;

              // One-time legacy-houseId self-heal — see ARCHITECTURE.md § Housing/location detection
              if (!venueList.venues.ContainsKey(computedHouseId.Value))
              {
                bool migrated = false;

                // Legacy interior id isn't available while outside — only fires after stepping inside once
                if (housingManager->IsInside())
                {
                  var legacyHouseId = (long)housingManager->GetCurrentIndoorHouseId().Id;
                  if (legacyHouseId != computedHouseId.Value)
                    migrated = MigrateLegacyHouseId(legacyHouseId, computedHouseId.Value);
                }

                // Fallback for the exterior-only case: match by physical location instead
                if (!migrated)
                {
                  var plotForMatch = housingManager->GetCurrentPlot() + 1;
                  var wardForMatch = housingManager->GetCurrentWard() + 1;
                  var roomForMatch = housingManager->GetCurrentRoom();
                  var typeForMatch = (ushort)HousingManager.GetOriginalHouseTerritoryTypeId();
                  migrated = MigrateVenueByLocation(computedHouseId.Value, currentWorldId, wardForMatch, plotForMatch, roomForMatch, typeForMatch);
                }

                if (migrated)
                {
                  // Force a fresh-transition detection below
                  pluginState.currentHouse.houseId = 0;
                }
              }

              if (pluginState.currentHouse.houseId != computedHouseId.Value)
              {
                var previousRoom = pluginState.currentHouse.room;
                pluginState.currentHouse.houseId = computedHouseId.Value;
                pluginState.currentHouse.plot = housingManager->GetCurrentPlot() + 1; // Game stores plot as -1
                pluginState.currentHouse.ward = housingManager->GetCurrentWard() + 1; // Game stores ward as -1
                pluginState.currentHouse.room = housingManager->GetCurrentRoom();
                pluginState.currentHouse.type = (ushort)HousingManager.GetOriginalHouseTerritoryTypeId();
                pluginState.currentHouse.district = TerritoryUtils.getDistrict(pluginState.currentHouse.type);
                pluginState.currentHouse.worldId = currentWorldId;

                // Auto-open Rooms tab when entering a private chamber (room > 0)
                if (pluginState.currentHouse.room > 0 && previousRoom != pluginState.currentHouse.room)
                {
                  MainWindow.OpenTab("Rooms");
                  if (!MainWindow.IsOpen)
                    MainWindow.IsOpen = true;
                  else
                  {
                    var status = MainWindow.Instance?.GetRoomStatus(pluginState.currentHouse.room) ?? "";
                    var msg = status switch
                    {
                      "Free" => $"Room {pluginState.currentHouse.room}: Free - open plugin to reserve",
                      "Occupied" => $"Room {pluginState.currentHouse.room}: Occupied",
                      "Locked" => $"Room {pluginState.currentHouse.room}: Locked",
                      "Disabled" => $"Room {pluginState.currentHouse.room}: Disabled",
                      _ => $"Room {pluginState.currentHouse.room}: Free - open plugin to reserve",
                    };
                    Chat.Print($"[{Name}] {msg}");
                  }
                }

                if (venueList.venues.ContainsKey(pluginState.currentHouse.houseId))
                {
                  var venue = venueList.venues[pluginState.currentHouse.houseId];
                  GuestList venueGuestList = new GuestList(venue.houseId, venue);
                  venueGuestList.load();
                  // Upsert, not Add — re-entering a house visited earlier this session would otherwise throw
                  guestLists[venue.houseId] = venueGuestList;
                }

                // Same precedence as BuildVenueLabel(): xiv-app linked name first, then local saved name
                pluginState.currentHouse.name = "";
                if (Configuration.houseToXivAppVenue.TryGetValue(pluginState.currentHouse.houseId, out var linkedVenueId))
                {
                  var linked = xivAppVenues.Find(x => x.Id == linkedVenueId);
                  if (linked != null && !string.IsNullOrEmpty(linked.Name))
                    pluginState.currentHouse.name = linked.Name;
                }
                if (string.IsNullOrEmpty(pluginState.currentHouse.name)
                    && venueList.venues.TryGetValue(pluginState.currentHouse.houseId, out var localVenue)
                    && !string.IsNullOrEmpty(localVenue.name))
                {
                  pluginState.currentHouse.name = localVenue.name;
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

          Dictionary<string, bool> seenPlayers = new();
          foreach (var o in Objects)
          {
            if (o is not IPlayerCharacter pc) continue;
            var player = Player.fromCharacter(pc);

            // Portrait/Adventure plates show up with an empty name
            if (pc.Name.TextValue.Length == 0) continue;
            if (o.SubKind != 4) continue;

            if (IsOutsidePlotBounds(o.Position)) continue;

            playerCount++;

            if (seenPlayers.ContainsKey(player.Name))
              seenPlayers[player.Name] = true;
            else
              seenPlayers.Add(player.Name, true);

            var isSelf = PlayerState.CharacterName == player.Name;

            if (PlayerState.CharacterName != null && PlayerState.CharacterName.Length > 0) pluginState.playerName = PlayerState.CharacterName ?? "";

            if (!getCurrentGuestList().guests.ContainsKey(player.Name))
            {
              guestListUpdated = true;
              getCurrentGuestList().guests.Add(player.Name, player);
              if (!isSelf) playerArrived = true;
              showGuestEnterChatAlert(getCurrentGuestList().guests[player.Name], isSelf);
              TryLogPatronVisit(player.Name, player.WorldName, "enter");
            }
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
            else if (justEnteredHouse)
            {
              getCurrentGuestList().guests[player.Name].timeCursor = DateTime.Now;
              if (this.Configuration.showChatAlertAlreadyHere)
                showGuestEnterChatAlert(getCurrentGuestList().guests[player.Name], isSelf);
            }

            getCurrentGuestList().guests[player.Name].isFriend = pc.StatusFlags.HasFlag(StatusFlags.Friend);

            getCurrentGuestList().guests[player.Name].lastSeen = DateTime.Now;

            if (justEnteredHouse && isSelf)
            {
              getCurrentGuestList().guests[player.Name].latestEntry = DateTime.Now;
            }
          }

          // First pass after loading from disk: reconcile stale inHouse=true silently, no "leave" sync
          bool skipLeaveSync = getCurrentGuestList().justLoaded;
          foreach (var guest in getCurrentGuestList().guests)
          {
            if (guest.Value.inHouse)
            {
              if (!seenPlayers.ContainsKey(guest.Value.Name))
              {
                guest.Value.onLeaveVenue();
                guestListUpdated = true;
                if (!skipLeaveSync)
                {
                  showGuestLeaveChatAlert(guest.Value);
                  TryLogPatronVisit(guest.Value.Name, guest.Value.WorldName, "leave");
                }
              }
              else
              {
                guest.Value.onAccumulateTime();
              }
            }

          }
          getCurrentGuestList().justLoaded = false;

          if (Configuration.soundAlerts && playerArrived && !pluginState.snoozed)
          {
            doorbell.play();
          }

          pluginState.playersInHouse = playerCount;

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

    private unsafe void SendGameChat(string message)
    {
      var uiModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->GetUIModule();
      if (uiModule == null) return;
      var chatMessage = Utf8String.FromString(message);
      uiModule->ProcessChatBoxEntry(chatMessage);
      chatMessage->Dtor();
    }

    private bool isBannedPatron(Player player)
    {
      return xivAppBannedPatrons.Any(v => v.CharacterName == player.Name && v.World == player.WorldName);
    }

    private void showGuestEnterChatAlert(Player player, bool isSelf)
    {
      var messageBuilder = new SeStringBuilder();
      var knownVenue = venueList.venues.ContainsKey(pluginState.currentHouse.houseId);

      // Only meaningful at a registered venue — otherwise every house spams the same line with no venue name
      if (!knownVenue) return;

      if (isSelf)
      {
        var selfVenue = venueList.venues[pluginState.currentHouse.houseId];
        if (this.Configuration.showPluginNameInChat) messageBuilder.AddText($"[{Name}] ");
        messageBuilder.AddText("You have entered " + selfVenue.name);
        Chat.Print(new XivChatEntry() { Message = messageBuilder.Build() });
        return;
      }

      // Fires independently of chat alert settings and snooze; only while a shift is active
      var shift = activeShift;
      if (!justEnteredHouse && shift != null && shift.Status == "ACTIVE")
      {
        if (player.entryCount == 1 && Configuration.enableGreeterMode && !string.IsNullOrWhiteSpace(Configuration.greeterMessage))
          SendGameChat($"/tell {player.Name}@{player.WorldName} {Configuration.greeterMessage}");
        else if (player.entryCount > 1 && Configuration.enableReentryGreeter && !string.IsNullOrWhiteSpace(Configuration.reentryGreeterMessage))
          SendGameChat($"/tell {player.Name}@{player.WorldName} {Configuration.reentryGreeterMessage}");
      }

      if (pluginState.snoozed) return;
      if (!Configuration.showChatAlerts) return;

      bool isAlreadyHere = justEnteredHouse && this.Configuration.showChatAlertAlreadyHere;

      if (justEnteredHouse && !this.Configuration.showChatAlertAlreadyHere) return;

      // isAlreadyHere bypasses these two checks
      if (player.entryCount > 1 && !Configuration.showChatAlertReentry && !isAlreadyHere) return;
      if (player.entryCount == 1 && !Configuration.showChatAlertEntry && !isAlreadyHere) return;

      if (this.Configuration.showPluginNameInChat) messageBuilder.AddText($"[{Name}] ");

      if (isBannedPatron(player))
      {
        messageBuilder.AddText("⚠ BANNED ");
      }

      messageBuilder.AddUiForeground(Colors.getChatColor(player, true));

      messageBuilder.Add(new PlayerPayload(player.Name, player.homeWorld));
      messageBuilder.AddUiForegroundOff();

      messageBuilder.AddUiForeground(Colors.getChatColor(player, false));

      if (justEnteredHouse)
      {
        messageBuilder.AddText(" is already inside");
      }
      else
      {
        messageBuilder.AddText(" has entered");
        if (player.entryCount > 1)
          messageBuilder.AddText(" (" + player.entryCount + ")");
      }

      var venue = venueList.venues[pluginState.currentHouse.houseId];
      messageBuilder.AddText(" " + venue.name);

      messageBuilder.AddUiForegroundOff();
      Chat.Print(new XivChatEntry() { Message = messageBuilder.Build() });
    }

    private void showGuestLeaveChatAlert(Player player)
    {
      if (!Configuration.showChatAlerts) return;
      if (!Configuration.showChatAlertLeave) return;
      if (pluginState.snoozed) return;

      var isSelf = PlayerState.CharacterName == player.Name;
      if (isSelf) return;
      if (justEnteredHouse) return;

      // Only meaningful at a registered venue
      if (!venueList.venues.TryGetValue(pluginState.currentHouse.houseId, out var venue)) return;

      var messageBuilder = new SeStringBuilder();

      if (this.Configuration.showPluginNameInChat) messageBuilder.AddText($"[{Name}] ");

      messageBuilder.Add(new PlayerPayload(player.Name, player.homeWorld));
      messageBuilder.AddText(" has left");

      messageBuilder.AddText(" " + venue.name);

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

    public void chatPlayerLink(Player player)
    {

      var messageBuilder = new SeStringBuilder();
      messageBuilder.Add(new PlayerPayload(player.Name, player.homeWorld));
      var entry = new XivChatEntry() { Message = messageBuilder.Build() };
      Chat.Print(entry);
    }

    /// <summary>
    /// Fire-and-forget patron-visit sync for every enter/re-entry/leave observed at the current house.
    /// </summary>
    /// <remarks>See ARCHITECTURE.md § Patron sync &amp; chat alerts for gating order and self-character handling.</remarks>
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
        // No link configured for this house — VenuesTab linking UI is the remedy
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
              var fresh = await xivAppClient.Venue.GetActiveEventAsync(venueId);
              if (fresh == null) return; // transport error, try again next arrival
              eventPresence.Set(venueId, fresh.Active, fresh.EventId);
              if (!fresh.Active) return;
            }
            else if (!cached.Active)
            {
              return;
            }
          }

          await xivAppClient.Patron.LogPatronVisitAsync(venueId, characterName, worldName, action);
        }
        catch (Exception ex)
        {
          Log.Debug($"TryLogPatronVisit failed: {ex.Message}");
        }
      });
    }

    private const float OutdoorGuestRadius = 15f;

    private static unsafe bool IsOutsidePlotBounds(System.Numerics.Vector3 position)
    {
      var hm = HousingManager.Instance();
      if (hm == null || !hm->IsOutside()) return false;
      var self = Objects[0];
      if (self == null) return false;
      var dx = self.Position.X - position.X;
      var dz = self.Position.Z - position.Z;
      return dx * dx + dz * dz > OutdoorGuestRadius * OutdoorGuestRadius;
    }

  } // Plugin
}
