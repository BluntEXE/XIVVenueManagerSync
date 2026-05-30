using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using VenueManager.UI;

namespace VenueManager.Tabs;

public class ShiftsTab
{
  private Plugin plugin;

  private List<ShiftDto> shifts = new();
  private bool loading = false;
  private bool clocking = false;
  private string statusMessage = string.Empty;
  private bool statusIsError = false;
  private DateTime lastFetch = DateTime.MinValue;

  // Refresh interval — shifts don't change often, 30s keeps things
  // responsive without hammering the server on every frame.
  private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

  private static readonly Vector4 ColorActive   = Colors.XivGreen;
  private static readonly Vector4 ColorUpcoming = Colors.XivSubtext0;
  private static readonly Vector4 ColorComplete = Colors.XivOverlay0;

  public ShiftsTab(Plugin plugin)
  {
    this.plugin = plugin;
  }

  public void draw()
  {
    ImGui.BeginChild(1);

    // --- Gates ---
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

    // Auto-refresh on interval
    if (!loading && DateTime.Now - lastFetch > RefreshInterval)
    {
      _ = FetchShiftsAsync();
    }

    // Manual refresh button
    if (loading)
    {
      ImGui.TextDisabled("Loading...");
    }
    else
    {
      if (ImGui.SmallButton("Refresh"))
      {
        _ = FetchShiftsAsync();
      }
    }

    ImGui.Separator();

    if (shifts.Count == 0 && !loading)
    {
      ThemeManager.EmptyState("No shifts scheduled.");
      ImGui.EndChild();
      return;
    }

    // Render shifts grouped: active first, then upcoming, then completed
    bool anyActive = false;
    bool anyUpcoming = false;
    bool anyCompleted = false;

    foreach (var shift in shifts)
    {
      if (shift.Status == "ACTIVE" && !anyActive)
      {
        anyActive = true;
        ImGui.Spacing();
        ThemeManager.SectionHeader("ON SHIFT", Colors.XivGreen);
      }
      if (shift.Status == "ACTIVE") drawShiftRow(shift);
    }

    foreach (var shift in shifts)
    {
      if (shift.Status == "SCHEDULED" && !anyUpcoming)
      {
        anyUpcoming = true;
        ImGui.Spacing();
        ThemeManager.SectionHeader("UPCOMING", Colors.XivSubtext0);
      }
      if (shift.Status == "SCHEDULED") drawShiftRow(shift);
    }

    foreach (var shift in shifts)
    {
      if (shift.Status == "COMPLETED" && !anyCompleted)
      {
        anyCompleted = true;
        ImGui.Spacing();
        ThemeManager.SectionHeader("COMPLETED", Colors.XivOverlay0);
      }
      if (shift.Status == "COMPLETED") drawShiftRow(shift);
    }

    // --- Status line ---
    if (!string.IsNullOrEmpty(statusMessage))
    {
      ImGui.Spacing();
      ImGui.TextColored(statusIsError ? Colors.StatusErr : Colors.StatusOk, statusMessage);
    }

    ImGui.EndChild();
  }

  private void drawShiftRow(ShiftDto shift)
  {
    // Parse scheduled times — use TryParse so malformed/null server data
    // doesn't throw inside the ImGui draw loop and leave BeginChild unbalanced.
    if (!DateTime.TryParse(shift.ScheduledStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var schedStartUtc)
     || !DateTime.TryParse(shift.ScheduledEnd,   CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var schedEndUtc))
    {
      ImGui.TextDisabled($"(invalid shift data — id: {shift.Id})");
      return;
    }
    var schedStart = schedStartUtc.ToLocalTime();
    var schedEnd   = schedEndUtc.ToLocalTime();

    // Format: "Apr 12  7:00 PM — 11:00 PM"
    string timeLabel;
    if (schedStart.Date == DateTime.Today)
      timeLabel = $"Today  {schedStart:h:mm tt} — {schedEnd:h:mm tt}";
    else if (schedStart.Date == DateTime.Today.AddDays(1))
      timeLabel = $"Tomorrow  {schedStart:h:mm tt} — {schedEnd:h:mm tt}";
    else
      timeLabel = $"{schedStart:MMM d}  {schedStart:h:mm tt} — {schedEnd:h:mm tt}";

    ImGui.Text(timeLabel);

    if (!string.IsNullOrEmpty(shift.Notes))
    {
      ImGui.SameLine();
      ImGui.TextDisabled($"({shift.Notes})");
    }

    // Action buttons
    ImGui.SameLine();
    float rightEdge = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX();

    if (shift.Status == "SCHEDULED")
    {
      // Check if within clock-in window (30 min before scheduled start)
      var windowStart = schedStart.AddMinutes(-30);
      bool canClockIn = DateTime.Now >= windowStart && !clocking;

      string btnLabel = $"Clock In##{shift.Id}";
      float btnWidth = ImGui.CalcTextSize("Clock In").X + ImGui.GetStyle().FramePadding.X * 2;
      ImGui.SameLine();
      ImGui.SetCursorPosX(rightEdge - btnWidth);

      if (!canClockIn) ImGui.BeginDisabled();
      using (ThemeManager.PrimaryButton())
      {
        if (ImGui.SmallButton(btnLabel))
          _ = ClockInAsync(shift.Id);
      }
      if (!canClockIn) ImGui.EndDisabled();

      if (!canClockIn && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
      {
        ImGui.SetTooltip($"Available at {windowStart:h:mm tt}");
      }
    }
    else if (shift.Status == "ACTIVE")
    {
      // Show elapsed time + clock out button
      if (!string.IsNullOrEmpty(shift.ActualStart)
       && DateTime.TryParse(shift.ActualStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startedUtc))
      {
        var elapsed = DateTime.Now - startedUtc.ToLocalTime();
        ImGui.SameLine();
        ImGui.TextColored(ColorActive, $"{elapsed.Hours}h {elapsed.Minutes}m");
      }

      string btnLabel = $"Clock Out##{shift.Id}";
      float btnWidth = ImGui.CalcTextSize("Clock Out").X + ImGui.GetStyle().FramePadding.X * 2;
      ImGui.SameLine();
      ImGui.SetCursorPosX(rightEdge - btnWidth);

      if (clocking) ImGui.BeginDisabled();
      using (ThemeManager.PrimaryButton())
      {
        if (ImGui.SmallButton(btnLabel))
          _ = ClockOutAsync(shift.Id);
      }
      if (clocking) ImGui.EndDisabled();
    }
    else if (shift.Status == "COMPLETED"
          && DateTime.TryParse(shift.ActualStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var completedStartUtc)
          && DateTime.TryParse(shift.ActualEnd,   CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var completedEndUtc))
    {
      var worked = completedEndUtc - completedStartUtc;
      ImGui.SameLine();
      ImGui.TextDisabled($"{(int)worked.TotalHours}h {worked.Minutes}m worked");
    }

    ImGui.Spacing();
  }

  private async Task FetchShiftsAsync()
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId))
      return;

    loading = true;
    try
    {
      shifts = await plugin.xivAppClient.Shift.GetMyShiftsAsync(plugin.currentXivAppVenueId);
      lastFetch = DateTime.Now;
    }
    catch (Exception ex)
    {
      Plugin.Log.Warning($"Error fetching shifts: {ex.Message}");
    }
    finally
    {
      loading = false;
    }
  }

  private async Task ClockInAsync(string shiftId)
  {
    if (plugin.xivAppClient == null) return;
    if (!await plugin.clockSem.WaitAsync(0))
    {
      statusMessage = "A clock action is already in progress.";
      statusIsError = true;
      return;
    }
    clocking = true;
    statusMessage = "Clocking in...";
    statusIsError = false;

    try
    {
      var result = await plugin.xivAppClient.Shift.ClockInAsync(shiftId);
      if (result.Success)
      {
        statusMessage = "Clocked in!";
        statusIsError = false;
        plugin.InvalidateShiftPollCache();
        _ = FetchShiftsAsync();
      }
      else
      {
        statusMessage = $"Clock-in failed: {result.Error ?? "unknown"}";
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
      clocking = false;
      plugin.clockSem.Release();
    }
  }

  private async Task ClockOutAsync(string shiftId)
  {
    if (plugin.xivAppClient == null) return;
    if (!await plugin.clockSem.WaitAsync(0))
    {
      statusMessage = "A clock action is already in progress.";
      statusIsError = true;
      return;
    }
    clocking = true;
    statusMessage = "Clocking out...";
    statusIsError = false;

    try
    {
      var result = await plugin.xivAppClient.Shift.ClockOutAsync(shiftId);
      if (result.Success)
      {
        string hours = result.HoursWorked.HasValue
          ? $" ({result.HoursWorked:F1}h worked)"
          : "";
        statusMessage = $"Clocked out!{hours}";
        statusIsError = false;
        plugin.InvalidateShiftPollCache();
        _ = FetchShiftsAsync();
      }
      else
      {
        statusMessage = $"Clock-out failed: {result.Error ?? "unknown"}";
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
      clocking = false;
      plugin.clockSem.Release();
    }
  }
}
