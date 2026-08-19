using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using VenueManager.UI;

namespace VenueManager.Tabs;

public class RoomsTab : ITab
{
  private Plugin plugin;

  public string Name => "Rooms";
  public FontAwesomeIcon Icon => FontAwesomeIcon.DoorOpen;
  public string Tooltip => "Rooms";
  public bool IsVisible => true;

  private List<Room> rooms = new();
  private bool loading = false;
  private string statusMessage = string.Empty;
  private bool statusIsError = false;
  private DateTime lastFetch = DateTime.MinValue;

  // Poll while this tab is visible — draw() is only called by MainWindow
  // when Rooms is the active tab, so this check naturally stops polling
  // the moment staff switch away. Matches ShiftsTab's exact pattern.
  private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(25);

  private Dictionary<string, string> noteDrafts = new();
  private HashSet<string> pendingRoomIds = new();
  private int selectedDurationIndex = -1;

  private static readonly (string Label, int Minutes)[] DurationOptions = {
    ("30 min", 30), ("1 hr", 60), ("1:30", 90), ("2 hr", 120),
    ("2:30", 150), ("3 hr", 180), ("3:30", 210), ("4 hr", 240),
    ("5 hr", 300), ("6 hr", 360), ("7 hr", 420), ("8 hr", 480),
  };

  public RoomsTab(Plugin plugin)
  {
    this.plugin = plugin;
  }

  public void draw()
  {
    ImGui.BeginChild(1);

    if (!ThemeManager.RequireXivAppReady(plugin))
    {
      ImGui.EndChild();
      return;
    }

    ThemeManager.PollGate(loading, lastFetch, RefreshInterval, () => { _ = FetchRoomsAsync(); });

    ImGui.Separator();

    if (rooms.Count == 0 && !loading)
    {
      ThemeManager.EmptyState("No rooms configured. Add rooms from the dashboard.");
      ImGui.EndChild();
      return;
    }

    foreach (var room in rooms)
      drawRoomRow(room);

    if (!string.IsNullOrEmpty(statusMessage))
    {
      ImGui.Spacing();
      ImGui.TextColored(statusIsError ? Colors.StatusErr : Colors.StatusOk, statusMessage);
    }

    ImGui.EndChild();
  }

  private void drawRoomRow(Room room)
  {
    var statusColor = Colors.XivGreen;
    var statusLabel = "Free";

    if (room.Disabled)
    {
      statusColor = Colors.XivSubtext0;
      statusLabel = "Disabled";
    }
    else if (room.Locked)
    {
      statusColor = Colors.XivYellow;
      statusLabel = "Locked";
    }
    else if (room.IsOccupied)
    {
      statusColor = Colors.XivRed;
      statusLabel = "Occupied";
    }

    ImGui.TextColored(statusColor, statusLabel);
    ImGui.SameLine();
    ImGui.Text(room.Name);

    if (!string.IsNullOrEmpty(room.Note))
    {
      ImGui.SameLine();
      ImGui.TextDisabled($"({room.Note})");
    }

    bool pending = pendingRoomIds.Contains(room.Id);
    bool isCurrentRoom = plugin.pluginState.currentHouse.room == room.RoomNumber;
    bool isInHouse = plugin.pluginState.userInHouse;
    bool canReserve = !room.IsOccupied && !room.Locked && !room.Disabled && isInHouse && isCurrentRoom;

    if (canReserve)
    {
      ImGui.SameLine();
      ImGui.PushItemWidth(120);

      int durationIndex = selectedDurationIndex;
      if (pending) ImGui.BeginDisabled();
      if (ImGui.Combo($"##duration{room.Id}", ref durationIndex,
          DurationOptions.Select(d => d.Label).ToArray(), DurationOptions.Length))
      {
        selectedDurationIndex = durationIndex;
        _ = ReserveRoomAsync(room, DurationOptions[durationIndex].Minutes);
      }
      if (pending) ImGui.EndDisabled();

      ImGui.PopItemWidth();
    }
    else if (room.IsOccupied && isCurrentRoom)
    {
      ImGui.SameLine();
      if (pending) ImGui.BeginDisabled();
      using (ThemeManager.PrimaryButton())
      {
        if (ImGui.SmallButton($"Release##{room.Id}"))
          _ = ReleaseRoomAsync(room);
      }
      if (pending) ImGui.EndDisabled();
    }

    if (!noteDrafts.TryGetValue(room.Id, out var draft))
      draft = room.Note ?? "";

    if (pending) ImGui.BeginDisabled();
    ImGui.PushItemWidth(200);
    if (ImGui.InputTextWithHint($"##note{room.Id}", "Note…", ref draft, 200, ImGuiInputTextFlags.EnterReturnsTrue))
    {
      _ = SetStatusAsync(room, room.IsOccupied, draft);
    }
    else
    {
      noteDrafts[room.Id] = draft;
    }
    ImGui.PopItemWidth();
    if (pending) ImGui.EndDisabled();

    ImGui.Spacing();
  }

  private async Task FetchRoomsAsync()
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId))
      return;

    loading = true;
    try
    {
      rooms = await plugin.xivAppClient.Venue.GetRoomsAsync(plugin.currentXivAppVenueId);
      lastFetch = DateTime.Now;
    }
    catch (Exception ex)
    {
      Plugin.Log.Warning($"Error fetching rooms: {ex.Message}");
    }
    finally
    {
      loading = false;
    }
  }

  private async Task SetStatusAsync(Room room, bool isOccupied, string? note)
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId)) return;
    if (!pendingRoomIds.Add(room.Id)) return;

    try
    {
      var result = await plugin.xivAppClient.Venue.SetRoomStatusAsync(plugin.currentXivAppVenueId, room.Id, isOccupied, note);
      if (result.Success)
      {
        statusMessage = $"{room.Name}: {(isOccupied ? "marked occupied" : "marked free")}";
        statusIsError = false;
        noteDrafts.Remove(room.Id);
        _ = FetchRoomsAsync();
      }
      else
      {
        statusMessage = $"Failed to update {room.Name}: {result.Error ?? "unknown error"}";
        statusIsError = true;
      }
    }
    catch (Exception ex)
    {
      statusMessage = $"Error: {ex.Message}";
      statusIsError = true;
    }
    finally
    {
      pendingRoomIds.Remove(room.Id);
    }
  }

  private async Task ReserveRoomAsync(Room room, int durationMinutes)
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId)) return;
    if (!pendingRoomIds.Add(room.Id)) return;

    try
    {
      var result = await plugin.xivAppClient.Venue.ReserveRoomAsync(
        plugin.currentXivAppVenueId, room.Id, durationMinutes);

      if (result.Success)
      {
        statusMessage = $"Reserved {room.Name} for {durationMinutes} min";
        statusIsError = false;
        _ = FetchRoomsAsync();
      }
      else
      {
        statusMessage = $"Failed to reserve {room.Name}: {result.Error ?? "unknown error"}";
        statusIsError = true;
      }
    }
    catch (Exception ex)
    {
      statusMessage = $"Error: {ex.Message}";
      statusIsError = true;
    }
    finally
    {
      pendingRoomIds.Remove(room.Id);
      selectedDurationIndex = -1;
    }
  }

  private async Task ReleaseRoomAsync(Room room)
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId)) return;
    if (!pendingRoomIds.Add(room.Id)) return;

    try
    {
      var result = await plugin.xivAppClient.Venue.ReleaseRoomAsync(
        plugin.currentXivAppVenueId, room.Id);

      if (result.Success)
      {
        statusMessage = $"Released {room.Name}";
        statusIsError = false;
        _ = FetchRoomsAsync();
      }
      else
      {
        statusMessage = $"Failed to release {room.Name}: {result.Error ?? "unknown error"}";
        statusIsError = true;
      }
    }
    catch (Exception ex)
    {
      statusMessage = $"Error: {ex.Message}";
      statusIsError = true;
    }
    finally
    {
      pendingRoomIds.Remove(room.Id);
    }
  }
}
