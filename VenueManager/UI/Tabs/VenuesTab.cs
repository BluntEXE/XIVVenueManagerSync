using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Bindings.ImGui;

namespace VenueManager.Tabs;

public class VenuesTab
{
  private readonly Vector4 colorGreen = new(0, 0.69f, 0, 1);
  private Plugin plugin;

  // Venue name inside input box 
  private string venueName = string.Empty;

  public VenuesTab(Plugin plugin)
  {
    this.plugin = plugin;
  }

  public bool TryLoadIcon(uint iconId, [NotNullWhen(true)] out IDalamudTextureWrap? wrap, bool keepAlive = false)
  {
    var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
    wrap = texture.GetWrapOrEmpty();
    return wrap != null;
  }

  private List<KeyValuePair<long, Venue>> getSortedVenues(ImGuiTableSortSpecsPtr sortSpecs)
  {
    ImGuiTableColumnSortSpecsPtr currentSpecs = sortSpecs.Specs;

    var venues = plugin.venueList.venues.ToList();
    switch (currentSpecs.ColumnIndex)
    {
      case 2: // Name
        if (currentSpecs.SortDirection == ImGuiSortDirection.Ascending) venues.Sort((pair1, pair2) => pair2.Value.name.CompareTo(pair1.Value.name));
        else if (currentSpecs.SortDirection == ImGuiSortDirection.Descending) venues.Sort((pair1, pair2) => pair1.Value.name.CompareTo(pair2.Value.name));
        break;
      case 3: // District 
        if (currentSpecs.SortDirection == ImGuiSortDirection.Ascending) venues.Sort((pair1, pair2) => pair2.Value.district.CompareTo(pair1.Value.district));
        else if (currentSpecs.SortDirection == ImGuiSortDirection.Descending) venues.Sort((pair1, pair2) => pair1.Value.district.CompareTo(pair2.Value.district));
        break;
      case 7: // World
        if (currentSpecs.SortDirection == ImGuiSortDirection.Ascending) venues.Sort((pair1, pair2) => pair2.Value.WorldName.CompareTo(pair1.Value.WorldName));
        else if (currentSpecs.SortDirection == ImGuiSortDirection.Descending) venues.Sort((pair1, pair2) => pair1.Value.WorldName.CompareTo(pair2.Value.WorldName));
        break;
      case 8: // Datacenter 
        if (currentSpecs.SortDirection == ImGuiSortDirection.Ascending) venues.Sort((pair1, pair2) => pair2.Value.DataCenter.CompareTo(pair1.Value.DataCenter));
        else if (currentSpecs.SortDirection == ImGuiSortDirection.Descending) venues.Sort((pair1, pair2) => pair1.Value.DataCenter.CompareTo(pair2.Value.DataCenter));
        break;
      default:
        break;
    }

    return venues;
  }

  // Draw venue list menu 
  public unsafe void draw()
  {
    if (!plugin.pluginState.userInHouse) ImGui.BeginDisabled();
    // Copy current location to clipboard 
    if (ImGui.Button("Copy Current Address"))
    {
      ImGui.SetClipboardText(plugin.pluginState.currentHouse.getVenueAddress());
    }
    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !plugin.pluginState.userInHouse)
    {
      ImGui.SetTooltip("You must be in a house");
    }
    if (!plugin.pluginState.userInHouse) ImGui.EndDisabled();
    ImGui.Separator();
    ImGui.Spacing();

    ImGui.Text("Save the current venue you are in to the list of venues");
    ImGui.InputTextWithHint("", "Enter venue name", ref venueName, 256);
    ImGui.SameLine();
    // Only allow saving venue if name is entered, user is in a house, and current house id is not in list 
    bool canAdd = venueName.Length > 0 &&
      plugin.pluginState.userInHouse &&
      !plugin.venueList.venues.ContainsKey(plugin.pluginState.currentHouse.houseId);
    if (!canAdd) ImGui.BeginDisabled();
    if (ImGui.Button("Save Venue"))
    {
      // Save venue to saved venue list 
      Venue venue = new Venue(plugin.pluginState.currentHouse);
      venue.name = venueName;
      plugin.venueList.venues.Add(venue.houseId, venue);
      plugin.venueList.save();
      // Add a new guest list to the main registry for this venue 
      GuestList guestList = new GuestList(venue.houseId, venue);
      plugin.guestLists.Add(venue.houseId, guestList);
    }
    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
    {
      if (!plugin.pluginState.userInHouse)
        ImGui.SetTooltip("You are not in a house");
      else if (plugin.venueList.venues.ContainsKey(plugin.pluginState.currentHouse.houseId))
        ImGui.SetTooltip("Current venue already saved as " + plugin.venueList.venues[plugin.pluginState.currentHouse.houseId].name);
      else if (venueName.Length == 0)
        ImGui.SetTooltip("You must enter a name");
    }
    if (!canAdd) ImGui.EndDisabled();

    ImGui.Spacing();
    ImGui.BeginChild(1);
    // Table flags:
    //   Sortable    — users click headers to sort (existing behaviour).
    //   Resizable   — users can drag column separators so a cramped
    //                 Notes field is a one-drag fix. This was the
    //                 single biggest complaint before the fix.
    //   ScrollX     — if the window is narrow, a horizontal scrollbar
    //                 appears instead of columns collapsing to nothing.
    //                 Requires an explicit outer_size to work; Dalamud's
    //                 default (0,0) is fine because we're inside a child
    //                 that already has a scroll region.
    //   BordersInnerV — thin vertical lines between columns so the
    //                 draggable separators are visually discoverable.
    //   RowBg       — alternating row tint pulled from the theme.
    var tableFlags = ImGuiTableFlags.Sortable
                   | ImGuiTableFlags.Resizable
                   | ImGuiTableFlags.ScrollX
                   | ImGuiTableFlags.BordersInnerV
                   | ImGuiTableFlags.RowBg;
    if (ImGui.BeginTable("Venues", 12, tableFlags))
    {
      // Column default widths — WidthStretch shares the remaining space
      // after fixed columns. We give Notes the widest default because
      // that's the most common "I can't read this" offender.
      ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 24);
      ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 24);
      ImGui.TableSetupColumn("Name",       ImGuiTableColumnFlags.WidthStretch, 160);
      ImGui.TableSetupColumn("District",   ImGuiTableColumnFlags.WidthStretch, 110);
      ImGui.TableSetupColumn("Ward",       ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 40);
      ImGui.TableSetupColumn("Plot",       ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 40);
      ImGui.TableSetupColumn("Room",       ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 40);
      ImGui.TableSetupColumn("World",      ImGuiTableColumnFlags.WidthStretch, 100);
      ImGui.TableSetupColumn("DataCenter", ImGuiTableColumnFlags.WidthStretch, 100);
      ImGui.TableSetupColumn("XIV-App Venue", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, 200);
      ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 24);
      ImGui.TableSetupColumn("Notes",      ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort, 280);
      ImGui.TableHeadersRow();

      ImGuiTableSortSpecsPtr sortSpecs = ImGui.TableGetSortSpecs();
      var venues = getSortedVenues(sortSpecs);

      foreach (var venue in venues)
      {
        var fontColor = plugin.pluginState.userInHouse && plugin.pluginState.currentHouse.houseId == venue.Value.houseId ?
          colorGreen : new Vector4(1, 1, 1, 1);

        ImGui.TableNextColumn();
        if (ImGuiComponents.IconButton("##Copy" + venue.Value.houseId, FontAwesomeIcon.Copy))
        {
          ImGui.SetClipboardText(venue.Value.getVenueAddress());
        }
        if (ImGui.IsItemHovered())
        {
          ImGui.SetTooltip("Copy Address");
        }
        ImGui.TableNextColumn();
        if (TryLoadIcon(TerritoryUtils.getHouseIcon(venue.Value.type), out var iconHandle))
          ImGui.Image(iconHandle.Handle, new Vector2(ImGui.GetFrameHeight()));
        else
          ImGui.Dummy(new Vector2(ImGui.GetFrameHeight()));
        ImGui.TableNextColumn();
        ImGui.TextColored(fontColor, venue.Value.name);
        ImGui.TableNextColumn();
        ImGui.TextColored(fontColor, venue.Value.district);
        ImGui.TableNextColumn();
        ImGui.TextColored(fontColor, "" + venue.Value.ward);
        ImGui.TableNextColumn();
        ImGui.TextColored(fontColor, venue.Value.plot > 0 ? "" + venue.Value.plot : "");
        ImGui.TableNextColumn();
        ImGui.TextColored(fontColor, venue.Value.room > 0 ? "" + venue.Value.room : "");
        ImGui.TableNextColumn();
        ImGui.TextColored(fontColor, venue.Value.WorldName);
        ImGui.TableNextColumn();
        ImGui.TextColored(fontColor, venue.Value.DataCenter);

        // XIV-App venue link — maps this in-game house to a venue on the
        // website so patron visits post to the right place. Requires the
        // user to have signed in via Settings → XIV-App Sync; otherwise the
        // dropdown is empty and we show a disabled placeholder.
        ImGui.TableNextColumn();
        drawXivAppVenuePicker(venue.Value.houseId);

        // Allow the user to delete the saved venue
        ImGui.TableNextColumn();
        bool disabled = false;
        if (!ImGui.IsKeyDown(ImGuiKey.LeftCtrl) && !ImGui.IsKeyDown(ImGuiKey.RightCtrl))
        {
          ImGui.BeginDisabled();
          disabled = true;
        }
        if (ImGuiComponents.IconButton("##" + venue.Value.houseId, FontAwesomeIcon.Trash))
        {
          plugin.venueList.venues.Remove(venue.Value.houseId);
          plugin.venueList.save();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && disabled)
        {
          ImGui.SetTooltip("Hold control to delete venue");
        }
        if (disabled) ImGui.EndDisabled();

        // Notes Section 
        ImGui.TableNextColumn();
        if (ImGuiComponents.IconButton("##Notes" + venue.Value.houseId, FontAwesomeIcon.StickyNote))
        {
          plugin.ShowNotesWindow(venue.Value);
        }
        if (ImGui.IsItemHovered())
        {
          ImGui.SetTooltip($"Edit {venue.Value.name} notes");
        }
        var notes = venue.Value.notes;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint($"##notes{venue.Value.name}", "Notes", ref notes, 256);
        if (notes != venue.Value.notes) {
          plugin.venueList.venues[venue.Key].notes = notes;
          plugin.venueList.save();
        }
      }

      ImGui.EndTable();
    }
    ImGui.EndChild();
  }

  /// <summary>
  /// Draws the XIV-App venue dropdown for a single saved house. Selection
  /// writes straight to <c>Configuration.houseToXivAppVenue[houseId]</c>
  /// and saves immediately — the next patron arrival at this house will
  /// pick up the new mapping without a reload.
  /// </summary>
  private void drawXivAppVenuePicker(long houseId)
  {
    var config = plugin.Configuration;
    var venues = plugin.xivAppVenues;
    ImGui.PushItemWidth(-1);

    // Not signed in / venues never fetched → disabled placeholder.
    if (venues == null || venues.Count == 0)
    {
      ImGui.BeginDisabled();
      string placeholder = string.IsNullOrEmpty(config.xivAppApiKey)
        ? "(sign in under Settings)"
        : "(fetch venues first)";
      ImGui.InputText($"##xivapplink_{houseId}", ref placeholder, 64, ImGuiInputTextFlags.ReadOnly);
      ImGui.EndDisabled();
      ImGui.PopItemWidth();
      return;
    }

    config.houseToXivAppVenue.TryGetValue(houseId, out var currentId);
    currentId ??= string.Empty;

    string currentLabel = "— unlinked —";
    if (!string.IsNullOrEmpty(currentId))
    {
      var match = venues.Find(v => v.Id == currentId);
      currentLabel = match != null ? match.Name : "(missing venue)";
    }

    if (ImGui.BeginCombo($"##xivapplink_{houseId}", currentLabel))
    {
      if (ImGui.Selectable("— unlinked —", string.IsNullOrEmpty(currentId)))
      {
        if (config.houseToXivAppVenue.Remove(houseId))
        {
          config.Save();
          plugin.eventPresence.Clear();
        }
      }
      foreach (var v in venues)
      {
        bool selected = v.Id == currentId;
        if (ImGui.Selectable(v.Name + "##opt_" + houseId + "_" + v.Id, selected))
        {
          config.houseToXivAppVenue[houseId] = v.Id;
          config.Save();
          // Invalidate cached event-presence so the new link picks up the
          // correct event state on the next arrival instead of inheriting
          // whatever was cached for the previous mapping.
          plugin.eventPresence.Clear();
        }
        if (selected) ImGui.SetItemDefaultFocus();
      }
      ImGui.EndCombo();
    }

    ImGui.PopItemWidth();
  }
}
