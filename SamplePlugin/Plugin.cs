
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OccultTracker.Windows;

namespace OccultTracker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/occulttracker";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;

    private readonly WindowSystem windowSystem =
        new("OccultTracker");

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Configuration Configuration { get; }

    private readonly TrackerApiClient apiClient = new();

    public List<TrackerData> Trackers { get; private set; } = new();

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
        IFramework framework)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;

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
        Trackers = new();

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

            Trackers = results;
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
