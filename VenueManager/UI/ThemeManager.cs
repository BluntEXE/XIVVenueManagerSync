using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace VenueManager.UI;

// XIV dark-navy theme applied to every window the plugin opens.
//
// Usage (window-level scope):
//     using var _ = ThemeManager.Scope();
//     // draw all widgets
//
// Usage (primary action button):
//     using var _ = ThemeManager.PrimaryButton();
//     if (ImGui.Button("Log Sale")) { ... }
//
// The Scoped handle tracks push counts and pops exactly that many on
// Dispose — ImGui's style stack is process-global across Dalamud plugins,
// so a leaked push bleeds into every later window drawn that frame.
public static class ThemeManager
{
    public static Scoped Scope()
    {
        var h = new Scoped();

        // -- Style vars -------------------------------------------------
        h.PushVar(ImGuiStyleVar.WindowPadding,    new Vector2(8f, 8f));
        h.PushVar(ImGuiStyleVar.FramePadding,     new Vector2(7f, 4f));
        h.PushVar(ImGuiStyleVar.CellPadding,      new Vector2(5f, 4f));
        h.PushVar(ImGuiStyleVar.ItemSpacing,      new Vector2(6f, 5f));
        h.PushVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(5f, 4f));

        h.PushVar(ImGuiStyleVar.IndentSpacing,   21f);
        h.PushVar(ImGuiStyleVar.ScrollbarSize,   10f);
        h.PushVar(ImGuiStyleVar.GrabMinSize,     20f);

        h.PushVar(ImGuiStyleVar.WindowBorderSize,  1f);
        h.PushVar(ImGuiStyleVar.ChildBorderSize,   1f);   // card-feel borders
        h.PushVar(ImGuiStyleVar.PopupBorderSize,   1f);
        h.PushVar(ImGuiStyleVar.FrameBorderSize,   0f);

        h.PushVar(ImGuiStyleVar.WindowRounding,    6f);
        h.PushVar(ImGuiStyleVar.ChildRounding,     4f * ImGuiHelpers.GlobalScale);
        h.PushVar(ImGuiStyleVar.FrameRounding,     4f);
        h.PushVar(ImGuiStyleVar.PopupRounding,     4f);
        h.PushVar(ImGuiStyleVar.ScrollbarRounding, 4f);
        h.PushVar(ImGuiStyleVar.GrabRounding,      4f * ImGuiHelpers.GlobalScale);
        h.PushVar(ImGuiStyleVar.TabRounding,       4f);

        // -- Window -------------------------------------------------------
        h.PushColor(ImGuiCol.WindowBg,  Colors.XivBase);
        h.PushColor(ImGuiCol.ChildBg,   Colors.XivMantle);
        h.PushColor(ImGuiCol.PopupBg,   Colors.XivCrust);
        h.PushColor(ImGuiCol.MenuBarBg, Colors.XivMantle);

        // Title bar — slightly distinct from window body
        h.PushColor(ImGuiCol.TitleBg,          Colors.XivCrust);
        h.PushColor(ImGuiCol.TitleBgActive,     Colors.XivMantle);
        h.PushColor(ImGuiCol.TitleBgCollapsed,  Colors.XivCrust);

        // -- Text ---------------------------------------------------------
        h.PushColor(ImGuiCol.Text,         Colors.XivText);
        h.PushColor(ImGuiCol.TextDisabled, Colors.XivOverlay0);

        // -- Borders / separators -----------------------------------------
        h.PushColor(ImGuiCol.Border,             Colors.XivSurface0);
        h.PushColor(ImGuiCol.BorderShadow,        new Vector4(0f, 0f, 0f, 0.35f));
        h.PushColor(ImGuiCol.Separator,           WithAlpha(Colors.XivOverlay0, 0.50f));
        h.PushColor(ImGuiCol.SeparatorHovered,    Colors.XivSurface1);
        h.PushColor(ImGuiCol.SeparatorActive,     Colors.XivBlue);

        // -- Input frames -------------------------------------------------
        h.PushColor(ImGuiCol.FrameBg,        Colors.XivSurface0);
        h.PushColor(ImGuiCol.FrameBgHovered, Colors.XivSurface1);
        h.PushColor(ImGuiCol.FrameBgActive,  Colors.XivSurface2);

        // -- Buttons — neutral base, accent on hover, full blue on active --
        h.PushColor(ImGuiCol.Button,        Colors.XivSurface0);
        h.PushColor(ImGuiCol.ButtonHovered, Colors.XivSurface2);
        h.PushColor(ImGuiCol.ButtonActive,  Colors.XivBlue);

        // -- Selectables / tree headers -----------------------------------
        h.PushColor(ImGuiCol.Header,        Colors.XivSurface0);
        h.PushColor(ImGuiCol.HeaderHovered, Colors.XivSurface1);
        h.PushColor(ImGuiCol.HeaderActive,  Colors.XivBlue);

        // -- Tabs — dark base, hover lightens, active = full XIV blue ----
        h.PushColor(ImGuiCol.Tab,                Colors.XivMantle);
        h.PushColor(ImGuiCol.TabHovered,         Colors.XivSurface1);
        h.PushColor(ImGuiCol.TabActive,          Colors.XivBlue);
        h.PushColor(ImGuiCol.TabUnfocused,       Colors.XivMantle);
        h.PushColor(ImGuiCol.TabUnfocusedActive, Colors.XivSurface0);

        // -- Interactive controls -----------------------------------------
        h.PushColor(ImGuiCol.CheckMark,        Colors.XivBlue);
        h.PushColor(ImGuiCol.SliderGrab,        Colors.XivBlueDim);
        h.PushColor(ImGuiCol.SliderGrabActive,  Colors.XivBlue);

        // -- Scrollbar — transparent bg, subtle grab ---------------------
        h.PushColor(ImGuiCol.ScrollbarBg,          new Vector4(0f, 0f, 0f, 0f)); // invisible bg
        h.PushColor(ImGuiCol.ScrollbarGrab,        Colors.XivSurface1);
        h.PushColor(ImGuiCol.ScrollbarGrabHovered, Colors.XivSurface2);
        h.PushColor(ImGuiCol.ScrollbarGrabActive,  Colors.XivOverlay0);

        // -- Resize grip — invisible (not needed with border) -------------
        h.PushColor(ImGuiCol.ResizeGrip,        new Vector4(0f, 0f, 0f, 0f));
        h.PushColor(ImGuiCol.ResizeGripHovered, new Vector4(0f, 0f, 0f, 0f));
        h.PushColor(ImGuiCol.ResizeGripActive,  Colors.XivBlue);

        // -- Tables -------------------------------------------------------
        h.PushColor(ImGuiCol.TableHeaderBg,      Colors.XivSurface0);
        h.PushColor(ImGuiCol.TableBorderStrong,  Colors.XivSurface1);
        h.PushColor(ImGuiCol.TableBorderLight,   Colors.XivSurface0);
        h.PushColor(ImGuiCol.TableRowBg,         new Vector4(0f, 0f, 0f, 0f));
        h.PushColor(ImGuiCol.TableRowBgAlt,      new Vector4(1f, 1f, 1f, 0.04f)); // very subtle alternate row

        // -- Misc ---------------------------------------------------------
        h.PushColor(ImGuiCol.TextSelectedBg,     WithAlpha(Colors.XivBlue, 0.35f));
        h.PushColor(ImGuiCol.DragDropTarget,     Colors.XivBlue);
        h.PushColor(ImGuiCol.NavHighlight,       WithAlpha(Colors.XivBlue, 0.70f));
        h.PushColor(ImGuiCol.NavWindowingDimBg,  new Vector4(0f, 0f, 0f, 0.45f));

        return h;
    }

    // Push XIV-blue styling for primary action buttons (Log Sale, Clock In/Out).
    // Usage: using var _ = ThemeManager.PrimaryButton();
    public static Scoped PrimaryButton()
    {
        var h = new Scoped();
        h.PushColor(ImGuiCol.Button,        Colors.XivBlueDim);
        h.PushColor(ImGuiCol.ButtonHovered, Colors.XivBlue);
        h.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.20f, 0.85f, 1.00f, 1f));
        h.PushColor(ImGuiCol.Text,          Colors.XivBase); // dark text on bright button
        return h;
    }

    // Draw a styled XIV section header: colored label + separator.
    // Replaces bare TextColored + Separator pairs throughout tabs.
    public static void SectionHeader(string label, Vector4? color = null)
    {
        ImGui.TextColored(color ?? Colors.XivBlue, label);
        ImGui.Separator();
    }

    // Styled "not configured / action required" banner.
    // Use for gate messages at the top of tabs (no API key, no venue, etc.).
    public static void ConfigBanner(string message)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithAlpha(Colors.XivSurface0, 0.55f));
        var lineH  = ImGui.GetTextLineHeightWithSpacing();
        var height = lineH * 2f + 12f;
        ImGui.BeginChild("##configbanner", new Vector2(-1, height), true);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f);
        ImGui.TextColored(Colors.XivYellow, "  ");
        ImGui.SameLine();
        ImGui.TextWrapped(message);
        ImGui.EndChild();
        ImGui.Spacing();
    }

    // Centered muted placeholder for empty list states.
    public static void EmptyState(string message)
    {
        var avail = ImGui.GetContentRegionAvail();
        var textW = ImGui.CalcTextSize(message).X;
        ImGui.Spacing();
        ImGui.Spacing();
        float xOffset = (avail.X - textW) * 0.5f;
        if (xOffset > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + xOffset);
        ImGui.TextColored(Colors.XivOverlay0, message);
    }

    // -------------------------------------------------------------------------

    private static Vector4 WithAlpha(Vector4 c, float a) => c with { W = a };

    public struct Scoped : IDisposable
    {
        private int _colors;
        private int _vars;

        internal void PushColor(ImGuiCol idx, Vector4 color)
        {
            ImGui.PushStyleColor(idx, color);
            _colors++;
        }

        internal void PushVar(ImGuiStyleVar idx, float value)
        {
            ImGui.PushStyleVar(idx, value);
            _vars++;
        }

        internal void PushVar(ImGuiStyleVar idx, Vector2 value)
        {
            ImGui.PushStyleVar(idx, value);
            _vars++;
        }

        public void Dispose()
        {
            if (_colors > 0) ImGui.PopStyleColor(_colors);
            if (_vars > 0)   ImGui.PopStyleVar(_vars);
            _colors = 0;
            _vars   = 0;
        }
    }
}
