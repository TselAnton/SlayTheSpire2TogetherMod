using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.addons.mega_text;
using Sts2Demo.Core;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace Sts2Demo.Ui;

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
internal static class DemoSettingsUi
{
    static void Postfix(NSettingsScreen __instance)
    {
        var manager = __instance.GetNode<NSettingsTabManager>("%SettingsTabManager");
        var tabs = (Dictionary<NSettingsTab, NSettingsPanel>)AccessTools.Field(typeof(NSettingsTabManager), "_tabs").GetValue(manager)!;
        var original = __instance.GetNode<NSettingsPanel>("%GeneralSettings");
        AddTranslations();
        var panel = (NSettingsPanel)original.Duplicate((int)Node.DuplicateFlags.Scripts);
        panel.Name = "FriendViewSettings"; panel.Visible = false;
        var content = panel.GetNode<VBoxContainer>("VBoxContainer");
        foreach (var child in panel.GetChildren().Where(child => child != content).ToList()) { panel.RemoveChild(child); child.Free(); }
        foreach (var child in content.GetChildren()) { content.RemoveChild(child); child.Free(); }
        var template = original.Content.GetNode<Control>("ShowMpDrawings");
        var localizedLabels = new List<(MegaRichTextLabel Label, string Key)>();
        void Row(string name, string key, bool value, Action<bool> set)
        {
            var row = (Control)template.Duplicate(0);
            row.Name = name;
            foreach (var node in DemoUi.Descendants<Node>(row).ToList())
                if (node is Control control) { control.MouseFilter = Control.MouseFilterEnum.Ignore; control.FocusMode = Control.FocusModeEnum.None; }
            var originalLabel = DemoUi.Descendants<MegaRichTextLabel>(template).First();
            var oldLabel = row.GetNode<Control>(template.GetPathTo(originalLabel));
            var label = (MegaRichTextLabel)originalLabel.Duplicate((int)Node.DuplicateFlags.Scripts);
            var labelParent = oldLabel.GetParent(); labelParent.RemoveChild(oldLabel); oldLabel.Free(); labelParent.AddChild(label);
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.SetTextAutoSize(new LocString("settings_ui", key).GetFormattedText());
            localizedLabels.Add((label, key));
            // Use the base native tickbox only, never the original setting's subclass/subscriptions.
            var source = row.GetNode<Control>(template.GetPathTo(DemoUi.Descendants<NTickbox>(template).First()));
            var checkbox = new NTickbox { Name = "Toggle", Size = source.Size,
                CustomMinimumSize = source.CustomMinimumSize, SizeFlagsHorizontal = source.SizeFlagsHorizontal,
                SizeFlagsVertical = source.SizeFlagsVertical, SizeFlagsStretchRatio = source.SizeFlagsStretchRatio,
                AnchorLeft = source.AnchorLeft, AnchorRight = source.AnchorRight, AnchorTop = source.AnchorTop, AnchorBottom = source.AnchorBottom,
                OffsetLeft = source.OffsetLeft, OffsetRight = source.OffsetRight, OffsetTop = source.OffsetTop, OffsetBottom = source.OffsetBottom,
                FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Stop };
            checkbox.Set("layout_mode", source.Get("layout_mode"));
            foreach (var child in source.GetChildren()) { source.RemoveChild(child); checkbox.AddChild(child); }
            foreach (var child in DemoUi.Descendants<Node>(checkbox))
            { child.Owner = checkbox; if (child.Name == "TickboxVisuals") child.UniqueNameInOwner = true; }
            var parent = source.GetParent(); parent.RemoveChild(source); source.Free(); parent.AddChild(checkbox);
            content.AddChild(row);
            var separator = original.Content.GetChildren().OfType<HSeparator>().FirstOrDefault();
            if (separator != null) content.AddChild(separator.Duplicate(0));
            checkbox.Ready += () => checkbox.IsTicked = value;
            checkbox.Toggled += box => { set(box.IsTicked); DemoSettings.Save(); };
            row.MouseFilter = Control.MouseFilterEnum.Pass;
            void ShowTip() => NHoverTipSet.CreateAndShow(row, new HoverTip(new LocString("settings_ui", key),
                new LocString("settings_ui", key + "_DESCRIPTION")))?.SetGlobalPosition(row.GlobalPosition + NSettingsScreen.settingTipsOffset);
            void HideTip() => NHoverTipSet.Remove(row);
            row.MouseEntered += ShowTip; row.MouseExited += HideTip;
            checkbox.MouseEntered += ShowTip; checkbox.MouseExited += HideTip;
            checkbox.FocusEntered += ShowTip; checkbox.FocusExited += HideTip;
            row.TreeExiting += HideTip;
            panel.VisibilityChanged += () => { if (!panel.Visible) HideTip(); };
        }
        Row("DemoEntry", "FRIEND_VIEW_BUTTON",
            DemoSettings.SeparateButton, value => DemoSettings.SeparateButton = value);
        Row("DemoAvailability", "FRIEND_VIEW_BATTLE_ONLY",
            !DemoSettings.AllActivities, value => DemoSettings.AllActivities = !value);
        original.GetParent().AddChild(panel);
        void RefreshLanguage()
        {
            AddTranslations();
            foreach (var entry in localizedLabels)
            { entry.Label.RefreshFont(); entry.Label.SetTextAutoSize(new LocString("settings_ui", entry.Key).GetFormattedText()); }
        }
        LocManager.Instance.SubscribeToLocaleChange(RefreshLanguage);
        panel.TreeExiting += () => LocManager.Instance.UnsubscribeToLocaleChange(RefreshLanguage);
        var tab = (NSettingsTab)tabs.Keys.First().Duplicate((int)Node.DuplicateFlags.Scripts);
        tab.Name = "FriendView";
        manager.AddChild(tab);
        tab.SetLabel("Friend View");
        tabs.Add(tab, panel);
        tab.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
            AccessTools.Method(typeof(NSettingsTabManager), "SwitchTabTo").Invoke(manager, [tab])));
        var all = tabs.Keys.ToArray();
        float left = all[0].Position.X;
        float right = all[^2].Position.X + all[^2].Size.X;
        float width = (right - left) / all.Length;
        for (int i = 0; i < all.Length; i++)
        {
            all[i].Position = new(left + i * width, all[0].Position.Y);
            all[i].Size = new(width, all[i].Size.Y);
        }
    }

    internal static void AddTranslations()
    {
        bool russian = LocManager.Instance.Language == "rus";
        LocManager.Instance.GetTable("settings_ui").MergeWith(new Dictionary<string, string>
        {
            ["FRIEND_VIEW_BUTTON"] = russian ? "Кнопка Friend View" : "Show Friend View Button",
            ["FRIEND_VIEW_BUTTON_DESCRIPTION"] = russian
                ? "Включено: небольшая кнопка с глазом рядом с каждым другом. Выключено: нажмите на друга в таблице игроков. Если просмотр недоступен, откроются обычные сведения об игроке."
                : "On: show a small eye button beside each friend. Off: click the friend in the player list to observe. When observation is unavailable, the normal player-details action is preserved.",
            ["FRIEND_VIEW_BATTLE_ONLY"] = russian ? "Только бой и награды" : "Battle and Rewards Only",
            ["FRIEND_VIEW_BATTLE_ONLY_DESCRIPTION"] = russian
                ? "Включено: просмотр только в бою и на экранах послебоевых наград. Выключено: также доступны магазины, события, гадание, костры и другие поддерживаемые окна. Карта открывается ваша, с общими отметками."
                : "On: observe combat and post-battle rewards only. Off: also observe shops, events, predictions, rest sites and other supported screens. The map opens your own shared map.",
        });
    }
}
