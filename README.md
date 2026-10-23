# Persistent Pots Tracker

A Dalamud plugin for Final Fantasy XIV that tracks Persistent Pots and related FATEs in Occult Crescent.

The plugin uses publicly available data from FFXIVTracker to display pot timers directly in-game and helps identify which tracker instance corresponds to your current instance.

## Features

- **Pot Timers:** View estimated upcoming pot spawns in South Horn and North Horn.
- **FFXIVTracker Integration:** Retrieves tracker information from the public FFXIVTracker API.
- **Automatic Instance Matching:** Attempts to identify your current instance by comparing active FATEs and Critical Engagements with tracker data.
- **Instance Highlighting:** Highlights the matching instance in the tracker list.
- **Datacenter Filtering:** Displays tracker information relevant to your selected datacenter.
- **Automatic Refreshing:** Updates tracker information periodically and when entering a supported area.
- **Manual Refresh:** Refresh tracker information directly from the plugin window.
- **Overlay:** View pot information without keeping the main tracker window open.

## Usage

Use the following command in-game to open the plugin:

`/pots`

The main window displays available tracker instances, pot information, and estimated timers.

When you're inside a supported Occult Crescent area, the plugin attempts to match your current instance automatically. Once a match is found, the corresponding tracker entry is highlighted.

### How Instance Matching Works

FFXIVTracker assigns its own identifiers to tracker instances. These identifiers are not directly available through the game's standard plugin APIs.

Instead, Persistent Pots Tracker compares available in-game FATE and Critical Engagement information with the data reported for each tracker instance.

When a unique match is identified, that instance is highlighted. If the available information isn't sufficient to identify one instance confidently, the plugin waits for additional information rather than selecting an arbitrary match.

Because matching depends on available event data, highlighting may not happen immediately after entering an instance.

## Supported Areas

- **Occult Crescent: South Horn**
  - Persistent Pots
  - Pleading Pots
- **Occult Crescent: North Horn**
  - Daylight Pottery
  - In a Pot of Bother

## Important Notes

- Pot spawn times are estimates based on externally reported tracker information.
- Instance matching depends on the availability and accuracy of FFXIVTracker data and in-game event information.
- The plugin may not be able to identify an instance immediately.
- The plugin uses FFXIVTracker's public API and is not affiliated with the website.
- Network or API errors may temporarily prevent timer information from being displayed.

## Credits

Tracker data is provided by the public [FFXIVTracker](https://ffxivtracker.com/) service.

Built using Dalamud for Final Fantasy XIV.
