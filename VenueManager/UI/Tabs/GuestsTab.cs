using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Internal;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using VenueManager.UI;
using VenueManager.Widgets;

namespace VenueManager.Tabs;

public class GuestsTab
{
  private Plugin plugin;
  private GuestListWidget guestListWidget;

  public GuestsTab(Plugin plugin)
  {
    this.plugin = plugin;
    this.guestListWidget = new GuestListWidget(plugin);
  }

  public unsafe void draw()
  {
    // Render high level information 
    if (plugin.pluginState.userInHouse)
    {
      if (plugin.venueList.venues.ContainsKey(plugin.pluginState.currentHouse.houseId))
      {
        var venue = plugin.venueList.venues[plugin.pluginState.currentHouse.houseId];
        ImGui.TextColored(Colors.XivSubtext0, "You are at ");
        ImGui.SameLine(0, 4f);
        ImGui.TextColored(Colors.XivBlue, venue.name);
      }
      else
      {
        var typeText = "";
        if (plugin.pluginState.currentHouse.plot > 0) typeText += "P" + plugin.pluginState.currentHouse.plot;
        if (plugin.pluginState.currentHouse.room > 0) typeText += "Room" + plugin.pluginState.currentHouse.room;
        ImGui.TextColored(Colors.XivSubtext0, "You are in a " + TerritoryUtils.getHouseType(plugin.pluginState.currentHouse.type) + " in " +
          plugin.pluginState.currentHouse.district + " W" + plugin.pluginState.currentHouse.ward + " " + typeText);
      }

      ImGui.TextColored(Colors.XivSubtext0, $"{plugin.pluginState.playersInHouse} patrons inside  ·  {plugin.getCurrentGuestList().guests.Count} total visitors");
    }
    else
    {
      ThemeManager.EmptyState("You are not in a house.");
    }
    if (plugin.pluginState.snoozed) ImGui.TextColored(Colors.StatusWarn, "Alarms snoozed");
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();

    if (plugin.pluginState.userInHouse)
    {
      if (plugin.venueList.venues.ContainsKey(plugin.pluginState.currentHouse.houseId))
      {
        var venue = plugin.venueList.venues[plugin.pluginState.currentHouse.houseId];
        ThemeManager.SectionHeader("Patron List — " + venue.name);
      }
      else
      {
        ThemeManager.ConfigBanner("This venue is not saved. Not all features will be supported.");
      }

      // We are in a saved house, draw guest list for that house
      if (plugin.venueList.venues.ContainsKey(plugin.pluginState.currentHouse.houseId))
        this.guestListWidget.draw(plugin.pluginState.currentHouse.houseId);
      // Otherwise draw public list
      else
        this.guestListWidget.draw(0);
    }
    else
    {
      ThemeManager.EmptyState("Enter a house to see the patron list.");
    }
  }
}