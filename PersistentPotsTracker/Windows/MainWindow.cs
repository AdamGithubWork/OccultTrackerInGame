
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Collections.Generic;

namespace PersistentPotsTracker.Windows;

public class MainWindow : Window
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Persistent Pots Tracker###PersistentPotsTrackerMainWindow")
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
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.Sortable |
            ImGuiTableFlags.SortMulti;

        if (!ImGui.BeginTable("PotTimerTable", 6, flags))
            return;

        ImGui.TableSetupColumn("Datacenter");
        ImGui.TableSetupColumn("Zone");
        ImGui.TableSetupColumn("Last/Current CE");
        ImGui.TableSetupColumn("Last/Current FATE");
        ImGui.TableSetupColumn("Next Pot");
        ImGui.TableSetupColumn("Countdown");

        // Countdown ascending by default.
        ImGui.TableSetupColumn(
            "Countdown",
            ImGuiTableColumnFlags.DefaultSort);

        ImGui.TableHeadersRow();

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Build one display row per tracker.
        var rows = plugin.PotRows.ToList();

        // Read the current sorting selection.
        var sortSpecs = ImGui.TableGetSortSpecs();

        if (sortSpecs.SpecsCount > 0)
        {
            // SortMulti allows multiple sort columns.
            // Apply them from lowest to highest priority.
            for (int i = sortSpecs.SpecsCount - 1; i >= 0; i--)
            {
                var spec = sortSpecs.Specs[i];

                bool ascending =
                    spec.SortDirection == ImGuiSortDirection.Ascending;

                rows = spec.ColumnIndex switch
                {
                    0 => ascending
                        ? rows.OrderBy(r => r.Datacenter).ToList()
                        : rows.OrderByDescending(r => r.Datacenter).ToList(),

                    1 => ascending
                        ? rows.OrderBy(r => r.Zone).ToList()
                        : rows.OrderByDescending(r => r.Zone).ToList(),

                    2 => ascending
                        ? rows.OrderBy(r => r.LastCurrentCE).ToList()
                        : rows.OrderByDescending(r => r.LastCurrentCE).ToList(),

                    3 => ascending
                        ? rows.OrderBy(r => r.LastCurrentFate).ToList()
                        : rows.OrderByDescending(r => r.LastCurrentFate).ToList(),

                    4 => ascending
                        ? rows.OrderBy(r => r.NextPot).ToList()
                        : rows.OrderByDescending(r => r.NextPot).ToList(),

                    5 => ascending
                        ? rows.OrderBy(r => r.NextSpawnTime == 0
                            ? long.MaxValue
                            : r.NextSpawnTime).ToList()
                        : rows.OrderByDescending(r => r.NextSpawnTime).ToList(),

                    _ => rows
                };
            }
        }

        foreach (var row in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(row.Datacenter);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(row.Zone);

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(row.LastCurrentCE);

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(row.LastCurrentFate);

            ImGui.TableSetColumnIndex(4);
            ImGui.TextUnformatted(row.NextPot);

            ImGui.TableSetColumnIndex(5);
            // Keep your existing countdown rendering here.

            if (row.NextSpawnTime == 0)
            {
                ImGui.TextDisabled("Waiting for first Pot");
                continue;
            }

            long remaining = row.NextSpawnTime - now;

            if (remaining <= 0)
            {
                ImGui.TextColored(
                    new Vector4(0.3f, 1f, 0.3f, 1f),
                    "Ready / Overdue");
            }
            else if (remaining <= 300)
            {
                ImGui.TextColored(
                    new Vector4(1f, 0.8f, 0.2f, 1f),
                    FormatCountdown(remaining));
            }
            else
            {
                ImGui.Text(FormatCountdown(remaining));
            }
        }

        ImGui.EndTable();
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
}
