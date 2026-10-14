using Godot;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2Demo.Ui;

internal static partial class DemoUi
{
    public static bool Russian => TranslationServer.GetLocale().StartsWith("ru", StringComparison.OrdinalIgnoreCase);
    public static string Text(string en, string ru) => Russian ? ru : en;
    public static readonly Color Accent = new("f1d697");
    public static Label Label(string text, int size = 22)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Accent);
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        return label;
    }
    public static Button Button(string text, Texture2D? icon = null)
    {
        var button = new Button
        {
            Text = text, Icon = icon, ExpandIcon = true,
            CustomMinimumSize = new(44, 44), FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        button.AddThemeFontSizeOverride("font_size", 21);
        button.AddThemeColorOverride("font_color", Accent);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color("191b27", state == "hover" ? 0.98f : 0.92f),
                BorderColor = state == "hover" || state == "focus" ? Accent : new Color("73654a"),
                CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
                BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
            };
            button.AddThemeStyleboxOverride(state, style);
        }
        return button;
    }
    public static TextureRect Icon(Texture2D texture, Vector2 size) => new()
    {
        Texture = texture, CustomMinimumSize = size, Size = size,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    public static TextureButton Eye(Texture2D? texture, float size = 66)
    {
        var button = new TextureButton
        {
            TextureNormal = texture, TextureHover = texture, TexturePressed = texture,
            TextureFocused = texture, TextureDisabled = texture,
            IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            Size = new(size, size), CustomMinimumSize = new(size, size),
            PivotOffset = new(size / 2, size / 2), MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
        };
        // The whole square is clickable, including transparent pixels. Never animate
        // this Control's transform: it is also the hitbox and controller focus rect.
        button.TextureClickMask = null;
        return button;
    }
    public static Texture2D? LoadEye()
    {
        var scene = PreloadManager.Cache.GetScene("res://scenes/combat/peek_button.tscn").Instantiate<Control>();
        var texture = Descendants<TextureRect>(scene).FirstOrDefault(t => t.Texture != null
            && !t.Name.ToString().Contains("Flash", StringComparison.OrdinalIgnoreCase))?.Texture;
        scene.Free();
        return texture;
    }
    public static TextureRect ControllerHint(Control parent)
    {
        // Clone the native glyph control (size, texture filtering and theme), not art.
        var template = PreloadManager.Cache.GetScene("res://scenes/screens/inspect_card_screen.tscn").Instantiate<Control>();
        var source = Descendants<NTickbox>(template).First(t => t.Name == "Upgrade")
            .GetNode<TextureRect>("%ControllerIcon");
        var hint = (TextureRect)source.Duplicate(0);
        template.Free();
        hint.Name = "DemoControllerHint";
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        hint.FocusMode = Control.FocusModeEnum.None;
        parent.AddChild(hint);
        hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        hint.Position = new((parent.Size.X - hint.Size.X) / 2, -hint.Size.Y - 6);
        hint.Modulate = Colors.White;
        hint.ShowBehindParent = false;
        hint.Hide();
        return hint;
    }
    public static void UpdateHint(TextureRect hint, string action, bool available)
    {
        hint.Texture = NInputManager.Instance?.GetHotkeyIcon(action);
        hint.Visible = available && NControllerManager.Instance?.IsUsingController == true && hint.Texture != null;
        // Eye size stays fixed; only the glyph above it changes with the native binding.
        hint.Position = new((hint.GetParent<Control>().Size.X - hint.Size.X) / 2, -hint.Size.Y - 6);
    }
    public static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    public static void ReadOnly(Node root)
    {
        if (root is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
            control.FocusMode = Control.FocusModeEnum.None;
        }
        foreach (var child in root.GetChildren()) ReadOnly(child);
    }
    public static string Plain(string text) => Markup().Replace(text, "");
    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex Markup();
}
