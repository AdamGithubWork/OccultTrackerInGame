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

    private static readonly TimeSpan MatchRetryDelay =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MatchInterval =
        TimeSpan.FromSeconds(2);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IDataManager dataManager;
    private readonly IFateTable fateTable;
    private readonly IPluginLog pluginLog;
    private readonly IPlayerState playerState;

    public IClientState ClientState { get; }
    public Configuration Configuration { get; }

    private readonly WindowSystem windowSystem =
        new("PersistentPotsTracker");

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly PotOverlayWindow potOverlayWindow;

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
    private bool refreshAfterCurrentRequest;
    private DateTime? scheduledMatchRefresh;
    private bool awaitingMatchRetry;
    private bool hasActivitySnapshot;
    private HashSet<int> previousFateIds = new();
    private HashSet<int> previousCeIds = new();

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IDataManager dataManager,
        IFateTable fateTable,
        IPluginLog pluginLog,
        IPlayerState playerState)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.dataManager = dataManager;
        this.fateTable = fateTable;
        this.pluginLog = pluginLog;
        this.playerState = playerState;

        ClientState = clientState;

        Configuration =
            pluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();

        Configuration.Initialize(pluginInterface);

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        potOverlayWindow = new PotOverlayWindow(this);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(potOverlayWindow);

        commandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Persistent Pots Tracker."
            });

        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUI;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUI;

        framework.Update += OnFrameworkUpdate;
        ClientState.TerritoryChanged += OnTerritoryChanged;

        // Handle loading the plugin while already inside a Horn.
        refreshOnEntryPending =
            IsHornTerritory(ClientState.TerritoryType);

        if (refreshOnEntryPending)
            scheduledMatchRefresh = DateTime.UtcNow + MatchRetryDelay;
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

    public void OpenTrackerWindow()
    {
        mainWindow.IsOpen = true;
    }

    public string GetCurrentDatacenter()
    {
        if (!playerState.IsLoaded)
            return "Unknown";

        var world = playerState.CurrentWorld;

        if (!world.IsValid)
            return "Unknown";

        uint datacenterId = world.Value.DataCenter.RowId;

        return datacenterId switch
        {
            4 => "Aether",
            5 => "Primal",
            8 => "Crystal",
            11 => "Dynamis",
            _ => "Unknown"
        };
    }

    private bool IsHornTerritory(uint territoryId)
    {
        var sheet = dataManager.GetExcelSheet<TerritoryType>();
        var territory = sheet.GetRowOrDefault(territoryId);

        if (territory == null)
            return false;

        string name =
            territory.Value.PlaceName.Value.Name.ToString();

        return name.Contains(
                   "South Horn",
                   StringComparison.OrdinalIgnoreCase)
               ||
               name.Contains(
                   "North Horn",
                   StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInsideHorn()
    {
        return IsHornTerritory(ClientState.TerritoryType);
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        // Clear the previous instance when changing territories.
        SetMatchedTracker(null);

        // Entering a Horn triggers an immediate API refresh.
        refreshOnEntryPending = IsHornTerritory(territoryId);

        scheduledMatchRefresh = refreshOnEntryPending
            ? DateTime.UtcNow + MatchRetryDelay
            : null;

        awaitingMatchRetry = false;
        hasActivitySnapshot = false;
        previousFateIds.Clear();
        previousCeIds.Clear();
    }

    public void DatacenterSelectionChanged()
    {
        selectionVersion++;

        Trackers = new();
        PotRows = new();
        MatchedTrackerId = null;
        ApiError = null;
        lastRefresh = DateTime.MinValue;

        if (IsRefreshing)
            refreshAfterCurrentRequest = true;
        else
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

            // Preserve usable data if the API returns an empty result.
            if (results.Count > 0)
            {
                Trackers = results;
                PotRows = BuildPotRows(results);
                ApiError = null;
            }
            else
            {
                ApiError = "The tracker API returned no instances.";
                pluginLog.Warning(ApiError);
            }
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

                if (refreshAfterCurrentRequest ||
                    requestVersion != selectionVersion)
                {
                    refreshAfterCurrentRequest = false;
                    _ = RefreshTrackersAsync();
                }
            }
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed)
            return;

        var now = DateTime.UtcNow;
        bool insideHorn = IsInsideHorn();

        // Local activity checks are cheap and do not require an API request.
        if (insideHorn && now - lastMatchCheck >= MatchInterval)
        {
            lastMatchCheck = now;
            CheckLocalActivity();
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

        if (insideHorn && scheduledMatchRefresh.HasValue &&
            now >= scheduledMatchRefresh.Value)
        {
            scheduledMatchRefresh = null;
            awaitingMatchRetry = false;
            _ = RefreshTrackersAsync();
            return;
        }

        bool isWindowOpen = mainWindow.IsOpen;

        if (isWindowOpen && !wasWindowOpen)
            lastRefresh = DateTime.MinValue;

        wasWindowOpen = isWindowOpen;

        // Preserve periodic timer updates while the main window is open.
        if (isWindowOpen && now - lastRefresh >= RefreshInterval)
            _ = RefreshTrackersAsync();
    }

    private void CheckLocalActivity()
    {
        var currentFates = GetLocalFateIds();
        var currentCes = GetLocalCEIds();

        if (!hasActivitySnapshot)
        {
            previousFateIds = currentFates;
            previousCeIds = currentCes;
            hasActivitySnapshot = true;
            return;
        }

        bool newActivity = currentFates.Except(previousFateIds).Any() ||
                           currentCes.Except(previousCeIds).Any();

        previousFateIds = currentFates;
        previousCeIds = currentCes;

        if (!newActivity || MatchedTrackerId.HasValue)
            return;

        // Fetch now; if the external API hasn't caught up, retry in 5s.
        // A new spawn resets the retry window.
        scheduledMatchRefresh = DateTime.UtcNow + MatchRetryDelay;
        awaitingMatchRetry = true;

        if (IsRefreshing)
            refreshAfterCurrentRequest = true;
        else
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
            if (ce.DynamicEventId != 0 && ce.IsActive())
                ids.Add(ce.DynamicEventId);
        }

        return ids;
    }

    private void TryMatchCurrentInstance()
    {
        // Once matched, keep the instance until leaving the Horn.
        if (MatchedTrackerId.HasValue)
            return;

        if (Trackers.Count == 0)
            return;

        var localFates = GetLocalFateIds();
        var localCEs = GetLocalCEIds();

        if (localFates.Count == 0 && localCEs.Count == 0)
            return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string currentDatacenter = GetCurrentDatacenter();

        int currentDcId = currentDatacenter switch
        {
            "Aether" => 4,
            "Primal" => 5,
            "Crystal" => 8,
            "Dynamis" => 11,
            _ => 0
        };

        const long recentWindowSeconds = 600;

        var scoredMatches =
            new List<(int TrackerId, int Score)>();

        foreach (var tracker in Trackers)
        {
            if (currentDcId != 0 &&
                tracker.Datacenter != currentDcId)
                continue;

            bool fateMatch = tracker.GetFates().Any(f =>
                localFates.Contains(f.FateId) &&
                f.SpawnTime > 0 &&
                f.SpawnTime <= now &&
                now - f.SpawnTime <= recentWindowSeconds);

            bool ceMatch = tracker.GetEncounters().Any(ce =>
                localCEs.Contains(ce.FateId) &&
                ce.SpawnTime > 0 &&
                ce.SpawnTime <= now &&
                (ce.DeathTime == 0 ||
                 ce.DeathTime > now - 120));

            int score =
                (fateMatch ? 1 : 0) +
                (ceMatch ? 2 : 0);

            if (score > 0)
                scoredMatches.Add((tracker.Id, score));
        }

        if (scoredMatches.Count == 0)
            return;

        int bestScore =
            scoredMatches.Max(m => m.Score);

        var bestMatches = scoredMatches
            .Where(m => m.Score == bestScore)
            .ToList();

        // Only identify the instance if there is one clear match.
        if (bestMatches.Count == 1)
        {
            SetMatchedTracker(bestMatches[0].TrackerId);
        }
    }

    private void SetMatchedTracker(int? trackerId)
    {
        if (MatchedTrackerId == trackerId)
            return;

        MatchedTrackerId = trackerId;

        if (trackerId.HasValue && awaitingMatchRetry)
        {
            scheduledMatchRefresh = null;
            awaitingMatchRetry = false;
        }

        pluginLog.Information(
            $"INSTANCE MATCH: {trackerId?.ToString() ?? "NONE"}");
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
            dataManager.GetExcelSheet<
                Lumina.Excel.Sheets.DynamicEvent>();

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
        var rows =
            new List<PotTableRow>(trackers.Count);

        long now =
            DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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

                if (pot.SpawnTime <= 0 ||
                    pot.SpawnTime > now)
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

                nextSpawnTime =
                    lastPot.SpawnTime + 1800;
            }

            string datacenter =
                tracker.Datacenter switch
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
