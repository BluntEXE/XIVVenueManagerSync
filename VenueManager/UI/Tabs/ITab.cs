using Dalamud.Interface;

namespace VenueManager.Tabs;

// Shared contract for MainWindow's sidebar tabs. Kept intentionally small —
// tabs with one-off behavior (SettingsTab's pinned nav position, SalesTab's
// Prefill()) stay special-cased in MainWindow rather than growing this
// interface for a single caller.
public interface ITab
{
    string Name { get; }
    FontAwesomeIcon Icon { get; }
    string Tooltip { get; }
    bool IsVisible { get; }
    void draw();
}
