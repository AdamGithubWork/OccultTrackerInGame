# Persistent Pots Tracker

A Dalamud plugin for Final Fantasy XIV that tracks Persistent Pots and related FATEs in Occult Crescent.

Persistent Pots Tracker uses publicly available data from FFXIVTracker to display estimated pot spawn timers directly in-game, with datacenter filtering, an optional overlay, and experimental automatic instance matching.

## Features

- **Pot Timers:** View estimated upcoming pot spawns in South Horn and North Horn.
- **xivstats Integration:** Retrieves tracker information from the public xivstats API.
- **Experimental Instance Matching:** Attempts to identify your current instance by comparing active FATEs and Critical Engagements with tracker data. Matches may occasionally be incorrect.
- **Instance Highlighting:** Highlights the identified instance in the tracker list.
- **Datacenter Filtering:** Choose which datacenters to display, including Aether, Primal, Crystal, and Dynamis.
- **Automatic Refreshing:** Updates tracker information periodically and when entering a supported area.
- **Manual Refresh:** Refresh tracker information directly from the plugin window.
- **Overlay:** View upcoming pot information without keeping the main tracker window open.
- **Sortable Tracker Table:** Sort available instances by datacenter, zone, FATE information, upcoming pot, or countdown.

## Installation

Persistent Pots Tracker is a third-party Dalamud plugin.

Installation instructions will be provided when a compatible plugin repository or distribution method is available.

Source code is available in this repository.

## Usage

Use the following command in-game to open the plugin:

`/pots`

The main window displays available tracker instances, pot information, and estimated spawn timers.

### Tracker Window

The main tracker window provides:

- Available tracker instances across selected datacenters.
- North Horn and South Horn information.
- Last or currently reported FATE and Critical Engagement.
- Predicted next pot location and countdown.
- Sortable columns and datacenter filters.
- Manual refresh controls.

Tracker information refreshes automatically every 45 seconds while the main window is open, with additional refreshes triggered by certain in-game events.

### Overlay

The overlay displays the next upcoming predicted pot for your current datacenter.

It includes:

- Current datacenter.
- Horn location.
- Countdown to the next predicted pot.

Hover over the overlay for additional information, or click it to open the main tracker window.

### Automatic Instance Matching (Experimental)

FFXIVTracker assigns its own identifiers to tracker instances. These identifiers are not directly available through the game's standard plugin APIs.

Instead, Persistent Pots Tracker compares available in-game FATE and Critical Engagement information with the data reported for each tracker instance.

When the matching algorithm identifies a unique candidate, the corresponding tracker entry is highlighted.

Because matching depends on externally reported activity and the timing of in-game events, it may not identify an instance immediately.

**Important:** Automatic instance matching is experimental. A unique candidate is not necessarily the correct instance, and incorrect matches can occur. Verify highlighted instances against FFXIVTracker when accuracy matters.

## Supported Areas

### Occult Crescent: South Horn

- Persistent Pots
- Pleading Pots

### Occult Crescent: North Horn

- Daylight Pottery
- In a Pot of Bother

## Important Notes

- Pot spawn times are estimates based on externally reported tracker information.
- Tracker information may be delayed, incomplete, or outdated.
- Automatic instance matching is experimental and may identify an incorrect instance.
- The plugin may not identify an instance immediately or at all.
- Network or API errors may temporarily prevent tracker information from updating.
- The plugin uses XIVStats' public API and is not affiliated with XIVStats.
- This plugin is an independent community project and is not affiliated with Square Enix.

## Credits

Tracker data is provided by the public [FFXIVTracker](https://tracker.xivstats.com/) service.

Built using [Dalamud](https://github.com/goatcorp/Dalamud) for Final Fantasy XIV.
