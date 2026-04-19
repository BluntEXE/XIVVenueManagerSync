using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace VenueManager.UI;

// Catppuccin Mocha theme for every window the plugin opens.
//
// Usage:
//     using var _ = ThemeManager.Scope();
//     // draw widgets
//
// The Scope handle counts the colors/vars it pushed and pops exactly that
// many on Dispose, so Push/Pop counts can never drift. ImGui's style stack
// is process-global across every Dalamud plugin in the frame — a leaked
// push bleeds colors into every later window drawn that frame.
public static class ThemeManager
{
  public static Scoped Scope()
  {
    var handle = new Scoped();

    handle.PushVar(ImGuiStyleVar.WindowRounding, 8f);
    handle.PushVar(ImGuiStyleVar.ChildRounding,  6f);
    handle.PushVar(ImGuiStyleVar.FrameRounding,  6f);
    handle.PushVar(ImGuiStyleVar.PopupRounding,  6f);
    handle.PushVar(ImGuiStyleVar.GrabRounding,   4f);
    handle.PushVar(ImGuiStyleVar.TabRounding,    6f);
    handle.PushVar(ImGuiStyleVar.FramePadding,   new Vector2(8f, 5f));

    handle.PushColor(ImGuiCol.WindowBg,  Colors.CatBase);
    handle.PushColor(ImGuiCol.ChildBg,   Colors.CatMantle);
    handle.PushColor(ImGuiCol.PopupBg,   Colors.CatCrust);
    handle.PushColor(ImGuiCol.MenuBarBg, Colors.CatMantle);

    handle.PushColor(ImGuiCol.Text,         Colors.CatText);
    handle.PushColor(ImGuiCol.TextDisabled, Colors.CatOverlay0);

    handle.PushColor(ImGuiCol.Border,          Colors.CatSurface0);
    handle.PushColor(ImGuiCol.Separator,       Colors.CatSurface0);
    handle.PushColor(ImGuiCol.SeparatorHovered, Colors.CatSurface2);
    handle.PushColor(ImGuiCol.SeparatorActive,  Colors.CatBlue);

    handle.PushColor(ImGuiCol.FrameBg,        Colors.CatSurface0);
    handle.PushColor(ImGuiCol.FrameBgHovered, Colors.CatSurface1);
    handle.PushColor(ImGuiCol.FrameBgActive,  Colors.CatSurface2);

    handle.PushColor(ImGuiCol.Button,        Colors.CatSurface1);
    handle.PushColor(ImGuiCol.ButtonHovered, Colors.CatSurface2);
    handle.PushColor(ImGuiCol.ButtonActive,  Colors.CatBlue);

    handle.PushColor(ImGuiCol.Header,        Colors.CatSurface1);
    handle.PushColor(ImGuiCol.HeaderHovered, Colors.CatSurface2);
    handle.PushColor(ImGuiCol.HeaderActive,  Colors.CatBlue);

    handle.PushColor(ImGuiCol.Tab,                Colors.CatMantle);
    handle.PushColor(ImGuiCol.TabHovered,         Colors.CatSurface1);
    handle.PushColor(ImGuiCol.TabActive,          Colors.CatSurface0);
    handle.PushColor(ImGuiCol.TabUnfocused,       Colors.CatMantle);
    handle.PushColor(ImGuiCol.TabUnfocusedActive, Colors.CatSurface0);

    handle.PushColor(ImGuiCol.CheckMark,        Colors.CatBlue);
    handle.PushColor(ImGuiCol.SliderGrab,       Colors.CatBlue);
    handle.PushColor(ImGuiCol.SliderGrabActive, Colors.CatLavender);

    handle.PushColor(ImGuiCol.ScrollbarBg,          Colors.CatMantle);
    handle.PushColor(ImGuiCol.ScrollbarGrab,        Colors.CatSurface1);
    handle.PushColor(ImGuiCol.ScrollbarGrabHovered, Colors.CatSurface2);
    handle.PushColor(ImGuiCol.ScrollbarGrabActive,  Colors.CatOverlay0);

    return handle;
  }

  public struct Scoped : IDisposable
  {
    private int colors;
    private int vars;

    internal void PushColor(ImGuiCol idx, Vector4 color)
    {
      ImGui.PushStyleColor(idx, color);
      colors++;
    }

    internal void PushVar(ImGuiStyleVar idx, float value)
    {
      ImGui.PushStyleVar(idx, value);
      vars++;
    }

    internal void PushVar(ImGuiStyleVar idx, Vector2 value)
    {
      ImGui.PushStyleVar(idx, value);
      vars++;
    }

    public void Dispose()
    {
      if (colors > 0) ImGui.PopStyleColor(colors);
      if (vars > 0)   ImGui.PopStyleVar(vars);
      colors = 0;
      vars = 0;
    }
  }
}
