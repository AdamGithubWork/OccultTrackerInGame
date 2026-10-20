
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PersistentPotsTracker.Windows;

namespace PersistentPotsTracker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pots";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
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
        IClientState clientState)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
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

        // Clear results from the previous selection.
        PotRows = new();

        // Allow an immediate refresh.
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
    private static List<PotTableRow> BuildPotRows(
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
                1976 or 2072 => "South",
                1977 or 2073 => "North",
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
                Datacenter = datacenter,
                Server = tracker.Server,
                Zone = zone,
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

        // Pending requests may still finish, but their results
        // will be ignored because disposed is true.
        apiClient.Dispose();
    }
}
