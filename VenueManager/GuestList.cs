using System;
using System.Collections.Generic;

namespace VenueManager
{
  [Serializable]
  public class GuestList
  {
    private static readonly string OutputFile = "guests.json";

    // List of guests in the venue
    public Dictionary<string, Player> guests { get; set; } = new();
    // FF House Id. House id 0 is unsaved house.
    public long houseId { get; set; } = 0;
    public Venue venue { get; set; } = new();
    public DateTime startTime { get; set; } = DateTime.Now;

    // True for one framework-update pass immediately after load(). Guests
    // restored from disk as inHouse=true whose real departure happened in a
    // prior session (plugin restart, crash, game restart) must not fire a
    // "leave" sync to the server the moment tracking resumes - we have no
    // real timestamp for when they actually left, and doing so orphans a
    // LEAVE with no matching ENTER, permanently skewing venue patron counts.
    public bool justLoaded { get; set; } = false;

    public GuestList()
    {
    }

    public GuestList(long id, Venue venue)
    {
      this.houseId = id;
      this.venue = new Venue(venue);
    }

    public GuestList(GuestList list)
    {
      this.guests = list.guests;
      this.houseId = list.houseId;
      this.venue = list.venue;
      this.startTime = list.startTime;
    }

    private string getFileName()
    {
      return houseId + "-" + OutputFile;
    }

    public void save()
    {
      // Save not supported for default guest list
      if (this.houseId == 0) return;

      FileStore.SaveClassToFileInPluginDir(getFileName(), this.GetType(), this);
    }

    public void load()
    {
      // Load not supported for default guest list
      if (this.houseId == 0) return;

      // Don't attempt to load if there is no file
      var fileInfo = FileStore.GetFileInfo(getFileName());
      if (!fileInfo.Exists) return;

      GuestList loadedData = FileStore.LoadFile<GuestList>(getFileName(), this);
      this.guests = loadedData.guests;
      this.houseId = loadedData.houseId;
      this.startTime = loadedData.startTime;
      // Don't replace venue if the incoming one is blank
      if (loadedData.venue.name.Length != 0)
        this.venue = loadedData.venue;
      this.justLoaded = true;
    }
  }
}
