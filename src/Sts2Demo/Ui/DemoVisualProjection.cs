using Godot;
using Sts2Demo.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Assets;

namespace Sts2Demo.Ui;

/// <summary>Read-only local display primitives using installed assets. No screen scripts are instantiated remotely.</summary>
public partial class DemoVisualProjection : Control
{
    private readonly Dictionary<string, Control> _nodes = [];
    private readonly Dictionary<string, Resource?> _assets = [];
    private readonly DemoVisualState _state = new();
    private Vector2 _reference = new(1920, 1080);
    public int VisualCount => _nodes.Count;
    public bool Apply(DemoVisualUpdate update, uint sequence)
    {
        if (!_state.Apply(update, sequence)) return false;
        _reference = new(update.Reference[0], update.Reference[1]);
        if (update.Full)
        {
            foreach (var node in _nodes.Values) { RemoveChild(node); node.QueueFree(); }
            _nodes.Clear();
        }
        foreach (var id in update.Removed)
            if (_nodes.Remove(id, out var old)) { RemoveChild(old); old.QueueFree(); }
        foreach (var visual in update.Items)
        {
            if (_nodes.Remove(visual.Id, out var old)) { RemoveChild(old); old.QueueFree(); }
            var node = CreateVisual(visual);
            if (node == null) continue;
            Control display = node;
            if (visual.Clip.Length == 4)
            {
                display = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore,
                    Position = new(visual.Clip[0], visual.Clip[1]), Size = new(visual.Clip[2], visual.Clip[3]) };
                display.AddChild(node);
            }
            AddChild(display); _nodes[visual.Id] = display;
            node.Name = "Visual";
            node.Size = new(visual.Size[0], visual.Size[1]);
            var transform = new Transform2D(new(visual.Transform[0], visual.Transform[1]),
                new(visual.Transform[2], visual.Transform[3]), new(visual.Transform[4], visual.Transform[5]));
            node.Position = transform.Origin; node.Rotation = transform.Rotation; node.Scale = transform.Scale;
            if (display != node) node.Position -= display.Position;
            node.Modulate = new(visual.Tint[0], visual.Tint[1], visual.Tint[2], visual.Tint[3]);
            if (visual.Shader != "" && DemoVisualCodec.SafeShader(visual.Shader)
                && ResourceLoader.Exists(visual.Shader) && ResourceLoader.Load(visual.Shader) is Shader shader)
            {
                var material = new ShaderMaterial { Shader = shader };
                foreach (var parameter in visual.Uniforms)
                {
                    var value = parameter.Value;
                    visual.UniformKinds.TryGetValue(parameter.Key, out int type);
                    if (type == 7 && value.Length % 3 == 0)
                    {
                        var cells = new Vector3[value.Length / 3];
                        for (int i = 0; i < cells.Length; i++) cells[i] = new(value[i * 3], value[i * 3 + 1], value[i * 3 + 2]);
                        material.SetShaderParameter(parameter.Key, cells);
                    }
                    else if (type == 8 && value.Length % 4 == 0)
                    {
                        var vectors = new Vector4[value.Length / 4];
                        for (int i = 0; i < vectors.Length; i++) vectors[i] = new(value[i * 4], value[i * 4 + 1], value[i * 4 + 2], value[i * 4 + 3]);
                        material.SetShaderParameter(parameter.Key, vectors);
                    }
                    else if (type == 9 && value.Length % 4 == 0)
                    {
                        var colors = new Color[value.Length / 4];
                        for (int i = 0; i < colors.Length; i++) colors[i] = new(value[i * 4], value[i * 4 + 1], value[i * 4 + 2], value[i * 4 + 3]);
                        material.SetShaderParameter(parameter.Key, colors);
                    }
                    else if (type == 1 && value.Length == 1) material.SetShaderParameter(parameter.Key, (int)value[0]);
                    else if (type == 2 && value.Length == 1) material.SetShaderParameter(parameter.Key, value[0] != 0);
                    else if (type == 6 && value.Length == 4) material.SetShaderParameter(parameter.Key, new Vector4(value[0], value[1], value[2], value[3]));
                    else if (value.Length == 1) material.SetShaderParameter(parameter.Key, value[0]);
                    else if (value.Length == 2) material.SetShaderParameter(parameter.Key, new Vector2(value[0], value[1]));
                    else if (value.Length == 3) material.SetShaderParameter(parameter.Key, new Vector3(value[0], value[1], value[2]));
                    else if (value.Length == 4) material.SetShaderParameter(parameter.Key, new Color(value[0], value[1], value[2], value[3]));
                }
                foreach (var parameter in visual.UniformTextures)
                    if (Asset<Texture2D>(parameter.Value) is { } texture) material.SetShaderParameter(parameter.Key, texture);
                node.Material = material;
            }
            // Native negative Z values describe ordering INSIDE the source screen.
            // They must not escape this projection and fall behind its backdrop.
            node.ZIndex = 0;
            display.ZIndex = 0;
            DemoUi.ReadOnly(node);
        }
        int drawIndex = 0;
        foreach (var visual in _state.Items.Values.OrderBy(v => v.Z).ThenBy(v => v.Id, StringComparer.Ordinal))
            if (_nodes.TryGetValue(visual.Id, out var display)) MoveChild(display, drawIndex++);
        return true;
    }
    public override void _Process(double delta)
    {
        if (GetParent() is Control parent && _reference.X > 0 && _reference.Y > 0)
            Scale = parent.Size / _reference;
    }
    private T? Asset<T>(string path) where T : Resource
    {
        if (path == "" || !DemoVisualCodec.SafeAsset(path)) return null;
        if (!_assets.TryGetValue(path, out var resource))
        {
            if (_assets.Count >= 2048) _assets.Clear();
            _assets[path] = resource = ResourceLoader.Exists(path) ? ResourceLoader.Load(path) : null;
        }
        return resource as T;
    }
    private Control? CreateVisual(DemoVisual visual)
    {
        if (visual.Kind == 5)
        {
            var template = PreloadManager.Cache.GetScene("res://scenes/screens/card_selection/card_reward_selection_screen.tscn").Instantiate<Control>();
            var banner = template.GetNode<NCommonBanner>("UI/Banner");
            banner.GetParent().RemoveChild(banner);
            foreach (var child in DemoUi.Descendants<Node>(banner))
            { child.Owner = banner; if (child.Name == "Label") child.UniqueNameInOwner = true; }
            template.Free();
            banner.GetNode<MegaLabel>("%Label").SetTextAutoSize(visual.Text);
            return banner;
        }
        if (visual.Kind is 0 or 4)
        {
            var texture = Asset<Texture2D>(visual.Asset);
            if (texture == null) return null;
            if (visual.Region.Length == 4) texture = new AtlasTexture { Atlas = texture,
                Region = new(visual.Region[0], visual.Region[1], visual.Region[2], visual.Region[3]),
                Margin = visual.Margin.Length == 4 ? new(visual.Margin[0], visual.Margin[1], visual.Margin[2], visual.Margin[3]) : default };
            if (visual.Kind == 4 && visual.Patch.Length == 4) return new NinePatchRect { Texture = texture,
                PatchMarginLeft = (int)visual.Patch[0], PatchMarginTop = (int)visual.Patch[1],
                PatchMarginRight = (int)visual.Patch[2], PatchMarginBottom = (int)visual.Patch[3] };
            return new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = (TextureRect.StretchModeEnum)visual.Stretch };
        }
        if (visual.Kind == 3) return new ColorRect { Color = Colors.White };
        var font = Asset<Font>(visual.Font);
        if (visual.Kind == 1)
        {
            var label = new Label { Text = visual.Text, HorizontalAlignment = (HorizontalAlignment)visual.Align,
                VerticalAlignment = (VerticalAlignment)visual.VerticalAlign, AutowrapMode = (TextServer.AutowrapMode)visual.Wrap };
            label.AddThemeFontSizeOverride("font_size", visual.FontSize);
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeConstantOverride("outline_size", visual.OutlineSize);
            label.AddThemeColorOverride("font_outline_color", new(visual.OutlineColor[0], visual.OutlineColor[1], visual.OutlineColor[2], visual.OutlineColor[3]));
            if (font != null) label.AddThemeFontOverride("font", font);
            return label;
        }
        var rich = new MegaRichTextLabel { BbcodeEnabled = true, ScrollActive = false, AutoSizeEnabled = false,
            MinFontSize = visual.FontSize, MaxFontSize = visual.FontSize,
            HorizontalAlignment = (HorizontalAlignment)visual.Align, VerticalAlignment = (VerticalAlignment)visual.VerticalAlign,
            AutowrapMode = (TextServer.AutowrapMode)visual.Wrap };
        foreach (string key in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" })
            rich.AddThemeFontSizeOverride(key, visual.FontSize);
        rich.AddThemeColorOverride("default_color", Colors.White);
        rich.AddThemeConstantOverride("outline_size", visual.OutlineSize);
        rich.AddThemeColorOverride("font_outline_color", new(visual.OutlineColor[0], visual.OutlineColor[1], visual.OutlineColor[2], visual.OutlineColor[3]));
        foreach (string key in new[] { "normal_font", "bold_font", "italics_font", "bold_italics_font" })
            rich.AddThemeFontOverride(key, font ?? ThemeDB.FallbackFont);
        rich.Text = visual.Text;
        return rich;
    }

    public static List<DemoVisual> Capture(Node root, Transform2D inverse, bool requireTree = true)
    {
        var visuals = new List<DemoVisual>();
        var cellNodes = root is NCrystalSphereScreen ? DemoUi.Descendants<NCrystalSphereCell>(root).ToList() : [];
        var cells = cellNodes.Select(c => c.Entity).ToList();
        int order = 0;
        foreach (var node in new[] { root }.Concat(DemoUi.Descendants<Node>(root)))
        {
            if (node is not CanvasItem canvas || (requireTree ? !canvas.IsVisibleInTree() : !canvas.Visible)) continue;
            // The native banner uses its own composed artwork. Send its title and
            // placement once, and instantiate that installed UI component locally.
            bool bannerChild = false;
            for (var parent = node.GetParent(); parent != null && parent != root.GetParent(); parent = parent.GetParent())
                if (parent is NCommonBanner) { bannerChild = true; break; }
            if (bannerChild) continue;
            // The fog shader hides underlying item sprites without changing their
            // Visible flag. Do not put wholly hidden item identity on the network.
            bool hiddenItem = false; CrystalSphereItem? predictionItem = null;
            for (var ancestor = node; ancestor != null && ancestor != root; ancestor = ancestor.GetParent())
                if (ancestor is NCrystalSphereItem itemNode)
                {
                    var item = (CrystalSphereItem?)AccessTools.Field(typeof(NCrystalSphereItem), "_item").GetValue(itemNode);
                    predictionItem = item;
                    hiddenItem = item != null && !cells.Any(c => c.Item == item && !c.IsHidden); break;
                }
            if (hiddenItem) continue;
            if (node.Name.ToString().Contains("ControllerIcon", StringComparison.Ordinal)
                || node is MegaCrit.Sts2.Core.Nodes.Multiplayer.NRemoteMouseCursor) continue;
            if (node is not Control && node is not Sprite2D && node is not AnimatedSprite2D) continue;
            Texture2D? texture = node switch
            {
                TextureRect rect => rect.Texture,
                NinePatchRect patch => patch.Texture,
                TextureButton button => button.TextureNormal,
                Sprite2D sprite => sprite.Texture,
                AnimatedSprite2D sprite when sprite.SpriteFrames != null => sprite.SpriteFrames.GetFrameTexture(sprite.Animation, sprite.Frame),
                _ => null,
            };
            byte kind; string text = "", fontPath = ""; int fontSize = 22, align = 0, vertical = 0, wrap = 0, outlineSize = 0;
            var outlineColor = Colors.Transparent;
            var color = Colors.White;
            Vector2 size = node is Control control ? control.Size : texture?.GetSize() ?? Vector2.One;
            if (node is NCommonBanner banner)
            { kind = 5; text = banner.label.Text; size = size.Max(Vector2.One); }
            else if (texture != null) kind = 0;
            else if (node is Label label)
            {
                kind = 1; text = label.Text; fontPath = FontPath(label.LabelSettings?.Font ?? label.GetThemeFont("font"));
                fontSize = label.LabelSettings?.FontSize ?? label.GetThemeFontSize("font_size");
                color = label.LabelSettings?.FontColor ?? label.GetThemeColor("font_color"); align = (int)label.HorizontalAlignment;
                vertical = (int)label.VerticalAlignment; wrap = (int)label.AutowrapMode;
                outlineSize = label.LabelSettings?.OutlineSize ?? label.GetThemeConstant("outline_size");
                outlineColor = label.LabelSettings?.OutlineColor ?? label.GetThemeColor("font_outline_color");
            }
            else if (node is RichTextLabel rich)
            {
                kind = 2; text = rich.Text; fontPath = FontPath(rich.GetThemeFont("normal_font"));
                fontSize = rich.GetThemeFontSize("normal_font_size"); color = rich.GetThemeColor("default_color");
                align = (int)rich.HorizontalAlignment; vertical = (int)rich.VerticalAlignment; wrap = (int)rich.AutowrapMode;
                outlineSize = rich.GetThemeConstant("outline_size"); outlineColor = rich.GetThemeColor("font_outline_color");
            }
            else if (node is ColorRect solid) { kind = 3; color = solid.Color; }
            else if (node is NCrystalSphereMask) kind = 3;
            else continue;
            if (size.X <= 0 || size.Y <= 0 || text.Length > 4096) continue;
            Rect2? clipRect = null;
            // Respect clipping without reading any GPU pixels.
            if (node is Control clipped)
            {
                bool outside = false;
                for (var parent = node.GetParent(); parent != null && parent != root.GetParent(); parent = parent.GetParent())
                    if (parent is Control { ClipContents: true } mask)
                    {
                        if (!mask.GetGlobalRect().Intersects(clipped.GetGlobalRect())) { outside = true; break; }
                        var maskTransform = inverse * mask.GetGlobalTransformWithCanvas();
                        var bounds = new Rect2(maskTransform.Origin, maskTransform.BasisXform(mask.Size).Abs());
                        clipRect = clipRect is { } existing ? existing.Intersection(bounds) : bounds;
                    }
                if (outside) continue;
            }
            color *= canvas.SelfModulate;
            for (Node? parent = node; parent != null; parent = parent.GetParent()) if (parent is CanvasItem item) color *= item.Modulate;
            if (color.A < .02f) continue;
            var transform = inverse * canvas.GetGlobalTransformWithCanvas();
            if (node is Sprite2D { Centered: true } or AnimatedSprite2D { Centered: true }) transform.Origin -= transform.BasisXform(size / 2);
            string asset = texture?.ResourcePath ?? ""; float[] region = [], margin = [], patchMargins = [];
            if (node is NinePatchRect nine)
            { kind = 4; patchMargins = [nine.PatchMarginLeft, nine.PatchMarginTop, nine.PatchMarginRight, nine.PatchMarginBottom]; }
            if (texture is AtlasTexture atlasTexture)
            {
                asset = atlasTexture.Atlas?.ResourcePath ?? "";
                var bounds = atlasTexture.Region;
                region = [bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y];
                var margins = atlasTexture.Margin;
                margin = [margins.Position.X, margins.Position.Y, margins.Size.X, margins.Size.Y];
            }
            if (kind is 0 or 4 && (asset == "" || !DemoVisualCodec.SafeAsset(asset))) continue;
            if (!DemoVisualCodec.SafeAsset(fontPath)) fontPath = "";
            var material = canvas.Material as ShaderMaterial;
            for (var parent = node.GetParent(); material == null && canvas.UseParentMaterial && parent != null; parent = parent.GetParent())
                if (parent is CanvasItem parentCanvas) material = parentCanvas.Material as ShaderMaterial;
            string shaderPath = material?.Shader?.ResourcePath ?? "";
            if (!DemoVisualCodec.SafeShader(shaderPath)) shaderPath = "";
            var uniforms = new Dictionary<string, float[]>(); var uniformTextures = new Dictionary<string, string>();
            var uniformKinds = new Dictionary<string, int>();
            if (shaderPath != "" && material?.Shader != null)
                foreach (var info in material.Shader.GetShaderUniformList())
                {
                    string parameter = info.AsGodotDictionary()["name"].AsString();
                    if (uniforms.Count >= 32 || parameter.Length > 80) break;
                    var value = material.GetShaderParameter(parameter);
                    switch (value.VariantType)
                    {
                        case Variant.Type.Float: uniforms[parameter] = [parameter == "time" && node is NCrystalSphereMask ? 1 : value.AsSingle()]; break;
                        case Variant.Type.Int: uniforms[parameter] = [value.AsInt32()]; uniformKinds[parameter] = 1; break;
                        case Variant.Type.Bool: uniforms[parameter] = [value.AsBool() ? 1 : 0]; uniformKinds[parameter] = 2; break;
                        case Variant.Type.Vector2: var v2 = value.AsVector2(); uniforms[parameter] = [v2.X, v2.Y]; break;
                        case Variant.Type.Vector3: var v3 = value.AsVector3(); uniforms[parameter] = [v3.X, v3.Y, v3.Z]; break;
                        case Variant.Type.Color: var c = value.AsColor(); uniforms[parameter] = [c.R, c.G, c.B, c.A]; break;
                        case Variant.Type.Vector4: var v4 = value.AsVector4(); uniforms[parameter] = [v4.X, v4.Y, v4.Z, v4.W]; uniformKinds[parameter] = 6; break;
                        case Variant.Type.PackedColorArray:
                            var colors = value.AsColorArray();
                            if (colors.Length <= 32) { uniforms[parameter] = colors.SelectMany(c => new[] { c.R, c.G, c.B, c.A }).ToArray(); uniformKinds[parameter] = 9; } break;
                        case Variant.Type.PackedVector4Array:
                            var vectors = value.AsVector4Array();
                            if (vectors.Length <= 32) { uniforms[parameter] = vectors.SelectMany(v => new[] { v.X, v.Y, v.Z, v.W }).ToArray(); uniformKinds[parameter] = 8; } break;
                        case Variant.Type.PackedVector3Array when parameter == "gridFadeParams":
                            var grid = value.AsVector3Array();
                            if (grid.Length == 121) { uniforms[parameter] = grid.SelectMany(v => new[] { v.Z == 0 ? 1 : v.Y, v.Z == 0 ? 1 : v.Y, 1f }).ToArray(); uniformKinds[parameter] = 7; } break;
                        case Variant.Type.Array when parameter == "gridFadeParams":
                            var values = value.AsGodotArray();
                            if (values.Count == 121) { uniforms[parameter] = values.SelectMany(v => { var p = v.AsVector3(); return new[] { p.Z == 0 ? 1 : p.Y, p.Z == 0 ? 1 : p.Y, 1f }; }).ToArray(); uniformKinds[parameter] = 7; } break;
                        case Variant.Type.Object when value.AsGodotObject() is Texture2D image && DemoVisualCodec.SafeAsset(image.ResourcePath):
                            if (image.ResourcePath != "" && uniformTextures.Count < 8) uniformTextures[parameter] = image.ResourcePath; break;
                    }
                }
            // Preserve native draw-layer ordering, including the prediction fog ABOVE items.
            int z = 0;
            for (Node? parent = node; parent != null; parent = parent.GetParent())
                if (parent is CanvasItem item) { z += item.ZIndex; if (!item.ZAsRelative) break; }
            static float Q(float x) => MathF.Round(x * 2) / 2;
            var visual = new DemoVisual { Id = root.GetPathTo(node).ToString(), Kind = kind,
                Transform = [transform.X.X, transform.X.Y, transform.Y.X, transform.Y.Y, Q(transform.Origin.X), Q(transform.Origin.Y)],
                Size = [size.X, size.Y], Tint = [color.R, color.G, color.B, color.A], Asset = asset, Region = region, Margin = margin, Patch = patchMargins,
                Text = text, Font = fontPath, FontSize = Math.Clamp(fontSize, 1, 180), Align = align, Z = Math.Clamp(z * 128 + order++, -4096, 4096),
                VerticalAlign = vertical, Wrap = wrap, OutlineSize = Math.Clamp(outlineSize, 0, 64),
                OutlineColor = [outlineColor.R, outlineColor.G, outlineColor.B, outlineColor.A],
                Stretch = node is TextureRect textureRect ? (int)textureRect.StretchMode : 0,
                Clip = clipRect is { } clip ? [clip.Position.X, clip.Position.Y, clip.Size.X, clip.Size.Y] : [],
                Shader = shaderPath, Uniforms = uniforms, UniformKinds = uniformKinds, UniformTextures = uniformTextures };
            if (predictionItem != null && cells.Any(c => c.Item == predictionItem && c.IsHidden))
            {
                // Partial rewards may span several cells. Clip every copied visual
                // to revealed cells locally; never uncover the rest of the item.
                foreach (var cell in cellNodes.Where(c => c.Entity.Item == predictionItem && !c.Entity.IsHidden))
                {
                    var rect = inverse * cell.GetGlobalTransformWithCanvas();
                    var cellSize = rect.BasisXform(cell.Size).Abs();
                    visuals.Add(visual with { Id = visual.Id + $"@{cell.Entity.X},{cell.Entity.Y}",
                        Clip = [rect.Origin.X, rect.Origin.Y, cellSize.X, cellSize.Y] });
                    if (visuals.Count >= DemoVisualCodec.MaxVisuals) break;
                }
            }
            else visuals.Add(visual);
            if (visuals.Count >= DemoVisualCodec.MaxVisuals) break;
        }
        return visuals;
    }
    private static string FontPath(Font? font)
    {
        // Locale font substitution creates in-memory FontVariation wrappers.
        // Their empty ResourcePath must not silently select Godot's default font.
        for (int depth = 0; depth < 8 && font != null; depth++)
        {
            if (font.ResourcePath != "" && DemoVisualCodec.SafeAsset(font.ResourcePath)) return font.ResourcePath;
            font = (font as FontVariation)?.BaseFont;
        }
        return "";
    }
    public static bool Same(DemoVisual a, DemoVisual b) => a.Kind == b.Kind && a.Asset == b.Asset && a.Text == b.Text
        && a.Font == b.Font && a.FontSize == b.FontSize && a.Align == b.Align && a.Z == b.Z && a.Stretch == b.Stretch
        && a.VerticalAlign == b.VerticalAlign && a.Wrap == b.Wrap && a.OutlineSize == b.OutlineSize && a.OutlineColor.SequenceEqual(b.OutlineColor)
        && a.Transform.SequenceEqual(b.Transform) && a.Size.SequenceEqual(b.Size) && a.Tint.SequenceEqual(b.Tint)
        && a.Region.SequenceEqual(b.Region) && a.Margin.SequenceEqual(b.Margin) && a.Patch.SequenceEqual(b.Patch)
        && a.Clip.SequenceEqual(b.Clip)
        && a.Shader == b.Shader && a.Uniforms.Count == b.Uniforms.Count
        && a.Uniforms.All(p => b.Uniforms.TryGetValue(p.Key, out var value) && p.Value.SequenceEqual(value))
        && a.UniformKinds.Count == b.UniformKinds.Count
        && a.UniformKinds.All(p => b.UniformKinds.TryGetValue(p.Key, out var value) && p.Value == value)
        && a.UniformTextures.Count == b.UniformTextures.Count
        && a.UniformTextures.All(p => b.UniformTextures.TryGetValue(p.Key, out var value) && p.Value == value);
}
