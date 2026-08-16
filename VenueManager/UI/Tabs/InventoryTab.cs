using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using VenueManager.UI;

namespace VenueManager.Tabs;

public class InventoryTab : ITab
{
  private Plugin plugin;

  public string Name => "Inventory";
  public FontAwesomeIcon Icon => FontAwesomeIcon.WineGlass;
  public string Tooltip => "Inventory";
  public bool IsVisible => plugin.xivAppInventoryEnabled;

  private string searchQuery = "";
  private List<ItemSearchResult> searchResults = new();
  private string? linkingServiceId = null;
  private string restockInput = "";
  private string? restockingServiceId = null;

  public InventoryTab(Plugin plugin)
  {
    this.plugin = plugin;
  }

  public void draw()
  {
    ImGui.BeginChild(1);

    if (string.IsNullOrEmpty(plugin.currentXivAppVenueId))
    {
      ImGui.TextColored(Colors.XivSubtext0, "Select a venue in Settings first.");
      ImGui.EndChild();
      return;
    }

    foreach (var service in plugin.availableServices)
    {
      drawServiceRow(service);
    }

    ImGui.EndChild();
  }

  private void drawServiceRow(Service service)
  {
    ImGui.PushID(service.Id);
    ImGui.Separator();

    ImGui.TextColored(Colors.XivBlue, service.Name);

    if (service.StockCount.HasValue)
    {
      bool low = service.StockCount.Value <= 5;
      ImGui.SameLine();
      ImGui.TextColored(low ? Colors.XivRed : Colors.XivSubtext0, $"({service.StockCount.Value} in stock)");
    }
    else
    {
      ImGui.SameLine();
      ImGui.TextColored(Colors.XivOverlay0, "(not tracked)");
    }

    if (!string.IsNullOrEmpty(service.LinkedItemName))
    {
      ImGui.TextColored(Colors.XivSubtext0, $"Linked: {service.LinkedItemName}");
    }

    if (ImGui.SmallButton("Link Item"))
    {
      linkingServiceId = linkingServiceId == service.Id ? null : service.Id;
      searchQuery = "";
      searchResults.Clear();
    }
    ImGui.SameLine();
    if (ImGui.SmallButton("Restock"))
    {
      restockingServiceId = restockingServiceId == service.Id ? null : service.Id;
      restockInput = service.StockCount?.ToString() ?? "";
    }

    if (linkingServiceId == service.Id) drawLinkItemPanel(service);
    if (restockingServiceId == service.Id) drawRestockPanel(service);

    ImGui.PopID();
  }

  private void drawLinkItemPanel(Service service)
  {
    ImGui.Indent();
    if (ImGui.InputText("Item name##search", ref searchQuery, 100))
    {
      searchResults = ItemSearch.Search(searchQuery);
    }

    foreach (var result in searchResults)
    {
      if (ImGui.Selectable($"{result.Name}##{result.ItemId}"))
      {
        _ = LinkItemAsync(service.Id, (int)result.ItemId, result.Name, (int)result.IconId);
      }
    }
    ImGui.Unindent();
  }

  private void drawRestockPanel(Service service)
  {
    ImGui.Indent();
    ImGui.InputText("Stock count##restock", ref restockInput, 10);
    if (ImGui.SmallButton("Save##restock"))
    {
      if (int.TryParse(restockInput, out var count) && count >= 0)
      {
        _ = RestockAsync(service.Id, count);
      }
    }
    ImGui.Unindent();
  }

  // Fire-and-forget calls used by the Link Item / Restock buttons. No
  // client-side OWNER/MANAGER gate here — matches BanPatronSilentAsync's
  // precedent (Plugin.cs): the server enforces the role check and returns
  // an error we log via Plugin.Log.Warning, same as the ban command.
  private async Task LinkItemAsync(string serviceId, int itemId, string itemName, int iconId)
  {
    await RunInventoryMutationAsync(
      (client, venueId) => client.Venue.LinkItemAsync(venueId, serviceId, itemId, itemName, iconId),
      () => linkingServiceId = null,
      "link item");
  }

  private async Task RestockAsync(string serviceId, int count)
  {
    await RunInventoryMutationAsync(
      (client, venueId) => client.Venue.RestockAsync(venueId, serviceId, count),
      () => restockingServiceId = null,
      "restock");
  }

  private async Task RunInventoryMutationAsync(
    Func<XIVAppApiClient, string, Task<LogTransactionResult>> mutation,
    Action onSuccess,
    string failureVerb)
  {
    if (plugin.xivAppClient == null || string.IsNullOrEmpty(plugin.currentXivAppVenueId)) return;

    var result = await mutation(plugin.xivAppClient, plugin.currentXivAppVenueId);
    if (result.Success)
    {
      onSuccess();
      var servicesResp = await plugin.xivAppClient.Venue.GetServicesAsync(plugin.currentXivAppVenueId);
      plugin.availableServices = servicesResp?.Services ?? plugin.availableServices;
    }
    else
    {
      Plugin.Log.Warning($"Failed to {failureVerb}: {result.Error}");
    }
  }
}
