using System.Numerics;

namespace VenueManager.UI;

public static class Colors {
  // -- Entry-count colors (guest repeat-visit progression) -----------------
  public static readonly Vector4 White        = new Vector4(1f, 1f, 1f, 1f);
  public static readonly Vector4 PlayerEntry2 = new Vector4(0.92f, 0.70f, 0.35f, 1f);
  public static readonly Vector4 PlayerEntry3 = new Vector4(0.97f, 0.47f, 0.10f, 1f);
  public static readonly Vector4 PlayerEntry4 = new Vector4(0.89f, 0.01f, 0.00f, 1f);
  public static readonly Vector4 PlayerBlue   = new Vector4(0.00f, 0.71f, 1.00f, 1f); // XIV blue for friends

  // -- XIV dark-navy palette -----------------------------------------------
  // Derived from the website redesign (May 2026): bg=#070b14, primary=#00b4ff.
  // All tabs and ThemeManager read from these — do not add inline Vector4s elsewhere.
  public static readonly Vector4 XivBase     = new Vector4(0.027f, 0.043f, 0.078f, 1f); // #070b14 — window bg
  public static readonly Vector4 XivMantle   = new Vector4(0.051f, 0.094f, 0.161f, 1f); // #0d1829 — child bg / elevated
  public static readonly Vector4 XivCrust    = new Vector4(0.016f, 0.031f, 0.063f, 1f); // #040810 — popup / deepest
  public static readonly Vector4 XivSurface0 = new Vector4(0.075f, 0.137f, 0.220f, 1f); // #132338 — input frames / borders
  public static readonly Vector4 XivSurface1 = new Vector4(0.102f, 0.227f, 0.361f, 1f); // #1a3a5c — hover state
  public static readonly Vector4 XivSurface2 = new Vector4(0.137f, 0.302f, 0.478f, 1f); // #234d7a — active / pressed
  public static readonly Vector4 XivOverlay0 = new Vector4(0.227f, 0.376f, 0.502f, 1f); // #3a6080 — muted / disabled text
  public static readonly Vector4 XivText     = new Vector4(0.784f, 0.847f, 0.910f, 1f); // #c8d8e8 — primary text
  public static readonly Vector4 XivSubtext0 = new Vector4(0.439f, 0.600f, 0.722f, 1f); // #7099b8 — secondary text
  public static readonly Vector4 XivBlue     = new Vector4(0.000f, 0.706f, 1.000f, 1f); // #00b4ff — PRIMARY ACCENT
  public static readonly Vector4 XivBlueDim  = new Vector4(0.000f, 0.467f, 0.667f, 1f); // #0077aa — dimmed blue (button base)
  public static readonly Vector4 XivGold     = new Vector4(0.941f, 0.753f, 0.376f, 1f); // #f0c060 — gil / important values
  public static readonly Vector4 XivGreen    = new Vector4(0.000f, 0.847f, 0.478f, 1f); // #00d87a — success / on-shift
  public static readonly Vector4 XivRed      = new Vector4(1.000f, 0.302f, 0.302f, 1f); // #ff4d4d — error
  public static readonly Vector4 XivYellow   = new Vector4(0.961f, 0.784f, 0.259f, 1f); // #f5c842 — warning

  // -- Shared status colors ------------------------------------------------
  // Use these everywhere status feedback is shown. Don't inline new Vector4s.
  public static readonly Vector4 StatusOk   = XivGreen;
  public static readonly Vector4 StatusWarn = XivYellow;
  public static readonly Vector4 StatusErr  = XivRed;
  public static readonly Vector4 StripMuted = XivOverlay0;

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
      if (player.isFriend) return PlayerBlue;
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
