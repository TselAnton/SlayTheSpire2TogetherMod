using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Sts2Demo.Ui;

// Rebind the native widgets to already-synchronized combat piles. No extra packets.
internal sealed class DemoCombatPiles
{
    private readonly Dictionary<NCombatCardPile, Player> _original = [];
    private static readonly System.Reflection.FieldInfo PileField = AccessTools.Field(typeof(NCombatCardPile), "_pile");
    private static readonly System.Reflection.FieldInfo PlayerField = AccessTools.Field(typeof(NCombatCardPile), "_localPlayer");
    public void Observe(Node root, Player player)
    {
        foreach (var button in DemoUi.Descendants<NCombatCardPile>(root))
        {
            if (!_original.ContainsKey(button) && PlayerField.GetValue(button) is Player original) _original[button] = original;
            Bind(button, player);
        }
    }
    private static void Bind(NCombatCardPile button, Player player)
    {
        // Initialize adds subscriptions but doesn't remove the old ones.
        if (PileField.GetValue(button) is CardPile previous)
        {
            previous.CardAddFinished -= (Action)AccessTools.Method(button.GetType(), "AddCard").CreateDelegate(typeof(Action), button);
            previous.CardRemoveFinished -= (Action)AccessTools.Method(typeof(NCombatCardPile), "RemoveCard").CreateDelegate(typeof(Action), button);
        }
        button.Initialize(player);
        if (button is NExhaustPileButton)
        {
            bool hasCards = player.PlayerCombatState!.ExhaustPile.Cards.Count > 0;
            button.Visible = hasCards;
            if (hasCards) button.Enable(); else button.Disable();
        }
    }
    public void Restore()
    {
        foreach (var (button, player) in _original)
            if (GodotObject.IsInstanceValid(button) && button.IsInsideTree() && player.PlayerCombatState != null) Bind(button, player);
        _original.Clear();
    }
}
