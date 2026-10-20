
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace OccultTracker.Windows;

public class MainWindow : Window
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Occult Tracker###OccultTrackerMainWindow")
    {
        this.plugin = plugin;

        Size = new Vector2(950, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public override void Draw()
    {
        DrawDatacenterFilters();

        ImGui.Separator();

        DrawRefreshControls();

        ImGui.Separator();

        DrawPotTable();
    }

    private void DrawDatacenterFilters()
    {
        ImGui.Text("Datacenters");

        bool aether = plugin.Configuration.ShowAether;
        bool primal = plugin.Configuration.ShowPrimal;
        bool crystal = plugin.Configuration.ShowCrystal;
        bool dynamis = plugin.Configuration.ShowDynamis;

        bool changed = false;

        if (ImGui.Checkbox("Aether", ref aether))
        {
            plugin.Configuration.ShowAether = aether;
            changed = true;
        }

        ImGui.SameLine();

        if (ImGui.Checkbox("Primal", ref primal))
        {
            plugin.Configuration.ShowPrimal = primal;
            changed = true;
        }

        ImGui.SameLine();

        if (ImGui.Checkbox("Crystal", ref crystal))
        {
            plugin.Configuration.ShowCrystal = crystal;
            changed = true;
        }

        ImGui.SameLine();

        if (ImGui.Checkbox("Dynamis", ref dynamis))
        {
            plugin.Configuration.ShowDynamis = dynamis;
            changed = true;
        }

        if (changed)
        {
            plugin.Configuration.Save();
            plugin.DatacenterSelectionChanged();
        }
    }

    private void DrawRefreshControls()
    {
        if (ImGui.Button("Refresh Pots"))
        {
            _ = plugin.RefreshTrackersAsync();
        }

        ImGui.SameLine();

        if (plugin.IsRefreshing)
        {
            ImGui.TextDisabled("Refreshing...");
        }
        else
        {
            ImGui.Text(
                $"Instances found: {plugin.Trackers.Count}");
        }

        if (!string.IsNullOrEmpty(plugin.ApiError))
        {
            ImGui.TextColored(
                new Vector4(1f, 0.3f, 0.3f, 1f),
                $"API Error: {plugin.ApiError}");
        }
    }


    private void DrawPotTable()
    {
        var flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("PotTimerTable", 4, flags))
            return;

        ImGui.TableSetupColumn("Datacenter");
        ImGui.TableSetupColumn("Zone");
        ImGui.TableSetupColumn("Next Pot");
        ImGui.TableSetupColumn("Countdown");

        ImGui.TableHeadersRow();

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var tracker in plugin.Trackers)
        {
            var pots = tracker.GetPots();

            var lastPot = pots
                .Where(p => IsPotFate(p.FateId))
                .Where(p => p.SpawnTime > 0)
                .Where(p => p.SpawnTime <= now)
                .OrderByDescending(p => p.SpawnTime)
                .FirstOrDefault();

            string zone = GetZoneName(tracker.Territory, pots);
            string nextPot = "Unknown";
            string countdown = "Waiting for first Pot";

            long nextSpawnTime = 0;

            if (lastPot != null)
            {
                int nextPotId = GetNextPotId(lastPot.FateId);

                nextPot = nextPotId switch
                {
                    1976 or 2072 => "North",
                    1977 or 2073 => "South",
                    _ => "Unknown"
                };

                // 30 minutes after the previous Pot spawned.
                nextSpawnTime = lastPot.SpawnTime + 1800;

                long remaining = nextSpawnTime - now;

                countdown = remaining > 0
                    ? FormatCountdown(remaining)
                    : "Ready / Overdue";
            }

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(GetDatacenterName(tracker.Datacenter));

            ImGui.TableNextColumn();
            ImGui.Text(zone);

            ImGui.TableNextColumn();
            ImGui.Text(nextPot);

            ImGui.TableNextColumn();

            if (lastPot == null)
            {
                ImGui.TextDisabled(countdown);
            }
            else if (nextSpawnTime <= now)
            {
                ImGui.TextColored(
                    new Vector4(0.3f, 1f, 0.3f, 1f),
                    countdown);
            }
            else if (nextSpawnTime - now <= 300)
            {
                ImGui.TextColored(
                    new Vector4(1f, 0.8f, 0.2f, 1f),
                    countdown);
            }
            else
            {
                ImGui.Text(countdown);
            }
        }

        ImGui.EndTable();
    }


    private static bool IsPotFate(int fateId)
    {
        return fateId is 1976 or 1977 or 2072 or 2073;
    }

    private static string GetPotName(int fateId)
    {
        return fateId switch
        {
            1976 => "Persistent Pots",
            1977 => "Pleading Pots",
            2072 => "Daylight Pottery",
            2073 => "In a Pot of Bother",
            _ => $"Unknown ({fateId})"
        };
    }

    private static int GetNextPotId(int fateId)
    {
        return fateId switch
        {
            1976 => 1977,
            1977 => 1976,
            2072 => 2073,
            2073 => 2072,
            _ => 0
        };
    }

    private static string GetZoneName(
        int territory,
        System.Collections.Generic.List<PotData> pots)
    {
        if (pots.Any(p => p.FateId is 1976 or 1977))
            return "South Horn";

        if (pots.Any(p => p.FateId is 2072 or 2073))
            return "North Horn";

        return $"Territory {territory}";
    }

    private static string FormatCountdown(long seconds)
    {
        if (seconds <= 0)
            return "00:00";

        var duration = TimeSpan.FromSeconds(seconds);

        if (duration.TotalHours >= 1)
        {
            return
                $"{(int)duration.TotalHours:D2}:" +
                $"{duration.Minutes:D2}:" +
                $"{duration.Seconds:D2}";
        }

        return $"{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    private static string FormatTimestamp(long timestamp)
    {
        if (timestamp <= 0)
            return "Unknown";

        try
        {
            return DateTimeOffset
                .FromUnixTimeSeconds(timestamp)
                .ToLocalTime()
                .ToString("HH:mm:ss");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "Invalid";
        }
    }

    private static string GetDatacenterName(int id)
    {
        return id switch
        {
            4 => "Aether",
            5 => "Primal",
            8 => "Crystal",
            11 => "Dynamis",
            _ => $"Unknown ({id})"
        };
    }
}
