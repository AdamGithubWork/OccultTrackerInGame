
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PersistentPotsTracker.Windows;

public class PotOverlayWindow : Window
{
    private readonly Plugin plugin;

    public PotOverlayWindow(Plugin plugin)
        : base("Pot Overlay###PersistentPotsOverlay")
    {
        this.plugin = plugin;

        Flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoFocusOnAppearing;

        IsOpen = true;
        RespectCloseHotkey = false;
    }

    public override void Draw()
    {
        string datacenter = plugin.GetCurrentDatacenter();

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var nextPot = plugin.PotRows
            .Where(r =>
                r.Datacenter == datacenter &&
                r.NextSpawnTime > now)
            .OrderBy(r => r.NextSpawnTime)
            .FirstOrDefault();

        string label;
        string tooltip;

        if (datacenter == "Unknown")
        {
            label = "Pots: Datacenter unknown";
            tooltip = "Waiting for current datacenter information";
        }
        else if (nextPot == null)
        {
            label = $"{datacenter} Pots: --:--";
            tooltip = "No upcoming Pot prediction available";
        }
        else
        {
            long remaining = nextPot.NextSpawnTime - now;
            var duration = TimeSpan.FromSeconds(remaining);

            string countdown = duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}"
                : $"{duration.Minutes:D2}:{duration.Seconds:D2}";

            string zone = nextPot.Zone switch
            {
                "South Horn" => "South",
                "North Horn" => "North",
                _ => "Unknown"
            };

            label = $"{datacenter} | {zone}: {countdown}";

            tooltip =
                $"Next predicted Pot on {datacenter}\n" +
                $"Zone: {nextPot.Zone}\n" +
                $"Pot location: {nextPot.NextPot}\n" +
                $"Countdown: {countdown}\n\n" +
                "Click to open Persistent Pots Tracker";
        }

        if (ImGui.Button(label))
        {
            plugin.OpenTrackerWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }
}
