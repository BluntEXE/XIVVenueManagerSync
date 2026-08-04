using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using VenueManager.UI;

namespace VenueManager.Tabs;

public class RoomsTab
{
  private Plugin plugin;

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

  public RoomsTab(Plugin plugin)
  {
    this.plugin = plugin;
  }

  public void draw()
  {
    ImGui.BeginChild(1);

    if (plugin.xivAppClient == null || !plugin.xivAppClient.IsConfigured)
    {
      ThemeManager.ConfigBanner("XIV-App is not configured. Add your API key in Settings.");
      ImGui.EndChild();
      return;
    }

    if (string.IsNullOrEmpty(plugin.currentXivAppVenueId))
    {
      ThemeManager.ConfigBanner("No venue selected. Pick one in Settings.");
      ImGui.EndChild();
      return;
    }

    if (!loading && DateTime.Now - lastFetch > RefreshInterval)
    {
      _ = FetchRoomsAsync();
    }

    if (loading)
    {
      ImGui.TextDisabled("Loading...");
    }
    else
    {
      if (ImGui.SmallButton("Refresh"))
      {
        _ = FetchRoomsAsync();
      }
    }

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
    var statusColor = room.IsOccupied ? Colors.XivRed : Colors.XivGreen;
    var statusLabel = room.IsOccupied ? "Occupied" : "Free";

    ImGui.TextColored(statusColor, statusLabel);
    ImGui.SameLine();
    ImGui.Text(room.Name);

    if (!string.IsNullOrEmpty(room.Note))
    {
      ImGui.SameLine();
      ImGui.TextDisabled($"({room.Note})");
    }

    bool pending = pendingRoomIds.Contains(room.Id);

    string toggleLabel = room.IsOccupied ? $"Mark Free##{room.Id}" : $"Mark Occupied##{room.Id}";
    float btnWidth = ImGui.CalcTextSize(toggleLabel.Split('#')[0]).X + ImGui.GetStyle().FramePadding.X * 2;
    float rightEdge = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX();
    ImGui.SameLine();
    ImGui.SetCursorPosX(rightEdge - btnWidth);

    if (pending) ImGui.BeginDisabled();
    using (ThemeManager.PrimaryButton())
    {
      if (ImGui.SmallButton(toggleLabel))
        _ = SetStatusAsync(room, !room.IsOccupied, room.Note);
    }
    if (pending) ImGui.EndDisabled();

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
}
