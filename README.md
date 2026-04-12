# XIV Venue Manager Sync

A Dalamud plugin for FFXIV that syncs patron visits, sales, and shift tracking with [XIVVenueManager](https://xivvenuemanager.com).

## Features

- **Patron Tracking** — Automatically detect and log patron enter/leave events with chat alerts and doorbell sounds
- **Sales Logging** — Log transactions via in-game UI or slash commands, with session tallies on the dashboard strip
- **Shift Management** — Clock in/out of scheduled shifts directly from chat commands
- **Venue Management** — Save and manage multiple venues with per-venue guest lists
- **XIV-App Integration** — Full sync with your XIVVenueManager server (venues, roles, services, payroll)

## Tabs

| Tab | Description |
|-----|-------------|
| **Guests** | Live patron list with entry count, time tracking, and friend status |
| **Venues** | Saved venue list with notes |
| **Sales** | Log transactions with service selection, customer name, and amount |
| **Shifts** | View and manage your scheduled/active shifts |
| **Settings** | API key, server URL, venue selection, alert preferences |

## Commands

| Command | Description |
|---------|-------------|
| `/venue` or `/vm` | Open the main plugin window |
| `/vm snooze` | Pause alerts until leaving the current house |
| `/vm sale [amount] [customer]` | Open Sales tab with optional prefill |
| `/vm sale! <amount> [customer]` | Log a sale instantly without opening UI |
| `/vm target [amount]` | Open Sales tab with current target as customer |
| `/vm target! <amount>` | Log a sale for current target without opening UI |
| `/vm start` | Clock into your next scheduled shift |
| `/vm end` | Clock out of your active shift |

## Installation

Add the custom plugin repository to Dalamud, then search for **XIVVenueManagerSync**.

## Configuration

1. Open plugin settings (`/vm` → Settings tab)
2. Enter your XIVVenueManager API key
3. Enter the server URL (e.g. `http://192.168.1.122:3000`)
4. Select which venue to log to
5. Enable sync

## Credits

Plugin by **Ehno** · Built on the Dalamud framework
