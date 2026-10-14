using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using Sts2Demo.Core;
using Sts2Demo.Networking;

namespace Sts2Demo.Ui;

/// <summary>Native window/buttons, populated as display-only controls. Never constructs or selects a Reward.</summary>
public partial class DemoRewardsWindow : Control
{
    private readonly List<NRewardButton> _buttons = [];
    public void Build(string header, IReadOnlyList<DemoRewardEntry> entries)
    {
        var template = PreloadManager.Cache.GetScene("res://scenes/screens/rewards_screen.tscn").Instantiate<Control>();
        var window = template.GetNode<Control>("Rewards");
        template.RemoveChild(window);
        foreach (var child in DemoUi.Descendants<Node>(window)) child.Owner = window;
        template.Free();
        AddChild(window); window.Modulate = Colors.White; window.Show();
        // Keep the native centred panel's anchors/offsets. FullRect here expands
        // the stone frame to the entire viewport instead of the rewards window.
        var label = DemoUi.Descendants<Label>(window).FirstOrDefault(x => x.Name == "HeaderLabel");
        if (label != null) label.Text = header;
        foreach (var scroll in DemoUi.Descendants<Control>(window).Where(c => c.Name == "Scrollbar")) scroll.Hide();
        var container = DemoUi.Descendants<Control>(window).First(x => x.Name == "RewardsContainer");
        container.Position = new(container.Position.X, 35);
        foreach (var child in container.GetChildren()) { container.RemoveChild(child); child.QueueFree(); }
        foreach (var entry in entries)
        {
            // Native factory's screen argument is unused. No live Reward object, RNG,
            // run registration or reward-selection callbacks are copied into this view.
            var button = NRewardButton.Create(null!, null!);
            container.AddChild(button);
            button.GetNode<RichTextLabel>("%Label").Text = entry.Text;
            var icons = button.GetNode<Control>("%Icon");
            var projection = new DemoVisualProjection { Name = "RewardIcons", MouseFilter = MouseFilterEnum.Ignore };
            icons.AddChild(projection);
            projection.Apply(new DemoVisualUpdate { Full = true, Screen = "RewardIcon", Reference = [icons.Size.X, icons.Size.Y], Items = entry.Icons }, 1);
            button.Disable(); DemoUi.ReadOnly(button);
            _buttons.Add(button);
        }
        DemoUi.ReadOnly(window);
    }
    public override void _Process(double delta)
    {
        if (GetParent() is Control parent) Size = parent.Size;
    }
    public void Highlight(int hovered, int chosen)
    {
        for (int i = 0; i < _buttons.Count; i++)
            _buttons[i].Modulate = chosen >= 0 && chosen != i ? new Color(1, 1, 1, .5f)
                : i == hovered || i == chosen ? new Color(1.12f, 1.12f, 1.12f) : Colors.White;
    }
}
