# Phantom Patron Detection Fix — Test Plan

## Status
Ready to test — DLL built and clean.

## DLL Location
`/home/ehno/VenueManager/VenueManager/bin/Debug/XIVVenueManagerSync.dll`

## What Changed
- `Plugin.cs`: Added `IsOutsidePlotBounds()` filter in Object Table scan loop
- Filters players >50 yalms from self when standing on plot exterior
- Interior detection unchanged (Object Table already scoped to house instance)
- No changes to API, data models, or other features

## Test Steps

1. Copy DLL to Dalamud dev plugins folder
2. `/xlplugins` → reload VenueManager
3. Link a venue to your current house if not already linked

### Test Cases

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Exterior — friends standing near you | Should appear in guest list |
| 2 | Exterior — players at neighboring plots | Should NOT appear |
| 3 | Interior — all players inside house | Should appear |
| 4 | Boundary — friend at edge of plot | May flicker in/out (threshold test) |

### What to Watch For
- Guest list count matches visible players at your plot
- No phantom entries from players at other plots
- No missing entries from players actually at your plot

### Tuning
- Radius is `2500f` (50² = 50 yalm threshold)
- If too tight: players at plot edges get filtered → increase value
- If too loose: players at adjacent plots still show → decrease value

## Future Work
- Outdoor events outside venue plots (not scoped to `houseId`)
- Would need separate tracking trigger (not tied to `IsInside() || IsOutside()`)

## Other Bugs (On Hold)
- **Roles member count bug**: API counts only primary role, not additional roles. Files: `roles/route.ts`, `staff/roles/page.tsx`
- **CLAIMED shift display**: Plugin UI doesn't render CLAIMED status. File: `ShiftsTab.cs`
