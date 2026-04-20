using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace VenueManager
{
  public enum DtrDisplayMode
  {
    Disabled,
    PatronCount,
    VenueName,
    SessionSales,
    Combined,
  }

  [Serializable]
  public class Configuration : IPluginConfiguration
  {
    public int Version { get; set; } = 0;

    // Should chat message alerts be printed to the chat
    public bool showChatAlerts { get; set; } = false;
    public bool showChatAlertEntry { get; set; } = true;
    public bool showChatAlertReentry { get; set; } = true;
    public bool showChatAlertLeave { get; set; } = true;
    public bool showChatAlertAlreadyHere { get; set; } = false;
    public bool showPluginNameInChat { get; set; } = false;

    // Should sound alerts be played when new players join the house
    public bool soundAlerts { get; set; } = false;
    public float soundVolume { get; set; } = 1;
    // User selection for doorbell type
    public DOORBELL_TYPE doorbellType { get; set; } = DOORBELL_TYPE.DOORBELL;

    // Tab visibiliy options
    public bool showGuestsTab { get; set; } = true;
    public bool showVenueTab { get; set; } = true;

    public bool sortFriendsToTop { get; set; } = true;
    public bool sortCurrentVisitorsTop { get; set; } = true;

    // XIV-App API Configuration
    public string xivAppApiKey { get; set; } = "";
    public string xivAppServerUrl { get; set; } = "https://xivvenuemanager.com";
    public string selectedVenueId { get; set; } = "";
    public bool syncToXivApp { get; set; } = false;

    // Maps in-game HouseId → xiv-app venueId so patron visits log to the
    // right venue automatically. Populated from the VenuesTab linking UI.
    public Dictionary<long, string> houseToXivAppVenue { get; set; } = new();

    // Server Info Bar (DTR) display. Surfaces at-a-glance plugin state in
    // the game's world/time strip so users don't need the main window open
    // to see patron count / venue / session sales. User picks the mode
    // from the Settings tab; disabled by default so new installs don't
    // add uninvited UI to a crowded DTR bar.
    public DtrDisplayMode dtrDisplayMode { get; set; } = DtrDisplayMode.Disabled;

    // When true (default), patron visits only sync during an active event
    // at the linked venue. Non-event visits are still tracked locally in
    // the plugin's guest list but aren't posted. Flip off to sync 24/7.
    public bool syncOnlyDuringEvents { get; set; } = true;

    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
      this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
      this.pluginInterface!.SavePluginConfig(this);
    }
  }
}