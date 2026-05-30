using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using VenueManager.UI;

namespace VenueManager.Windows;

public class ChangelogWindow : Window, IDisposable
{
    private readonly record struct ChangelogEntry(string Version, string Date, string Tagline, bool IsCurrent, string[] Items);

    private static readonly ChangelogEntry[] Entries =
    [
        new("v3.8.0", "May 2026", "Full UI Redesign", IsCurrent: true,
        [
            "Complete visual overhaul — XIV blue design system matching xivvenuemanager.com",
            "Icon sidebar navigation replaces horizontal tab bar",
            "Dark navy background (#070b14), XIV blue (#00b4ff) primary accent",
            "Primary action buttons (Log Sale, Clock In/Out) highlighted in XIV blue",
            "Styled section headers, gate messages, and empty states throughout",
            "Compact spacing and card-feel child regions with subtle borders",
            "Default window size set to 480×580, minimum 320×400",
        ]),

        new("v3.7.5 – v3.7.6", "May 2026", "Auto-Greeter & Dalamud Review Fixes", IsCurrent: false,
        [
            "Re-entry greeter with independent message and toggle",
            "Auto-greeter fires via /tell independently of chat alert settings",
            "Dalamud review pass — all F-01 through F-10 findings resolved",
            "CI: Discord release ping to #announcements on publish",
        ]),

        new("v3.7.0 – v3.7.4", "May 2026", "Auto-Greeter", IsCurrent: false,
        [
            "Auto-greeter: automatically send /tell to patrons on venue entry",
            "Greet on first entry only, configurable message per venue",
            "Fixed command routing to use ProcessChatBoxEntry for /tell dispatch",
        ]),

        new("v3.6.7 – v3.6.9", "May 2026", "Sounds & Onboarding", IsCurrent: false,
        [
            "New sound options: Booba and Hello MF doorbells",
            "First-run onboarding banner with setup instructions",
            "HomeWorld fallback fix for cross-world patrons",
            "Slash commands renamed: /venue → /xvenue, /vm → /xvm",
        ]),

        new("v3.6.4 – v3.6.6", "April 2026", "Sales & Patron Improvements", IsCurrent: false,
        [
            "Sato doorbell sound option for patron entries",
            "Session sales tally displayed in dashboard strip",
            "Patron sync reliability improvements",
        ]),
    ];

    public ChangelogWindow() : base(
        "XIV Venue Manager — Changelog###XIVVMChangelog",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse)
    {
        Size          = new Vector2(540, 620);
        SizeCondition = ImGuiCond.Always;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        ImGui.SetNextWindowPos(
            new Vector2(
                (ImGui.GetIO().DisplaySize.X - Size!.Value.X) * 0.5f,
                (ImGui.GetIO().DisplaySize.Y - Size!.Value.Y) * 0.5f),
            ImGuiCond.Appearing);
        base.PreDraw();
    }

    public override void Draw()
    {
        using var theme = ThemeManager.Scope();

        // Header
        ImGui.TextColored(Colors.XivBlue, "XIV Venue Manager");
        ImGui.SameLine();
        ImGui.TextColored(Colors.XivOverlay0, $"  Changelog");
        ImGui.Separator();
        ImGui.Spacing();

        // Changelog entries
        float listH = Size!.Value.Y - 110f;
        ImGui.BeginChild("##changelog_list", new Vector2(-1, listH), false);

        foreach (var entry in Entries)
        {
            drawEntry(entry);
            ImGui.Spacing();
        }

        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Footer buttons
        float btnW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2) / 3f;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.00f, 0.45f, 0.35f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.00f, 0.60f, 0.47f, 1.00f));
        if (ImGui.Button("Ko-fi", new Vector2(btnW, 0)))
            Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/ehnocure", UseShellExecute = true });
        ImGui.PopStyleColor(2);

        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.05f, 0.25f, 0.45f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.00f, 0.45f, 0.70f, 1.00f));
        if (ImGui.Button("GitHub", new Vector2(btnW, 0)))
            Process.Start(new ProcessStartInfo { FileName = "https://github.com/BluntEXE/XIVVenueManagerSync", UseShellExecute = true });
        ImGui.PopStyleColor(2);

        ImGui.SameLine();

        using (ThemeManager.PrimaryButton())
        {
            if (ImGui.Button("Close", new Vector2(btnW, 0)))
                IsOpen = false;
        }
    }

    private static void drawEntry(ChangelogEntry entry)
    {
        var titleColor  = entry.IsCurrent ? Colors.XivBlue    : Colors.XivSubtext0;
        var tagColor    = entry.IsCurrent ? Colors.XivText     : Colors.XivOverlay0;
        var flags = entry.IsCurrent
            ? ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Framed
            : ImGuiTreeNodeFlags.Framed;

        ImGui.PushStyleColor(ImGuiCol.Text,   titleColor);
        ImGui.PushStyleColor(ImGuiCol.Header, entry.IsCurrent
            ? new Vector4(Colors.XivSurface1.X, Colors.XivSurface1.Y, Colors.XivSurface1.Z, 0.6f)
            : new Vector4(Colors.XivSurface0.X, Colors.XivSurface0.Y, Colors.XivSurface0.Z, 0.4f));

        bool open = ImGui.CollapsingHeader($" {entry.Version}  –  {entry.Date}###{entry.Version}", flags);

        ImGui.PopStyleColor(2);

        ImGui.SameLine();
        ImGui.TextColored(tagColor, $"  {entry.Tagline}");

        if (!open) return;

        ImGui.Indent(12f);
        ImGui.Spacing();
        foreach (var item in entry.Items)
            ImGui.BulletText(item);
        ImGui.Spacing();
        ImGui.Unindent(12f);
    }
}
