
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace OccultTracker.Windows;

public class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Occult Tracker Settings###OccultTrackerConfigWindow")
    {
        this.plugin = plugin;

        Size = new Vector2(400, 200);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.Text("Occult Tracker Settings");
        ImGui.Separator();

        bool movable =
            plugin.Configuration.IsConfigWindowMovable;

        if (ImGui.Checkbox("Allow settings window movement", ref movable))
        {
            plugin.Configuration.IsConfigWindowMovable = movable;
            plugin.Configuration.Save();
        }

        Flags = movable
            ? ImGuiWindowFlags.None
            : ImGuiWindowFlags.NoMove;

        ImGui.Spacing();
        ImGui.TextDisabled(
            "Tracker data automatically refreshes every 45 seconds."
        );
    }
}
