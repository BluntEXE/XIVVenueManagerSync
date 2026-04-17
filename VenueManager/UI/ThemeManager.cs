using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace VenueManager.UI;

// Catppuccin Mocha theme for every window the plugin opens.
//
// Call `ThemeManager.Push()` at the top of a window's Draw() and
// `ThemeManager.Pop()` in a finally{} block at the bottom. Every child
// window, tab, combo, button, slider, checkbox, etc. drawn between those
// two calls inherits the Catppuccin palette.
//
// Why push/pop instead of SetStyleColor? ImGui style is global state. If
// we mutated the shared style we'd bleed colors into other plugins drawn
// after ours in the same frame. Scoped Push/Pop leaves the global style
// untouched.
public static class ThemeManager
{
  // Number of colors pushed by Push(). Must match the number of Pop calls.
  // Kept here so a future edit can't silently desync the two methods.
  private const int ColorCount = 30;
  private const int StyleVarCount = 7;

  public static void Push()
  {
    // -- Frame radius + spacing ---------------------------------------------
    // Website uses 6–8px rounded corners and generous padding. ImGui units
    // are pixels; these match the --radius and --spacing-* tokens roughly.
    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
    ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
    ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f);
    ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
    ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 6f);
    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 5f));

    // -- Backgrounds --------------------------------------------------------
    ImGui.PushStyleColor(ImGuiCol.WindowBg,   Colors.CatBase);
    ImGui.PushStyleColor(ImGuiCol.ChildBg,    Colors.CatMantle);
    ImGui.PushStyleColor(ImGuiCol.PopupBg,    Colors.CatCrust);
    ImGui.PushStyleColor(ImGuiCol.MenuBarBg,  Colors.CatMantle);

    // -- Text ---------------------------------------------------------------
    ImGui.PushStyleColor(ImGuiCol.Text,         Colors.CatText);
    ImGui.PushStyleColor(ImGuiCol.TextDisabled, Colors.CatOverlay0);

    // -- Borders + separators ----------------------------------------------
    ImGui.PushStyleColor(ImGuiCol.Border,         Colors.CatSurface0);
    ImGui.PushStyleColor(ImGuiCol.Separator,      Colors.CatSurface0);
    ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, Colors.CatSurface2);
    ImGui.PushStyleColor(ImGuiCol.SeparatorActive,  Colors.CatBlue);

    // -- Inputs / frames (checkbox, input, combo, slider track) ------------
    ImGui.PushStyleColor(ImGuiCol.FrameBg,        Colors.CatSurface0);
    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Colors.CatSurface1);
    ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  Colors.CatSurface2);

    // -- Buttons ------------------------------------------------------------
    ImGui.PushStyleColor(ImGuiCol.Button,        Colors.CatSurface1);
    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Colors.CatSurface2);
    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  Colors.CatBlue);

    // -- Headers (collapsing header, selected row, combo item) -------------
    ImGui.PushStyleColor(ImGuiCol.Header,        Colors.CatSurface1);
    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Colors.CatSurface2);
    ImGui.PushStyleColor(ImGuiCol.HeaderActive,  Colors.CatBlue);

    // -- Tabs ---------------------------------------------------------------
    ImGui.PushStyleColor(ImGuiCol.Tab,                Colors.CatMantle);
    ImGui.PushStyleColor(ImGuiCol.TabHovered,         Colors.CatSurface1);
    ImGui.PushStyleColor(ImGuiCol.TabActive,          Colors.CatSurface0);
    ImGui.PushStyleColor(ImGuiCol.TabUnfocused,       Colors.CatMantle);
    ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, Colors.CatSurface0);

    // -- Check mark + slider grab ------------------------------------------
    ImGui.PushStyleColor(ImGuiCol.CheckMark,        Colors.CatBlue);
    ImGui.PushStyleColor(ImGuiCol.SliderGrab,       Colors.CatBlue);
    ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Colors.CatLavender);

    // -- Scrollbar ----------------------------------------------------------
    ImGui.PushStyleColor(ImGuiCol.ScrollbarBg,          Colors.CatMantle);
    ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab,        Colors.CatSurface1);
    ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Colors.CatSurface2);
    ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive,  Colors.CatOverlay0);
  }

  public static void Pop()
  {
    ImGui.PopStyleColor(ColorCount);
    ImGui.PopStyleVar(StyleVarCount);
  }
}
