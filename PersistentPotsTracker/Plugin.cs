

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Excel.Sheets;
using PersistentPotsTracker.Windows;

namespace PersistentPotsTracker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pots";

    private static readonly TimeSpan RefreshInterval =
        TimeSpan.FromSeconds(45);

    private static readonly TimeSpan MatchInterval =
        TimeSpan.FromSeconds(2);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IDataManager dataManager;
    private readonly IFateTable fateTable;
    private readonly IPluginLog pluginLog;

    public IClientState ClientState { get; }
    public Configuration Configuration { get; }

    private readonly WindowSystem windowSystem =
        new("PersistentPotsTracker");

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly TrackerApiClient apiClient = new();

    private readonly Dictionary<int, string> fateNameCache = new();
    private readonly Dictionary<int, string> ceNameCache = new();

    public List<TrackerData> Trackers { get; private set; } = new();
    public List<PotTableRow> PotRows { get; private set; } = new();

    public bool IsRefreshing { get; private set; }
    public string? ApiError { get; private set; }

    public int? MatchedTrackerId { get; private set; }

    private DateTime lastRefresh = DateTime.MinValue;
    private DateTime lastMatchCheck = DateTime.MinValue;

    private int selectionVersion;
    private bool disposed;
    private bool wasWindowOpen;
    private bool refreshOnEntryPending;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IDataManager dataManager,
        IFateTable fateTable,
        IPluginLog pluginLog)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.dataManager = dataManager;
        this.fateTable = fateTable;
        this.pluginLog = pluginLog;

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
        ClientState.TerritoryChanged += OnTerritoryChanged;
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

    private bool IsHornTerritory(uint territoryId)
    {
        var sheet = dataManager.GetExcelSheet<TerritoryType>();
        var territory = sheet.GetRowOrDefault(territoryId);

        if (territory == null)
            return false;

        string name = territory.Value.PlaceName.Value.Name.ToString();

        return name.Contains(
                   "South Horn",
                   StringComparison.OrdinalIgnoreCase)
               ||
               name.Contains(
                   "North Horn",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        // A previous instance must never remain highlighted.
        SetMatchedTracker(null);

        // Only entering one of the two Horn zones triggers
        // an additional API refresh.
        if (!IsHornTerritory(territoryId))
            return;

        refreshOnEntryPending = true;
    }

    public void DatacenterSelectionChanged()
    {
        selectionVersion++;

        Trackers = new();
        PotRows = new();
        MatchedTrackerId = null;
        ApiError = null;
        lastRefresh = DateTime.MinValue;

        if (!IsRefreshing)
            _ = RefreshTrackersAsync();
    }

    public async Task RefreshTrackersAsync()
    {
        if (disposed || IsRefreshing)
            return;

        var datacenters = new List<int>(4);

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
            Trackers = new();
            PotRows = new();
            MatchedTrackerId = null;
            ApiError = null;
            refreshOnEntryPending = false;
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

            Trackers = results;
            PotRows = BuildPotRows(results);
            ApiError = null;
        }
        catch (Exception ex)
        {
            if (!disposed && requestVersion == selectionVersion)
            {
                ApiError = ex.Message;
                pluginLog.Error(ex, "Failed to refresh trackers.");
            }
        }
        finally
        {
            IsRefreshing = false;

            if (!disposed)
            {
                lastRefresh = requestVersion == selectionVersion
                    ? DateTime.UtcNow
                    : DateTime.MinValue;
            }
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed)
            return;

        if (DateTime.UtcNow - lastMatchCheck >= MatchInterval)
        {
            lastMatchCheck = DateTime.UtcNow;
            TryMatchCurrentInstance();
        }

        if (IsRefreshing)
            return;

        if (refreshOnEntryPending)
        {
            refreshOnEntryPending = false;
            _ = RefreshTrackersAsync();
            return;
        }

        bool isWindowOpen = mainWindow.IsOpen;

        if (isWindowOpen && !wasWindowOpen)
            lastRefresh = DateTime.MinValue;

        wasWindowOpen = isWindowOpen;

        if (DateTime.UtcNow - lastRefresh >= RefreshInterval)
            _ = RefreshTrackersAsync();
    }

    private HashSet<int> GetLocalFateIds()
    {
        var ids = new HashSet<int>();

        foreach (var fate in fateTable)
        {
            if (fate == null)
                continue;

            ids.Add((int)fate.FateId);
        }

        return ids;
    }

    private unsafe HashSet<int> GetLocalCEIds()
    {
        var ids = new HashSet<int>();

        var container = DynamicEventContainer.GetInstance();

        if (container == null)
            return ids;

        foreach (ref var ce in container->Events)
        {
            if (ce.DynamicEventId == 0)
                continue;

            if (ce.IsActive())
                ids.Add(ce.DynamicEventId);
        }

        return ids;
    }

    private void TryMatchCurrentInstance()
    {
        if (Trackers.Count == 0)
        {
            SetMatchedTracker(null);
            return;
        }

        var localFates = GetLocalFateIds();
        var localCEs = GetLocalCEIds();

        if (localFates.Count == 0 || localCEs.Count == 0)
        {
            SetMatchedTracker(null);
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var matches = new List<TrackerData>();

        foreach (var tracker in Trackers)
        {
            var apiFates = tracker.GetFates();
            var apiCEs = tracker.GetEncounters();

            var latestFate = apiFates
                .Where(f => f.SpawnTime > 0 && f.SpawnTime <= now)
                .OrderByDescending(f => f.SpawnTime)
                .FirstOrDefault();

            bool fateMatch = latestFate != null &&
                             localFates.Contains(latestFate.FateId);

            bool ceMatch = apiCEs.Any(ce =>
                localCEs.Contains(ce.FateId) &&
                ce.SpawnTime > 0 &&
                ce.SpawnTime <= now &&
                ce.DeathTime == 0);

            if (fateMatch && ceMatch)
                matches.Add(tracker);
        }

        SetMatchedTracker(
            matches.Count == 1 ? matches[0].Id : null);
    }

    private void SetMatchedTracker(int? trackerId)
    {
        if (MatchedTrackerId == trackerId)
            return;

        MatchedTrackerId = trackerId;
    }

    private string GetFateName(int fateId)
    {
        if (fateNameCache.TryGetValue(fateId, out var cached))
            return cached;

        var sheet = dataManager.GetExcelSheet<Fate>();
        var fate = sheet.GetRowOrDefault((uint)fateId);

        string name = fate?.Name.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(name))
            name = $"FATE {fateId}";

        fateNameCache[fateId] = name;
        return name;
    }

    private string GetCEName(int ceId)
    {
        if (ceNameCache.TryGetValue(ceId, out var cached))
            return cached;

        var sheet =
            dataManager.GetExcelSheet<Lumina.Excel.Sheets.DynamicEvent>();

        var ce = sheet.GetRowOrDefault((uint)ceId);

        string name = ce?.Name.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(name))
            name = $"CE {ceId}";

        ceNameCache[ceId] = name;
        return name;
    }

    private string GetLastCurrentEncounter(
        List<PotData> history,
        bool isFate,
        long now)
    {
        PotData? latestActive = null;
        PotData? latest = null;

        foreach (var encounter in history)
        {
            if (encounter.SpawnTime <= 0 ||
                encounter.SpawnTime > now)
                continue;

            if (latest == null ||
                encounter.SpawnTime > latest.SpawnTime)
            {
                latest = encounter;
            }

            if (encounter.DeathTime == 0 &&
                (latestActive == null ||
                 encounter.SpawnTime > latestActive.SpawnTime))
            {
                latestActive = encounter;
            }
        }

        var selected = latestActive ?? latest;

        if (selected == null)
            return "Unknown";

        string name = isFate
            ? GetFateName(selected.FateId)
            : GetCEName(selected.FateId);

        return latestActive != null
            ? $"{name} (Active)"
            : name;
    }

    private List<PotTableRow> BuildPotRows(
        List<TrackerData> trackers)
    {
        var rows = new List<PotTableRow>(trackers.Count);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var tracker in trackers)
        {
            var pots = tracker.GetPots();
            var fates = tracker.GetFates();
            var encounters = tracker.GetEncounters();

            PotData? lastPot = null;
            bool hasSouth = false;
            bool hasNorth = false;

            foreach (var pot in pots)
            {
                switch (pot.FateId)
                {
                    case 1976:
                    case 1977:
                        hasSouth = true;
                        break;

                    case 2072:
                    case 2073:
                        hasNorth = true;
                        break;

                    default:
                        continue;
                }

                if (pot.SpawnTime <= 0 || pot.SpawnTime > now)
                    continue;

                if (lastPot == null ||
                    pot.SpawnTime > lastPot.SpawnTime)
                {
                    lastPot = pot;
                }
            }

            string zone = hasSouth
                ? "South Horn"
                : hasNorth
                    ? "North Horn"
                    : "Unknown";

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

            rows.Add(new PotTableRow
            {
                TrackerId = tracker.Id,
                Datacenter = datacenter,
                Zone = zone,

                LastCurrentCE = GetLastCurrentEncounter(
                    encounters, false, now),

                LastCurrentFate = GetLastCurrentEncounter(
                    fates, true, now),

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
        ClientState.TerritoryChanged -= OnTerritoryChanged;

        commandManager.RemoveHandler(CommandName);

        pluginInterface.UiBuilder.Draw -= DrawUI;
        pluginInterface.UiBuilder.OpenMainUi -= OpenMainUI;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUI;

        windowSystem.RemoveAllWindows();

        apiClient.Dispose();
    }
}
