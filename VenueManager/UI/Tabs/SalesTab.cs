using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using VenueManager.UI;

namespace VenueManager.Tabs;

public class SalesTab
{
  private Plugin plugin;

  // Form state — persists across frames because the tab instance is
  // long-lived (constructed once in MainWindow).
  private int selectedServiceIndex = 0; // 0 = "(no service)"
  private string customerName = string.Empty;
  private string amountText = "0";
  private string notes = string.Empty;

  // Tracks whether we've already auto-filled customerName from the
  // current target. Without this the field would refill on every draw
  // frame after the user cleared it.
  private bool customerPrimed = false;

  // Last-action feedback rendered below the submit button.
  private string statusMessage = string.Empty;
  private bool statusIsError = false;
  private bool submitting = false;

  public SalesTab(Plugin plugin)
  {
    this.plugin = plugin;
  }

  // Accept prefill values from slash commands (/vm sale, /vm target).
  // Called before the window opens; the next draw() frame picks up
  // the new field values via normal ImGui immediate-mode reads.
  public void Prefill(int? amount, string? customer)
  {
    if (amount.HasValue)
      amountText = amount.Value.ToString(CultureInfo.InvariantCulture);
    if (customer != null)
    {
      customerName = customer;
      customerPrimed = true; // don't let auto-prime overwrite the explicit value
    }
  }

  public void draw()
  {
    ImGui.BeginChild(1);

    // --- Gates ---------------------------------------------------------
    if (plugin.xivAppClient == null || !plugin.xivAppClient.IsConfigured)
    {
      ThemeManager.ConfigBanner("XIV-App is not configured. Add your API key in Settings before logging sales.");
      ImGui.EndChild();
      return;
    }

    if (string.IsNullOrEmpty(plugin.currentXivAppVenueId))
    {
      ThemeManager.ConfigBanner("No venue selected. Pick an active venue in Settings.");
      ImGui.EndChild();
      return;
    }

    // --- Service dropdown ---------------------------------------------
    var services = plugin.availableServices;
    string selectedLabel = "(no service)";
    if (selectedServiceIndex > 0 && selectedServiceIndex - 1 < services.Count)
    {
      var svc = services[selectedServiceIndex - 1];
      selectedLabel = $"{svc.Name} — {svc.Price}g";
    }

    ImGui.TextColored(Colors.XivSubtext0, "Service");
    if (ImGui.BeginCombo("##sales-service", selectedLabel))
    {
      bool noneSelected = selectedServiceIndex == 0;
      if (ImGui.Selectable("(no service)", noneSelected))
        selectedServiceIndex = 0;
      if (noneSelected) ImGui.SetItemDefaultFocus();

      for (int i = 0; i < services.Count; i++)
      {
        var svc = services[i];
        bool isSelected = selectedServiceIndex == i + 1;
        var label = $"{svc.Name} — {svc.Price}g";
        if (ImGui.Selectable(label, isSelected))
        {
          selectedServiceIndex = i + 1;
          if (decimal.TryParse(svc.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            amountText = ((int)parsed).ToString(CultureInfo.InvariantCulture);
        }
        if (isSelected) ImGui.SetItemDefaultFocus();
      }
      ImGui.EndCombo();
    }

    ImGui.Spacing();

    // --- Customer name -------------------------------------------------
    if (!customerPrimed)
    {
      var target = Plugin.TargetManager.Target;
      if (target != null) customerName = target.Name.TextValue;
      customerPrimed = true;
    }

    ImGui.TextColored(Colors.XivSubtext0, "Customer");
    float iconBtnW = ImGui.GetFrameHeight();
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - iconBtnW - ImGui.GetStyle().ItemSpacing.X);
    ImGui.InputText("##sales-customer", ref customerName, 64);
    ImGui.SameLine();
    // Icon button: crosshair = "use target"
    ImGui.PushFont(UiBuilder.IconFont);
    if (ImGui.Button($"{FontAwesomeIcon.Crosshairs.ToIconString()}##target", new Vector2(iconBtnW, iconBtnW)))
    {
      var target = Plugin.TargetManager.Target;
      if (target != null) customerName = target.Name.TextValue;
    }
    ImGui.PopFont();
    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Use current target");

    ImGui.Spacing();

    // --- Amount --------------------------------------------------------
    ImGui.TextColored(Colors.XivSubtext0, "Amount (gil)");
    ImGui.SetNextItemWidth(200f);
    ImGui.InputText("##sales-amount", ref amountText, 16);

    ImGui.Spacing();

    // --- Notes ---------------------------------------------------------
    ImGui.TextColored(Colors.XivSubtext0, "Notes (optional)");
    ImGui.InputTextMultiline("##sales-notes", ref notes, 512, new Vector2(-1, 48f));

    ImGui.Spacing();

    // --- Submit --------------------------------------------------------
    bool parseOk = int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAmount) && parsedAmount > 0;
    bool canSubmit = parseOk && !submitting;
    if (!canSubmit) ImGui.BeginDisabled();
    using (ThemeManager.PrimaryButton())
    {
      if (ImGui.Button(submitting ? "Logging..." : "Log Sale", new Vector2(-1, 26f)))
        _ = LogSaleAsync(parsedAmount);
    }
    if (!canSubmit) ImGui.EndDisabled();

    if (!parseOk && !string.IsNullOrEmpty(amountText) && amountText != "0")
    {
      ImGui.SameLine();
      ImGui.TextDisabled("(amount must be a positive whole number)");
    }

    // --- Status line ---------------------------------------------------
    if (!string.IsNullOrEmpty(statusMessage))
    {
      ImGui.Spacing();
      ImGui.TextColored(statusIsError ? Colors.StatusErr : Colors.StatusOk, statusMessage);

      // After a successful sale, offer a deep link to the sales page.
      if (!statusIsError)
      {
        var salesUrl = plugin.BuildVenueUrl("sales");
        if (salesUrl != null)
        {
          ImGui.SameLine();
          if (ImGui.SmallButton("View on site"))
          {
            Util.OpenLink(salesUrl);
          }
        }
      }
    }

    ImGui.EndChild();
  }

  private async Task LogSaleAsync(int amount)
  {
    if (plugin.xivAppClient == null) return;

    // Snapshot the venue id so a mid-request venue switch can't send the
    // sale to the wrong venue, and so the compiler sees a non-null string
    // at the API call site.
    var venueId = plugin.currentXivAppVenueId;
    if (string.IsNullOrEmpty(venueId)) return;

    submitting = true;
    statusMessage = "Logging…";
    statusIsError = false;

    try
    {
      string? serviceId = null;
      if (selectedServiceIndex > 0 && selectedServiceIndex - 1 < plugin.availableServices.Count)
      {
        serviceId = plugin.availableServices[selectedServiceIndex - 1].Id;
      }

      string? trimmedName = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim();
      string? trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

      var result = await plugin.xivAppClient.Patron.LogTransactionAsync(
        venueId,
        serviceId,
        (decimal)amount,
        trimmedName,
        trimmedNotes
      );

      if (result.Success)
      {
        // Bump the session tally so the dashboard strip's "💰 Ng" readout
        // updates the same frame this call returns on.
        plugin.SessionSalesTotal += amount;
        plugin.SessionSalesCount++;

        statusMessage = trimmedName != null
          ? $"Logged {amount}g from {trimmedName}"
          : $"Logged {amount}g";
        statusIsError = false;
        // Reset the parts that should change per sale. Keep service
        // selection so rapid-fire logging of the same item stays one
        // click; clear customer + notes so the next sale doesn't
        // accidentally reuse them.
        notes = string.Empty;
        customerName = string.Empty;
        customerPrimed = false; // re-prime from target on next draw
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
      Plugin.Log.Error($"LogSaleAsync exception: {ex}");
    }
    finally
    {
      submitting = false;
    }
  }
}
