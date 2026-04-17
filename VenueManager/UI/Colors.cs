using System.Numerics;

namespace VenueManager.UI;

public static class Colors {
  public static readonly Vector4 Green = new Vector4(0,1,0,1);
  public static readonly Vector4 White = new Vector4(1,1,1,1);
  public static readonly Vector4 HalfWhite = new Vector4(.5f,.5f,.5f,1);

  // Colors for different entry counts
  public static readonly Vector4 PlayerEntry2 = new Vector4(.92f,.7f,.35f,1);
  public static readonly Vector4 PlayerEntry3 = new Vector4(.97f,.47f,.1f,1);
  public static readonly Vector4 PlayerEntry4 = new Vector4(.89f,0.01f,0,1);

  public static readonly Vector4 PlayerBlue = new Vector4(.25f,0.65f,0.89f,1);

  // -- Catppuccin Mocha palette ---------------------------------------------
  // Values taken directly from catppuccin.com/palette so the plugin + website
  // share one source of truth. All tabs and widgets read from these.
  public static readonly Vector4 CatBase     = new Vector4(0.117f, 0.117f, 0.180f, 1f); // #1e1e2e
  public static readonly Vector4 CatMantle   = new Vector4(0.094f, 0.094f, 0.145f, 1f); // #181825
  public static readonly Vector4 CatCrust    = new Vector4(0.067f, 0.067f, 0.106f, 1f); // #11111b
  public static readonly Vector4 CatSurface0 = new Vector4(0.192f, 0.196f, 0.267f, 1f); // #313244
  public static readonly Vector4 CatSurface1 = new Vector4(0.271f, 0.278f, 0.353f, 1f); // #45475a
  public static readonly Vector4 CatSurface2 = new Vector4(0.345f, 0.357f, 0.439f, 1f); // #585b70
  public static readonly Vector4 CatOverlay0 = new Vector4(0.424f, 0.439f, 0.525f, 1f); // #6c7086
  public static readonly Vector4 CatOverlay1 = new Vector4(0.498f, 0.514f, 0.592f, 1f); // #7f849c
  public static readonly Vector4 CatText     = new Vector4(0.804f, 0.839f, 0.957f, 1f); // #cdd6f4
  public static readonly Vector4 CatSubtext0 = new Vector4(0.651f, 0.678f, 0.784f, 1f); // #a6adc8
  public static readonly Vector4 CatBlue     = new Vector4(0.537f, 0.706f, 0.980f, 1f); // #89b4fa — primary accent
  public static readonly Vector4 CatLavender = new Vector4(0.706f, 0.745f, 0.996f, 1f); // #b4befe
  public static readonly Vector4 CatSapphire = new Vector4(0.455f, 0.780f, 0.925f, 1f); // #74c7ec
  public static readonly Vector4 CatTeal     = new Vector4(0.580f, 0.886f, 0.835f, 1f); // #94e2d5
  public static readonly Vector4 CatCatGreen = new Vector4(0.651f, 0.890f, 0.631f, 1f); // #a6e3a1
  public static readonly Vector4 CatYellow   = new Vector4(0.976f, 0.886f, 0.686f, 1f); // #f9e2af
  public static readonly Vector4 CatPeach    = new Vector4(0.980f, 0.702f, 0.529f, 1f); // #fab387
  public static readonly Vector4 CatRed      = new Vector4(0.953f, 0.545f, 0.659f, 1f); // #f38ba8
  public static readonly Vector4 CatMauve    = new Vector4(0.796f, 0.651f, 0.969f, 1f); // #cba6f7

  // -- Shared status colors -------------------------------------------------
  // Any tab that needs "operation succeeded / warning / error" feedback reads
  // these. Replaces the ~5 local Vector4s that drifted across SettingsTab,
  // ShiftsTab, SalesTab with inconsistent RGB values.
  public static readonly Vector4 StatusOk   = CatCatGreen;
  public static readonly Vector4 StatusWarn = CatYellow;
  public static readonly Vector4 StatusErr  = CatRed;
  public static readonly Vector4 StripMuted = CatOverlay0;

  public static ushort getChatColor(Player player, bool nameOnly) {
    if (nameOnly) {
      if (player.isFriend) return 526; // Blue 
    }

    if (player.entryCount == 1)
      return 060; // Green. `/xldata` -> UIColor in chat in game 
    else if (player.entryCount == 2)
      return 063;
    else if (player.entryCount == 3)
      return 500;
    else if (player.entryCount >= 4)
      return 518;

    return 003; // default 
  }

  public static Vector4 getGuestListColor(Player player, bool nameOnly) {
    if (nameOnly) {
      if (player.isFriend) return PlayerBlue; // Blue 
    }

    if (player.entryCount == 2)
      return PlayerEntry2;
    else if (player.entryCount == 3)
      return PlayerEntry3;
    else if (player.entryCount >= 4)
      return PlayerEntry4;

    return White; // default 
  }
}