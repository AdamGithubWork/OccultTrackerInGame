
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using PersistentPotsTracker.Windows;

namespace PersistentPotsTracker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pots";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IDataManager dataManager;

    public IClientState ClientState { get; }

    private readonly WindowSystem windowSystem =
        new("PersistentPotsTracker");

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Configuration Configuration { get; }

    private readonly TrackerApiClient apiClient = new();

    public List<TrackerData> Trackers { get; private set; } = new();
    public List<PotTableRow> PotRows { get; private set; } = new();

    public bool IsRefreshing { get; private set; }
    public string? ApiError { get; private set; }

    private DateTime lastRefresh = DateTime.MinValue;

    private static readonly TimeSpan RefreshInterval =
        TimeSpan.FromSeconds(45);

    private bool disposed;
    private int selectionVersion;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IDataManager dataManager)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.dataManager = dataManager;

        ClientState = clientState;

        Configuration =
            pluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();

        Configuration.Initialize(pluginInterface);

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        commandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Open the Occult Tracker window."
            });

        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUI;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUI;

        framework.Update += OnFrameworkUpdate;
    }

    private void OnCommand(string command, string args)
    {
        mainWindow.Toggle();
    }

    private void DrawUI()
    {
        windowSystem.Draw();
    }

    private void OpenMainUI()
    {
        mainWindow.IsOpen = true;
    }

    private void OpenConfigUI()
    {
        configWindow.IsOpen = true;
    }

    public void DatacenterSelectionChanged()
    {
        selectionVersion++;

        PotRows = new();
        Trackers = new();

        lastRefresh = DateTime.MinValue;

        _ = RefreshTrackersAsync();
    }

    public async Task RefreshTrackersAsync()
    {
        if (disposed || IsRefreshing)
            return;

        var datacenters = new List<int>();

        if (Configuration.ShowAether)
            datacenters.Add(4);

        if (Configuration.ShowPrimal)
            datacenters.Add(5);

        if (Configuration.ShowCrystal)
            datacenters.Add(8);

        if (Configuration.ShowDynamis)
            datacenters.Add(11);

        if (datacenters.Count == 0)
        {
            PotRows = new();
            Trackers = new();
            ApiError = null;
            return;
        }

        IsRefreshing = true;

        int requestVersion = selectionVersion;

        try
        {
            var results =
                await apiClient.GetTrackersAsync(datacenters);

            if (disposed || requestVersion != selectionVersion)
                return;

            var rows = BuildPotRows(results);

            Trackers = results;
            PotRows = rows;

            ApiError = null;
        }
        catch (Exception ex)
        {
            if (!disposed && requestVersion == selectionVersion)
                ApiError = ex.Message;
        }
        finally
        {
            IsRefreshing = false;

            if (!disposed)
                lastRefresh = DateTime.UtcNow;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed || IsRefreshing)
            return;

        if (DateTime.UtcNow - lastRefresh >= RefreshInterval)
        {
            _ = RefreshTrackersAsync();
        }
    }

    // Resolve regular FATE IDs through FFXIV game data.
    private string GetFateName(int fateId)
    {
        var sheet = dataManager.GetExcelSheet<Fate>();

        var fate = sheet.GetRowOrDefault((uint)fateId);

        if (fate == null)
            return $"FATE {fateId}";

        string name = fate.Value.Name.ToString();

        return string.IsNullOrWhiteSpace(name)
            ? $"FATE {fateId}"
            : name;
    }

    // CE IDs use a different ID system.
    // For now, display their numeric IDs.
    private string GetCEName(int ceId)
    {
        var sheet = dataManager.GetExcelSheet<DynamicEvent>();

        var ce = sheet.GetRowOrDefault((uint)ceId);

        if (ce == null)
            return $"CE {ceId}";

        string name = ce.Value.Name.ToString();

        return string.IsNullOrWhiteSpace(name)
            ? $"CE {ceId}"
            : name;
    }

    // Find the active encounter, or fall back to the
    // most recently spawned encounter.
    private string GetLastCurrentEncounter(
        List<PotData> history,
        bool isFate)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var active = history
            .Where(p => p.SpawnTime > 0
                     && p.SpawnTime <= now
                     && p.DeathTime == 0)
            .OrderByDescending(p => p.SpawnTime)
            .FirstOrDefault();

        if (active != null)
        {
            string name = isFate
                ? GetFateName(active.FateId)
                : GetCEName(active.FateId);

            return $"{name} (Active)";
        }

        var last = history
            .Where(p => p.SpawnTime > 0
                     && p.SpawnTime <= now)
            .OrderByDescending(p => p.SpawnTime)
            .FirstOrDefault();

        if (last != null)
        {
            return isFate
                ? GetFateName(last.FateId)
                : GetCEName(last.FateId);
        }

        return "Unknown";
    }

    private List<PotTableRow> BuildPotRows(
        List<TrackerData> trackers)
    {
        var rows = new List<PotTableRow>();

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var tracker in trackers)
        {
            var pots = tracker.GetPots();

            PotData? lastPot = null;

            foreach (var pot in pots)
            {
                if (pot.FateId is not (1976 or 1977 or 2072 or 2073))
                    continue;

                if (pot.SpawnTime <= 0 || pot.SpawnTime > now)
                    continue;

                if (lastPot == null ||
                    pot.SpawnTime > lastPot.SpawnTime)
                {
                    lastPot = pot;
                }
            }

            string zone = "Unknown";

            if (pots.Exists(p => p.FateId is 1976 or 1977))
                zone = "South Horn";
            else if (pots.Exists(p => p.FateId is 2072 or 2073))
                zone = "North Horn";

            string nextPot = "Unknown";
            long nextSpawnTime = 0;

            if (lastPot != null)
            {
                nextPot = lastPot.FateId switch
                {
                    1976 or 2072 => "S",
                    1977 or 2073 => "N",
                    _ => "Unknown"
                };

                nextSpawnTime = lastPot.SpawnTime + 1800;
            }

            string datacenter = tracker.Datacenter switch
            {
                4 => "Aether",
                5 => "Primal",
                8 => "Crystal",
                11 => "Dynamis",
                _ => $"Unknown ({tracker.Datacenter})"
            };

            string lastFate = GetLastCurrentEncounter(
                tracker.GetFates(),
                true);

            string lastCE = GetLastCurrentEncounter(
                tracker.GetEncounters(),
                false);

            rows.Add(new PotTableRow
            {
                Datacenter = datacenter,
                Zone = zone,
                LastCurrentFate = lastFate,
                LastCurrentCE = lastCE,
                NextPot = nextPot,
                NextSpawnTime = nextSpawnTime
            });
        }

        return rows;
    }

    public void Dispose()
    {
        disposed = true;

        framework.Update -= OnFrameworkUpdate;

        commandManager.RemoveHandler(CommandName);

        pluginInterface.UiBuilder.Draw -= DrawUI;
        pluginInterface.UiBuilder.OpenMainUi -= OpenMainUI;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUI;

        windowSystem.RemoveAllWindows();

        apiClient.Dispose();
    }
}
