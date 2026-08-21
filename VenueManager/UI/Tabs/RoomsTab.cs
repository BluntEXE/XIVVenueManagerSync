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

  private HashSet<string> pendingRoomIds = new();
  private int selectedDurationIndex = -1;
  private string? postDiscordStatus = null;

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

    bool froggeReady = plugin.xivAppVenues.Any(v => v.Id == plugin.currentXivAppVenueId && v.FroggeConnected);

    if (froggeReady)
    {
      using (ThemeManager.PrimaryButton())
      {
        if (ImGui.SmallButton("Post to Discord"))
          _ = PostToDiscordAsync();
      }
      if (!string.IsNullOrEmpty(postDiscordStatus))
      {
        ImGui.SameLine();
        ImGui.TextDisabled(postDiscordStatus);
      }
      ImGui.Spacing();
    }

    foreach (var room in rooms)
      drawRoomRow(room, froggeReady);

    if (!string.IsNullOrEmpty(statusMessage))
    {
      ImGui.Spacing();
      ImGui.TextColored(statusIsError ? Colors.StatusErr : Colors.StatusOk, statusMessage);
    }

    ImGui.EndChild();
  }

  private void drawRoomRow(Room room, bool froggeReady)
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
    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip(statusLabel switch
      {
        "Free" => "Room is available for reservation.",
        "Occupied" => "Room is currently reserved by a patron.",
        "Locked" => "Room is locked. Patrons cannot reserve it, but staff can still toggle it.",
        "Disabled" => "Room is hidden from the reservation list entirely.",
        _ => ""
      });
    }
    ImGui.SameLine();
    ImGui.Text(room.Name);

    if (froggeReady && !string.IsNullOrEmpty(room.OwnerDiscordId))
    {
      ImGui.SameLine();
      var member = plugin.xivAppFroggeMembers.FirstOrDefault(m => m.Id == room.OwnerDiscordId);
      var ownerLabel = member != null ? member.Username : room.OwnerDiscordId[..Math.Min(8, room.OwnerDiscordId.Length)];
      ImGui.TextDisabled($"({ownerLabel})");
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
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Select how long to reserve this room.");
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
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Release this room so others can reserve it.");
      if (pending) ImGui.EndDisabled();
    }

    if (froggeReady && room.IsOccupied)
    {
      ImGui.SameLine();
      var currentOwner = room.OwnerDiscordId ?? "";
      if (pending) ImGui.BeginDisabled();
      ImGui.PushItemWidth(140);
      if (ImGui.BeginCombo($"Owner##{room.Id}", GetOwnerDisplay(room)))
      {
        if (ImGui.Selectable("(none)", currentOwner == ""))
          _ = SetRoomOwnerAsync(room, null);
        foreach (var m in plugin.xivAppFroggeMembers)
        {
          bool isSelected = m.Id == currentOwner;
          if (ImGui.Selectable($"{m.Username}##{m.Id}", isSelected))
            _ = SetRoomOwnerAsync(room, m.Id);
        }
        ImGui.EndCombo();
      }
      ImGui.PopItemWidth();
      if (pending) ImGui.EndDisabled();
    }

    // Lock/Disable buttons for managers
    if (!room.IsOccupied && isInHouse && isCurrentRoom)
    {
      ImGui.SameLine();
      if (pending) ImGui.BeginDisabled();
      if (ImGui.SmallButton(room.Locked ? $"Unlock##{room.Id}" : $"Lock##{room.Id}"))
        _ = ToggleLockAsync(room);
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip(room.Locked ? "Unlock this room so patrons can reserve it." : "Lock this room to prevent new reservations.");
      if (pending) ImGui.EndDisabled();

      ImGui.SameLine();
      if (pending) ImGui.BeginDisabled();
      if (ImGui.SmallButton(room.Disabled ? $"Enable##{room.Id}" : $"Disable##{room.Id}"))
        _ = ToggleDisableAsync(room);
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip(room.Disabled ? "Enable this room to make it visible again." : "Disable this room to hide it from patrons.");
      if (pending) ImGui.EndDisabled();
    }

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

  public string GetRoomStatus(int roomNumber)
  {
    var room = rooms.FirstOrDefault(r => r.RoomNumber == roomNumber);
    if (room == null) return "";
    if (room.Disabled) return "Disabled";
    if (room.Locked) return "Locked";
    if (room.IsOccupied) return "Occupied";
    return "Free";
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

  private async Task ToggleLockAsync(Room room)
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId)) return;
    if (!pendingRoomIds.Add(room.Id)) return;

    try
    {
      var result = await plugin.xivAppClient.Venue.UpdateRoomAsync(
        plugin.currentXivAppVenueId, room.Id, locked: !room.Locked);

      if (result.Success)
      {
        statusMessage = room.Locked ? $"Unlocked {room.Name}" : $"Locked {room.Name}";
        statusIsError = false;
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

  private async Task ToggleDisableAsync(Room room)
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId)) return;
    if (!pendingRoomIds.Add(room.Id)) return;

    try
    {
      var result = await plugin.xivAppClient.Venue.UpdateRoomAsync(
        plugin.currentXivAppVenueId, room.Id, disabled: !room.Disabled);

      if (result.Success)
      {
        statusMessage = room.Disabled ? $"Enabled {room.Name}" : $"Disabled {room.Name}";
        statusIsError = false;
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

  private string GetOwnerDisplay(Room room)
  {
    if (string.IsNullOrEmpty(room.OwnerDiscordId)) return "(none)";
    var member = plugin.xivAppFroggeMembers.FirstOrDefault(m => m.Id == room.OwnerDiscordId);
    return member != null ? member.Username : room.OwnerDiscordId[..Math.Min(12, room.OwnerDiscordId.Length)];
  }

  private async Task PostToDiscordAsync()
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId)) return;

    postDiscordStatus = "Posting...";
    try
    {
      var result = await plugin.xivAppClient.Venue.PostRoomsToDiscordAsync(plugin.currentXivAppVenueId);
      postDiscordStatus = result.Success ? "Posted!" : result.Error ?? "Failed";
    }
    catch (Exception ex)
    {
      postDiscordStatus = $"Error: {ex.Message}";
    }
  }

  private async Task SetRoomOwnerAsync(Room room, string? ownerDiscordId)
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId)) return;
    if (!pendingRoomIds.Add(room.Id)) return;

    try
    {
      var result = await plugin.xivAppClient.Venue.SetRoomOwnerAsync(
        plugin.currentXivAppVenueId, room.Id, ownerDiscordId);

      if (result.Success)
      {
        statusMessage = ownerDiscordId != null ? $"Assigned owner to {room.Name}" : $"Cleared owner for {room.Name}";
        statusIsError = false;
        _ = FetchRoomsAsync();
      }
      else
      {
        statusMessage = $"Failed: {result.Error ?? "unknown error"}";
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
