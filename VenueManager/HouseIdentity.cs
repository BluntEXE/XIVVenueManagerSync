using FFXIVClientStructs.FFXIV.Client.Game;

namespace VenueManager
{
  // Composite house identity that works both inside the house interior and
  // standing outside on the plot exterior, unlike the native
  // HousingManager.GetCurrentIndoorHouseId() (interior-instance only, returns
  // nothing meaningful outside). Same technique as Aetherphone's
  // VenueSyncHouseDetector - reconstructs the world+territoryType+ward+plot+room
  // address the game's own indoor house ID already encodes, so a patron
  // standing outside your door and one standing inside resolve to the same
  // venue identity instead of being tracked as two different places.
  public static unsafe class HouseIdentity
  {
    private const sbyte NoPlotSentinel = -1;
    private const sbyte ApartmentMainDivisionSentinel = -128;
    private const sbyte ApartmentSubdivisionSentinel = -127;

    // Returns null when the player is not at a housing plot at all (neither
    // inside nor outside on a valid plot) - callers treat that the same as
    // "not tracking" today.
    public static long? Current(HousingManager* housingManager, uint worldId)
    {
      if (housingManager == null) return null;
      if (!(housingManager->IsInside() || housingManager->IsOutside())) return null;

      var plotIndex = housingManager->GetCurrentPlot();
      if (plotIndex == NoPlotSentinel) return null;

      var wardIndex = housingManager->GetCurrentWard();
      var room = housingManager->GetCurrentRoom();
      var territoryTypeId = HousingManager.GetOriginalHouseTerritoryTypeId();

      return Build(worldId, territoryTypeId, wardIndex, plotIndex, room);
    }

    public static long Build(uint worldId, uint territoryTypeId, int wardIndex, int plotIndex, int room)
    {
      byte unit;
      if (plotIndex is ApartmentMainDivisionSentinel or ApartmentSubdivisionSentinel)
      {
        var division = plotIndex == ApartmentMainDivisionSentinel ? 0 : 1;
        unit = (byte)(0x80 | (division & 0x7F));
      }
      else
      {
        unit = (byte)(plotIndex & 0x7F);
      }

      var id = (ulong)unit;
      var data2 = (ushort)((wardIndex & 0x3F) | ((room & 0x3FF) << 6));
      id |= (ulong)data2 << 16;
      id |= (ulong)(ushort)territoryTypeId << 32;
      id |= (ulong)(ushort)worldId << 48;
      return (long)id;
    }
  }
}
