using System;
using System.Collections.Generic;
using System.Linq;

namespace VenueManager
{
  public class ItemSearchResult
  {
    public uint ItemId { get; set; }
    public string Name { get; set; } = "";
    public uint IconId { get; set; }
  }

  public static class ItemSearch
  {
    // Local Lumina lookup — no network call needed, unlike the dashboard's
    // XIVAPI proxy. Both paths converge on the same real item ID.
    public static List<ItemSearchResult> Search(string query, int limit = 20)
    {
      if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        return new List<ItemSearchResult>();

      var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
      if (sheet == null) return new List<ItemSearchResult>();

      var needle = query.Trim();
      return sheet
        .Where(item => !string.IsNullOrEmpty(item.Name.ToString())
                     && item.Name.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase))
        .Take(limit)
        .Select(item => new ItemSearchResult
        {
          ItemId = item.RowId,
          Name = item.Name.ToString(),
          IconId = item.Icon,
        })
        .ToList();
    }
  }
}
