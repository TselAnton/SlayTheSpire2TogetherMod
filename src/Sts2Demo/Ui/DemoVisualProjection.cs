using Godot;
using Sts2Demo.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Sts2Demo.Ui;

/// <summary>Read-only local display primitives using installed assets. No screen scripts are instantiated remotely.</summary>
public partial class DemoVisualProjection : Control
{
    private sealed record BoneReferences(GodotObject Skeleton, (GodotObject Bone, string Name)[] Bones);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node, BoneReferences> BoneCache = new();
    public static List<DemoVisual> CaptureActivity(Node root)
    {
        var room = MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GetNodeOrNull<Node>("RoomContainer");
        if (room == null) return Capture(root, Transform2D.Identity);
        var background = Capture(room, Transform2D.Identity);
        bool insideRoom = room == root || room.IsAncestorOf(root);
        // Shared overlay backstop is a sibling of the active screen, not its child.
        var overlays = MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance;
        var capstones = MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance;
        var result = background.Select(v => v with { Id = "room/" + v.Id }).ToList();
        // Inspectors live above NRun. Preserve the visible card/reward/deck window
        // underneath them, as well as each container's shared dimming backstop.
        var layers = new List<Node>();
        if (!insideRoom)
        {
            if (overlays != null) layers.Add(overlays);
            if (capstones != null) layers.Add(capstones);
            if (!layers.Any(layer => layer == root || layer.IsAncestorOf(root))) layers.Add(root);
        }
        // Native keyword/card/relic tips live in the game's global overlay,
        // outside both the room and inspector. Capture their visible primitives too.
        if (MegaCrit.Sts2.Core.Nodes.NGame.Instance?.HoverTipsContainer is { } tips) layers.Add(tips);
        for (int i = 0; i < layers.Count; i++)
            result.AddRange(Capture(layers[i], Transform2D.Identity).Select(v => v with
                { Id = $"layer{i}/" + v.Id, Z = v.Z + (i + 1) * 134217728 }));
        if (result.Count > DemoVisualCodec.MaxVisuals) throw new InvalidDataException("Activity exceeds visual budget.");
        return result;
    }
    private readonly Dictionary<string, Control> _nodes = [];
    private readonly Dictionary<string, Control> _leaves = [];
    private readonly Dictionary<string, DemoVisual> _rendered = [];
    private readonly Dictionary<string, float> _hoverAmounts = [];
    public Vector2 RemoteCursor { get; set; } = new(-10000, -10000); // normalized
    private readonly Dictionary<string, Resource?> _assets = [];
    private readonly DemoVisualState _state = new();
    private Vector2 _reference = new(1920, 1080);
    public int VisualCount => _nodes.Count;
    public float ChromeOpacity => _state.Screen is "NInspectCardScreen" or "NInspectRelicScreen"
        ? 1 - _state.Items.Values.Where(v => v.Kind == 3 && v.Size[0] >= _reference.X && v.Size[1] >= _reference.Y)
            .Select(v => v.Tint[3]).DefaultIfEmpty(0).Max() : 1;
    public bool Apply(DemoVisualUpdate update, uint sequence)
    {
        bool sameScreen = _state.Screen == update.Screen;
        if (!_state.Apply(update, sequence)) return false;
        _reference = new(update.Reference[0], update.Reference[1]);
        if (update.Full && !sameScreen)
        {
            foreach (var node in _nodes.Values) { RemoveChild(node); node.QueueFree(); }
            _nodes.Clear();
            _leaves.Clear(); _rendered.Clear(); _hoverAmounts.Clear();
        }
        foreach (var id in _nodes.Keys.Where(id => !_state.Items.ContainsKey(id)).ToArray())
            RemoveVisual(id);
        foreach (var visual in update.Items)
        {
            if (_rendered.TryGetValue(visual.Id, out var previous) && SameContent(previous, visual))
            {
                _rendered[visual.Id] = visual;
                continue; // Keep native particles, Spine tracks and text effects alive.
            }
            RemoveVisual(visual.Id);
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
            if (node is NCommonBanner banner)
            { banner.label.SetTextAutoSize(visual.Text); banner.label.Modulate = Colors.White; }
            node.Name = "Visual";
            node.Size = new(visual.Size[0], visual.Size[1]);
            var transform = new Transform2D(new(visual.Transform[0], visual.Transform[1]),
                new(visual.Transform[2], visual.Transform[3]), new(visual.Transform[4], visual.Transform[5]));
            node.Position = transform.Origin; node.Rotation = transform.Rotation; node.Scale = transform.Scale;
            if (display != node) node.Position -= display.Position;
            node.Modulate = new(visual.Tint[0], visual.Tint[1], visual.Tint[2], visual.Tint[3]);
            if (visual.Hover.Length == 9 && visual.Hover[8] == 3) node.Modulate = new Color(node.Modulate, 0);
            if (visual.CanvasMaterial.Length == 6)
            {
                var c = visual.CanvasMaterial;
                node.Material = new CanvasItemMaterial { BlendMode = (CanvasItemMaterial.BlendModeEnum)c[0],
                    LightMode = (CanvasItemMaterial.LightModeEnum)c[1], ParticlesAnimation = c[2] != 0,
                    ParticlesAnimHFrames = c[3], ParticlesAnimVFrames = c[4], ParticlesAnimLoop = c[5] != 0 };
            }
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
            foreach (var mask in visual.AlphaMasks)
            {
                var maskTexture = Asset<Texture2D>(mask.Asset);
                if (maskTexture == null) throw new InvalidDataException("Native alpha mask resource unavailable.");
                var maskTransform = new Transform2D(new(mask.Transform[0], mask.Transform[1]),
                    new(mask.Transform[2], mask.Transform[3]), new(mask.Transform[4], mask.Transform[5]));
                Control wrapper = mask.Patch.Length == 4
                    ? new NinePatchRect { Texture = maskTexture, PatchMarginLeft = mask.Patch[0], PatchMarginTop = mask.Patch[1],
                        PatchMarginRight = mask.Patch[2], PatchMarginBottom = mask.Patch[3] }
                    : new TextureRect { Texture = maskTexture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = (TextureRect.StretchModeEnum)mask.Stretch };
                wrapper.ClipChildren = ClipChildrenMode.Only; wrapper.MouseFilter = MouseFilterEnum.Ignore;
                wrapper.Size = new(mask.Size[0], mask.Size[1]); wrapper.Position = maskTransform.Origin;
                wrapper.Rotation = maskTransform.Rotation; wrapper.Scale = maskTransform.Scale;
                var relative = maskTransform.AffineInverse() * display.GetTransform();
                RemoveChild(display); wrapper.AddChild(display); AddChild(wrapper);
                display.Position = relative.Origin; display.Rotation = relative.Rotation; display.Scale = relative.Scale;
                display = wrapper;
            }
            _nodes[visual.Id] = display;
            _leaves[visual.Id] = node;
            _rendered[visual.Id] = visual;
        }
        var activeGroups = _rendered.Values.Select(v => v.HoverGroup).ToHashSet();
        foreach (var group in _hoverAmounts.Keys.Where(k => !activeGroups.Contains(k)).ToArray()) _hoverAmounts.Remove(group);
        int drawIndex = 0;
        foreach (var visual in _state.Items.Values.OrderBy(v => v.Z).ThenBy(v => v.Id, StringComparer.Ordinal))
            if (_nodes.TryGetValue(visual.Id, out var display)) MoveChild(display, drawIndex++);
        return true;
    }
    public override void _Process(double delta)
    {
        if (GetParent() is Control parent && _reference.X > 0 && _reference.Y > 0)
            Scale = parent.Size / _reference;
        float blend = 1 - MathF.Exp(-24 * (float)delta);
        foreach (var group in _rendered.Values.Where(v => v.Hover.Length == 9).GroupBy(v => v.HoverGroup))
        {
            var h = group.First().Hover;
            bool hovered = new Rect2(h[2], h[3], h[4], h[5]).HasPoint(RemoteCursor * _reference);
            _hoverAmounts.TryGetValue(group.Key, out float amount);
            _hoverAmounts[group.Key] = Mathf.Lerp(amount, hovered ? 1 : 0,
                1 - MathF.Exp(-(hovered ? 60 : 14) * (float)delta));
        }
        foreach (var (id, visual) in _rendered)
        {
            var node = _leaves[id];
            var transform = new Transform2D(new(visual.Transform[0], visual.Transform[1]),
                new(visual.Transform[2], visual.Transform[3]), new(visual.Transform[4], visual.Transform[5]));
            var tint = new Color(visual.Tint[0], visual.Tint[1], visual.Tint[2], visual.Tint[3]);
            float hover = 0;
            if (visual.Hover.Length == 9)
            {
                var h = visual.Hover;
                hover = _hoverAmounts.GetValueOrDefault(visual.HoverGroup);
                var scale = Vector2.One.Lerp(new(h[6], h[7]), hover);
                var pivot = new Vector2(h[0], h[1]);
                transform.Origin = pivot + (transform.Origin - pivot) * scale;
                transform.X *= scale; transform.Y *= scale;
                if (h[8] == 3) tint.A *= hover; // native option outline
            }
            // Wrappers retain native clipping/masks. Express the target in the leaf's parent space.
            var relativeParent = GetGlobalTransform().AffineInverse() * node.GetParent<CanvasItem>().GetGlobalTransform();
            transform = relativeParent.AffineInverse() * transform;
            node.Position = node.Position.Lerp(transform.Origin, blend);
            node.Rotation = Mathf.LerpAngle(node.Rotation, transform.Rotation, blend);
            node.Scale = node.Scale.Lerp(transform.Scale, blend);
            node.Size = new(visual.Size[0], visual.Size[1]);
            node.Modulate = node.Modulate.Lerp(tint, blend);
            if (node.Material is ShaderMaterial material)
                foreach (var (key, values) in visual.Uniforms)
                    if (values.Length == 1 && visual.UniformKinds.GetValueOrDefault(key) == 0)
                    {
                        float target = visual.Hover.Length == 9 && visual.Hover[8] is 2 or 3 && key == "v"
                            ? Mathf.Lerp(.9f, 1.2f, hover) : values[0];
                        material.SetShaderParameter(key, Mathf.Lerp(material.GetShaderParameter(key).AsSingle(), target, blend));
                    }
        }
    }
    private void RemoveVisual(string id)
    {
        if (_nodes.Remove(id, out var node)) { RemoveChild(node); node.QueueFree(); }
        _leaves.Remove(id); _rendered.Remove(id);
    }
    private static bool SameContent(DemoVisual a, DemoVisual b) => Same(a with
    {
        Transform = b.Transform, Size = b.Size, Tint = b.Tint, Z = b.Z, Hover = b.Hover,
        // A running track advances locally; frozen poses and track switches still rebuild.
        SpinePlayback = a.Animation == b.Animation && a.SpinePlayback.Length == 3 && a.SpinePlayback[1] > 0
            && a.SpinePlayback.Take(2).SequenceEqual(b.SpinePlayback.Take(2)) ? b.SpinePlayback : a.SpinePlayback,
        Uniforms = a.Uniforms.ToDictionary(p => p.Key, p => p.Value.Length == 1 && a.UniformKinds.GetValueOrDefault(p.Key) == 0
            && b.Uniforms.TryGetValue(p.Key, out var value) && value.Length == 1 ? value : p.Value)
    }, b);
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
        if (visual.Kind == 7)
        {
            if (visual.Particles.Length != 9 || Asset<Material>(visual.ParticleMaterial) is not { } material) return null;
            var p = visual.Particles;
            var texture = Asset<Texture2D>(visual.Asset);
            if (texture != null && visual.Region.Length == 4) texture = new AtlasTexture { Atlas = texture,
                Region = new(visual.Region[0], visual.Region[1], visual.Region[2], visual.Region[3]) };
            var particles = new GpuParticles2D { ProcessMaterial = material, Texture = texture, UseParentMaterial = true,
                Amount = Math.Clamp((int)p[0], 1, 2048), Lifetime = Math.Clamp(p[1], .01, 60),
                Explosiveness = Math.Clamp(p[2], 0, 1), Randomness = Math.Clamp(p[3], 0, 1), SpeedScale = Math.Clamp(p[4], 0, 4),
                Emitting = p[5] != 0, OneShot = p[6] != 0, LocalCoords = p[7] != 0, Preprocess = Math.Min(p[8], 8) };
            var container = new Control { MouseFilter = MouseFilterEnum.Ignore };
            container.AddChild(particles); return container;
        }
        if (visual.Kind == 6)
        {
            var resource = Asset<Resource>(visual.Asset);
            if (resource == null || resource.GetClass() != "SpineSkeletonDataResource") return null;
            var container = new Control { MouseFilter = MouseFilterEnum.Ignore };
            var sprite = ClassDB.Instantiate("SpineSprite").As<Node2D>();
            foreach (var property in visual.SpineMaterials)
                if (Asset<Material>(property.Value) is { } material) sprite.Set(property.Key, material);
            sprite.Set("skeleton_data_res", resource);
            foreach (var slot in visual.SpineSlots)
            {
                var slotNode = ClassDB.Instantiate("SpineSlotNode").As<Node2D>();
                slotNode.Set("slot_name", slot.Key);
                foreach (var property in slot.Value)
                    if (Asset<Material>(property.Value) is { } material) slotNode.Set(property.Key, material);
                sprite.AddChild(slotNode);
            }
            container.AddChild(sprite);
            var binding = new MegaSprite(sprite);
            container.Ready += () => container.RunWhenSpineReady(binding, state =>
            {
                if (visual.Skin != "") binding.GetSkeleton()?.SetSkinByName(visual.Skin);
                if (visual.Animation != "" && binding.HasAnimation(visual.Animation))
                {
                    var playback = visual.SpinePlayback;
                    var entry = state.SetAnimation(visual.Animation, playback.Length != 3 || playback[0] != 0);
                    if (entry != null && playback.Length == 3)
                    { entry.SetTimeScale(Math.Clamp(playback[1], 0, 4)); entry.SetTrackTime(playback[2]); }
                }
                foreach (string bone in visual.HiddenBones) binding.GetSkeleton()?.FindBone(bone)?.Hide();
            });
            return container;
        }
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
                StretchMode = (TextureRect.StretchModeEnum)visual.Stretch, FlipH = visual.FlipH, FlipV = visual.FlipV };
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
            label.AddThemeConstantOverride("line_spacing", visual.LineSeparation);
            label.AddThemeColorOverride("font_outline_color", new(visual.OutlineColor[0], visual.OutlineColor[1], visual.OutlineColor[2], visual.OutlineColor[3]));
            ApplyShadow(label, visual);
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
        rich.AddThemeConstantOverride("line_separation", visual.LineSeparation);
        rich.AddThemeConstantOverride("paragraph_separation", visual.ParagraphSeparation);
        rich.AddThemeColorOverride("font_outline_color", new(visual.OutlineColor[0], visual.OutlineColor[1], visual.OutlineColor[2], visual.OutlineColor[3]));
        ApplyShadow(rich, visual);
        rich.AddThemeFontOverride("normal_font", font ?? ThemeDB.FallbackFont);
        rich.AddThemeFontOverride("bold_font", Asset<Font>(visual.BoldFont) ?? font ?? ThemeDB.FallbackFont);
        rich.AddThemeFontOverride("italics_font", Asset<Font>(visual.ItalicFont) ?? font ?? ThemeDB.FallbackFont);
        rich.AddThemeFontOverride("bold_italics_font", Asset<Font>(visual.BoldItalicFont) ?? font ?? ThemeDB.FallbackFont);
        rich.Text = visual.Text;
        if (visual.Text.Contains("[ancient_banner]", StringComparison.Ordinal))
        {
            var effect = new MegaCrit.Sts2.Core.RichTextTags.RichTextAncientBanner { Rotation = 1 };
            if (visual.AncientBanner.Length == 3)
            { effect.Rotation = visual.AncientBanner[0]; effect.Spacing = visual.AncientBanner[1]; effect.CenterCharacter = visual.AncientBanner[2]; }
            rich.InstallEffect(effect);
            rich.ParseBbcode(visual.Text);
        }
        return rich;
    }

    private static void ApplyShadow(Control label, DemoVisual visual)
    {
        label.AddThemeColorOverride("font_shadow_color", new(visual.ShadowColor[0], visual.ShadowColor[1], visual.ShadowColor[2], visual.ShadowColor[3]));
        label.AddThemeConstantOverride("shadow_outline_size", visual.Shadow[0]);
        label.AddThemeConstantOverride("shadow_offset_x", visual.Shadow[1]);
        label.AddThemeConstantOverride("shadow_offset_y", visual.Shadow[2]);
    }

    public static List<DemoVisual> Capture(Node root, Transform2D inverse, bool requireTree = true)
    {
        var visuals = new List<DemoVisual>();
        var cellNodes = DemoUi.Descendants<NCrystalSphereCell>(root).ToList();
        var cells = cellNodes.Select(c => c.Entity).ToList();
        int order = 0;
        foreach (var node in DrawOrder(root))
        {
            if (node is not CanvasItem canvas || (requireTree ? !canvas.IsVisibleInTree() : !canvas.Visible)) continue;
            if (canvas.ClipChildren == ClipChildrenMode.Only) continue;
            Control? hoverOwner = null;
            for (var ancestor = node; ancestor != null && ancestor != root.GetParent(); ancestor = ancestor.GetParent())
                if (ancestor is NGridCardHolder or NEventOptionButton)
                { hoverOwner = (Control)ancestor; break; }
            if (hoverOwner is NEventOptionButton { Option.IsLocked: true }) hoverOwner = null;
            bool optionOutline = hoverOwner is NEventOptionButton && node.Name == "Outline";
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
            bool spine = node.GetClass() == "SpineSprite";
            if (node is not Control && node is not Sprite2D && node is not AnimatedSprite2D && node is not GpuParticles2D && !spine) continue;
            Texture2D? texture = node switch
            {
                TextureRect rect => rect.Texture,
                NinePatchRect patch => patch.Texture,
                TextureButton button => button.TextureNormal,
                Sprite2D sprite => sprite.Texture,
                GpuParticles2D particles => particles.Texture,
                AnimatedSprite2D sprite when sprite.SpriteFrames != null => sprite.SpriteFrames.GetFrameTexture(sprite.Animation, sprite.Frame),
                // Some game scripts derive from Control but are attached to a
                // native TextureRect (notably banners). C# 'is TextureRect' is false.
                _ when node.IsClass("TextureRect") => node.Get("texture").AsGodotObject() as Texture2D,
                _ => null,
            };
            byte kind; string text = "", fontPath = ""; int fontSize = 22, align = 0, vertical = 0, wrap = 0, outlineSize = 0;
            var outlineColor = Colors.Transparent;
            var shadowColor = Colors.Transparent;
            int[] shadow = [0, 0, 0];
            var color = Colors.White;
            Vector2 size = node is Control control ? control.Size : texture?.GetSize() ?? Vector2.One;
            if (spine) kind = 6;
            else if (node is GpuParticles2D) kind = 7;
            else if (texture != null) kind = 0;
            else if (node is Label label)
            {
                kind = 1; text = label.Text; fontPath = FontPath(label.LabelSettings?.Font ?? label.GetThemeFont("font"));
                fontSize = label.LabelSettings?.FontSize ?? label.GetThemeFontSize("font_size");
                color = label.LabelSettings?.FontColor ?? label.GetThemeColor("font_color"); align = (int)label.HorizontalAlignment;
                vertical = (int)label.VerticalAlignment; wrap = (int)label.AutowrapMode;
                outlineSize = label.LabelSettings?.OutlineSize ?? label.GetThemeConstant("outline_size");
                outlineColor = label.LabelSettings?.OutlineColor ?? label.GetThemeColor("font_outline_color");
                shadowColor = label.LabelSettings?.ShadowColor ?? label.GetThemeColor("font_shadow_color");
                shadow = [label.LabelSettings?.ShadowSize ?? label.GetThemeConstant("shadow_outline_size"),
                    (int)(label.LabelSettings?.ShadowOffset.X ?? label.GetThemeConstant("shadow_offset_x")),
                    (int)(label.LabelSettings?.ShadowOffset.Y ?? label.GetThemeConstant("shadow_offset_y"))];
            }
            else if (node is RichTextLabel rich)
            {
                kind = 2; text = rich.Text; fontPath = FontPath(rich.GetThemeFont("normal_font"));
                fontSize = rich.GetThemeFontSize("normal_font_size"); color = rich.GetThemeColor("default_color");
                align = (int)rich.HorizontalAlignment; vertical = (int)rich.VerticalAlignment; wrap = (int)rich.AutowrapMode;
                outlineSize = rich.GetThemeConstant("outline_size"); outlineColor = rich.GetThemeColor("font_outline_color");
                shadowColor = rich.GetThemeColor("font_shadow_color");
                shadow = [rich.GetThemeConstant("shadow_outline_size"), rich.GetThemeConstant("shadow_offset_x"), rich.GetThemeConstant("shadow_offset_y")];
            }
            else if (node.IsClass("ColorRect")) { kind = 3; color = node.Get("color").AsColor(); }
            else if (node is NCrystalSphereMask) kind = 3;
            else continue;
            if (size.X <= 0 || size.Y <= 0 || text.Length > 4096) continue;
            Rect2? clipRect = null;
            var alphaMasks = new List<DemoAlphaMask>();
            for (var ancestor = node.GetParent(); ancestor != null && ancestor != root.GetParent(); ancestor = ancestor.GetParent())
            {
                if (ancestor is not CanvasItem { ClipChildren: not ClipChildrenMode.Disabled } mask) continue;
                // CanvasGroup's internal clip mode composites its children's union;
                // it is not a texture alpha stencil supplied by the parent.
                if (mask is CanvasGroup || mask.IsClass("CanvasGroup")) continue;
                // Empty Control containers have no own alpha silhouette; their
                // drawable descendant mask is captured separately (native HP bars).
                if (mask.GetClass() == "Control") continue;
                if ((!mask.IsClass("TextureRect") && mask is not NinePatchRect) || mask.Get("texture").AsGodotObject() is not Texture2D maskTexture
                    || !DemoVisualCodec.SafeAsset(maskTexture.ResourcePath) || maskTexture.ResourcePath == "")
                    throw new InvalidDataException($"Unsupported native alpha mask: {mask.GetPath()} ({mask.GetClass()}).");
                var maskTransform = inverse * mask.GetGlobalTransformWithCanvas();
                var maskSize = mask.Get("size").AsVector2();
                alphaMasks.Add(new DemoAlphaMask { Asset = maskTexture.ResourcePath, Size = [maskSize.X, maskSize.Y],
                    Transform = [maskTransform.X.X, maskTransform.X.Y, maskTransform.Y.X, maskTransform.Y.Y, maskTransform.Origin.X, maskTransform.Origin.Y],
                    Stretch = mask is NinePatchRect ? 0 : mask.Get("stretch_mode").AsInt32(),
                    Patch = mask is NinePatchRect maskPatch ? [maskPatch.PatchMarginLeft, maskPatch.PatchMarginTop, maskPatch.PatchMarginRight, maskPatch.PatchMarginBottom] : [] });
                if (alphaMasks.Count > 4) throw new InvalidDataException("Native mask depth exceeds budget.");
            }
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
            if (color.A < .02f && !optionOutline) continue;
            var transform = inverse * canvas.GetGlobalTransformWithCanvas();
            if (node is Sprite2D { Centered: true } or AnimatedSprite2D { Centered: true }) transform.Origin -= transform.BasisXform(size / 2);
            string asset = texture?.ResourcePath ?? ""; float[] region = [], margin = [], patchMargins = [];
            string animation = "";
            float[] spinePlayback = [];
            var spineMaterials = new Dictionary<string, string>();
            var spineSlots = new Dictionary<string, Dictionary<string, string>>();
            string[] hiddenBones = []; string skin = "";
            if (spine)
            {
                asset = (node.Get("skeleton_data_res").AsGodotObject() as Resource)?.ResourcePath ?? "";
                var state = new MegaSprite(node).TryGetAnimationState();
                // Native get_current can return an Object variant containing null.
                var track = state == null ? null : SpineCall(state.BoundObject, "get_current", 0).AsGodotObject();
                animation = track == null ? "" : new MegaTrackEntry(track).GetAnimationName();
                if (track != null)
                {
                    bool loop = !track.HasMethod("get_loop") || SpineCall(track, "get_loop").AsBool();
                    float speed = track.HasMethod("get_time_scale") ? SpineCall(track, "get_time_scale").AsSingle() : 1;
                    if (state?.BoundObject.HasMethod("get_time_scale") == true)
                        speed *= SpineCall(state.BoundObject, "get_time_scale").AsSingle();
                    // Frozen/native one-shot poses must not turn into endlessly looping animations.
                    // Idle loops advance locally and do not create clock-only network updates.
                    var entry = new MegaTrackEntry(track);
                    float time = !loop || speed == 0 ? Math.Min(entry.GetTrackTime(), entry.GetAnimationEnd()) : 0;
                    spinePlayback = [loop ? 1 : 0, Math.Clamp(speed, 0, 4), Math.Clamp(MathF.Round(time * 10) / 10, 0, 3600)];
                }
                if (!DemoVisualCodec.SafeAsset(asset) || asset == "") continue;
                spineMaterials = CaptureSpineMaterials(node);
                foreach (var slot in node.GetChildren().Where(n => n.GetClass() == "SpineSlotNode"))
                    spineSlots[slot.Get("slot_name").AsString()] = CaptureSpineMaterials(slot);
                var skeleton = new MegaSprite(node).GetSkeleton()?.BoundObject;
                if (skeleton?.HasMethod("get_bones") == true)
                {
                    // Reuse bone wrappers for this skeleton. Enumerating every bone
                    // every sample creates many native signal subscriptions and GC
                    // finalizers while the source sprite is still rendering.
                    if (!BoneCache.TryGetValue(node, out var references) || references.Skeleton != skeleton)
                    {
                        var bones = SpineCall(skeleton, "get_bones").AsGodotArray().Select(v => v.AsGodotObject())
                            .Select(b => (b, SpineCall(SpineCall(b, "get_data").AsGodotObject(), "get_name").AsString())).ToArray();
                        references = new(skeleton, bones); BoneCache.Remove(node); BoneCache.Add(node, references);
                    }
                    hiddenBones = references.Bones.Where(b => SpineCall(b.Bone, "get_scale_x").AsSingle() == 0
                        && SpineCall(b.Bone, "get_scale_y").AsSingle() == 0).Select(b => b.Name).Take(128).ToArray();
                }
                if (skeleton?.HasMethod("get_skin") == true)
                {
                    var currentSkin = SpineCall(skeleton, "get_skin").AsGodotObject();
                    skin = currentSkin == null ? "" : SpineCall(currentSkin, "get_name").AsString();
                }
            }
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
            var materialOwner = canvas;
            while (materialOwner.UseParentMaterial && materialOwner.GetParent() is CanvasItem parentCanvas)
                materialOwner = parentCanvas;
            var material = materialOwner.Material as ShaderMaterial;
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
            var ancientEffect = (node as RichTextLabel)?.CustomEffects.Select(v => v.AsGodotObject())
                .OfType<MegaCrit.Sts2.Core.RichTextTags.RichTextAncientBanner>().FirstOrDefault();
            var visual = new DemoVisual { Id = root.GetPathTo(node).ToString(), Kind = kind,
                Transform = [transform.X.X, transform.X.Y, transform.Y.X, transform.Y.Y, Q(transform.Origin.X), Q(transform.Origin.Y)],
                Size = [size.X, size.Y], Tint = [color.R, color.G, color.B, color.A], Asset = asset, Animation = animation, SpinePlayback = spinePlayback,
                SpineMaterials = spineMaterials, SpineSlots = spineSlots, HiddenBones = hiddenBones, Skin = skin,
                ParticleMaterial = node is GpuParticles2D pm ? pm.ProcessMaterial?.ResourcePath ?? "" : "",
                Particles = node is GpuParticles2D pg ? [pg.Amount, (float)pg.Lifetime, pg.Explosiveness, pg.Randomness, (float)pg.SpeedScale,
                    pg.Emitting ? 1 : 0, pg.OneShot ? 1 : 0, pg.LocalCoords ? 1 : 0, (float)Math.Max(pg.Preprocess, pg.OneShot ? 0 : Math.Min(pg.Lifetime, 8))] : [],
                Region = region, Margin = margin, Patch = patchMargins,
                Text = text, Font = fontPath, FontSize = Math.Clamp(fontSize, 1, 180), Align = align, Z = Math.Clamp(z, -4096, 4096) * 16384 + order++,
                AncientBanner = ancientEffect == null ? [] : [ancientEffect.Rotation, ancientEffect.Spacing, ancientEffect.CenterCharacter],
                BoldFont = node is RichTextLabel richBold ? FontPath(richBold.GetThemeFont("bold_font")) : "",
                ItalicFont = node is RichTextLabel richItalic ? FontPath(richItalic.GetThemeFont("italics_font")) : "",
                BoldItalicFont = node is RichTextLabel richBoth ? FontPath(richBoth.GetThemeFont("bold_italics_font")) : "",
                FlipH = node is Sprite2D { FlipH: true } or TextureRect { FlipH: true } or AnimatedSprite2D { FlipH: true },
                FlipV = node is Sprite2D { FlipV: true } or TextureRect { FlipV: true } or AnimatedSprite2D { FlipV: true },
                VerticalAlign = vertical, Wrap = wrap, OutlineSize = Math.Clamp(outlineSize, 0, 64),
                LineSeparation = node is RichTextLabel rt ? rt.GetThemeConstant("line_separation")
                    : node is Label lt ? lt.LabelSettings?.LineSpacing is float spacing ? (int)spacing : lt.GetThemeConstant("line_spacing") : 0,
                ParagraphSeparation = node is RichTextLabel rp ? rp.GetThemeConstant("paragraph_separation") : 0,
                OutlineColor = [outlineColor.R, outlineColor.G, outlineColor.B, outlineColor.A],
                ShadowColor = [shadowColor.R, shadowColor.G, shadowColor.B, shadowColor.A], Shadow = shadow,
                Stretch = node.IsClass("TextureRect") ? node.Get("stretch_mode").AsInt32() : 0,
                Clip = clipRect is { } clip ? [clip.Position.X, clip.Position.Y, clip.Size.X, clip.Size.Y] : [],
                AlphaMasks = alphaMasks,
                Shader = shaderPath, Uniforms = uniforms, UniformKinds = uniformKinds, UniformTextures = uniformTextures };
            if (materialOwner.Material is CanvasItemMaterial canvasMaterial)
                visual = visual with { CanvasMaterial = [(int)canvasMaterial.BlendMode, (int)canvasMaterial.LightMode,
                    canvasMaterial.ParticlesAnimation ? 1 : 0, canvasMaterial.ParticlesAnimHFrames,
                    canvasMaterial.ParticlesAnimVFrames, canvasMaterial.ParticlesAnimLoop ? 1 : 0] };
            if (hoverOwner != null && alphaMasks.Count == 0
                && Math.Abs(hoverOwner.Scale.X) > .001f && Math.Abs(hoverOwner.Scale.Y) > .001f)
            {
                // Remove the source client's hover tween from the geometry. Only
                // its stable layout and hit area cross the wire; the observer's
                // existing 10 Hz cursor descriptor drives the local animation.
                var current = inverse * hoverOwner.GetGlobalTransformWithCanvas();
                var normal = current;
                var small = hoverOwner is NGridCardHolder holder ? holder.SmallScale : Vector2.One;
                var large = hoverOwner is NGridCardHolder grid
                    ? (Vector2)AccessTools.Property(grid.GetType(), "HoverScale").GetValue(grid)! : Vector2.One * 1.01f;
                normal.X *= small.X / hoverOwner.Scale.X; normal.Y *= small.Y / hoverOwner.Scale.Y;
                normal.Origin += current.BasisXform(hoverOwner.PivotOffset) - normal.BasisXform(hoverOwner.PivotOffset);
                var neutral = normal * current.AffineInverse() * transform;
                var hitbox = hoverOwner is NGridCardHolder cardHolder ? (Control)cardHolder.Hitbox : hoverOwner;
                var hitTransform = normal * current.AffineInverse() * inverse * hitbox.GetGlobalTransformWithCanvas();
                var rect = hitTransform * new Rect2(Vector2.Zero, hitbox.Size);
                var pivot = normal * hoverOwner.PivotOffset;
                if (optionOutline)
                {
                    var glow = StsColors.blueGlow;
                    float opacity = 1;
                    for (Node? p = hoverOwner; p != null; p = p.GetParent()) if (p is CanvasItem item) opacity *= item.Modulate.A;
                    visual = visual with { Tint = [glow.R, glow.G, glow.B, opacity] };
                }
                if (hoverOwner is NEventOptionButton && visual.Uniforms.ContainsKey("v")) visual.Uniforms["v"] = [.9f];
                visual = visual with { HoverGroup = hoverOwner.GetPath().ToString(),
                    Hover = [pivot.X, pivot.Y, rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y,
                        large.X / small.X, large.Y / small.Y, optionOutline ? 3 : hoverOwner is NEventOptionButton ? 2 : 1],
                    Transform = [neutral.X.X, neutral.X.Y, neutral.Y.X, neutral.Y.Y, Q(neutral.Origin.X), Q(neutral.Origin.Y)] };
            }
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
            if (visuals.Count >= DemoVisualCodec.MaxVisuals) throw new InvalidDataException("Screen exceeds visual budget.");
        }
        return visuals;
    }
    private static IEnumerable<Node> DrawOrder(Node node)
    {
        // The merchant hand is decorative, cursor-driven and has no gameplay state.
        // Do not replay its sampled motion; the watched cursor still identifies the item.
        if (node is NMerchantHand) yield break;
        var children = node.GetChildren();
        // Godot renders behind-parent children first (e.g. the dark relic outline).
        // Plain tree traversal incorrectly paints that outline over its icon.
        foreach (var child in children.Where(c => c is CanvasItem { ShowBehindParent: true }))
            foreach (var descendant in DrawOrder(child)) yield return descendant;
        yield return node;
        foreach (var child in children.Where(c => c is not CanvasItem { ShowBehindParent: true }))
            foreach (var descendant in DrawOrder(child)) yield return descendant;
    }
    private static Variant SpineCall(GodotObject receiver, string method, params Variant[] args)
    {
        // Spine creates sole-owned RefCounted wrappers. Root them through the
        // native call, matching the game's MegaSpineBinding lifetime contract.
        try { return receiver.Call(method, args); }
        finally { GC.KeepAlive(receiver); GC.KeepAlive(args); }
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
    private static Dictionary<string, string> CaptureSpineMaterials(Node node)
    {
        var result = new Dictionary<string, string>();
        foreach (string key in new[] { "normal_material", "additive_material", "multiply_material", "screen_material" })
            if (node.Get(key).AsGodotObject() is Material material && material.ResourcePath != "" && DemoVisualCodec.SafeAsset(material.ResourcePath))
                result[key] = material.ResourcePath;
        return result;
    }
    public static bool Same(DemoVisual a, DemoVisual b) => a.Kind == b.Kind && a.Asset == b.Asset && a.Animation == b.Animation && a.Text == b.Text
        && a.HoverGroup == b.HoverGroup && a.Hover.SequenceEqual(b.Hover)
        && a.CanvasMaterial.SequenceEqual(b.CanvasMaterial)
        && a.SpinePlayback.SequenceEqual(b.SpinePlayback)
        && a.ParticleMaterial == b.ParticleMaterial && a.Particles.SequenceEqual(b.Particles)
        && a.AncientBanner.SequenceEqual(b.AncientBanner)
        && a.Skin == b.Skin && a.HiddenBones.SequenceEqual(b.HiddenBones)
        && a.SpineMaterials.Count == b.SpineMaterials.Count && a.SpineMaterials.All(p => b.SpineMaterials.TryGetValue(p.Key, out var path) && path == p.Value)
        && a.SpineSlots.Count == b.SpineSlots.Count && a.SpineSlots.All(p => b.SpineSlots.TryGetValue(p.Key, out var slot)
            && p.Value.Count == slot.Count && p.Value.All(m => slot.TryGetValue(m.Key, out var path) && path == m.Value))
        && a.Font == b.Font && a.FontSize == b.FontSize && a.Align == b.Align && a.Z == b.Z && a.Stretch == b.Stretch
        && a.BoldFont == b.BoldFont && a.ItalicFont == b.ItalicFont && a.BoldItalicFont == b.BoldItalicFont && a.FlipH == b.FlipH && a.FlipV == b.FlipV
        && a.VerticalAlign == b.VerticalAlign && a.Wrap == b.Wrap && a.OutlineSize == b.OutlineSize && a.OutlineColor.SequenceEqual(b.OutlineColor)
        && a.Shadow.SequenceEqual(b.Shadow) && a.ShadowColor.SequenceEqual(b.ShadowColor)
        && a.AlphaMasks.Count == b.AlphaMasks.Count && a.AlphaMasks.Zip(b.AlphaMasks).All(m => m.First.Asset == m.Second.Asset
            && m.First.Stretch == m.Second.Stretch && m.First.Patch.SequenceEqual(m.Second.Patch)
            && m.First.Transform.SequenceEqual(m.Second.Transform) && m.First.Size.SequenceEqual(m.Second.Size))
        && a.LineSeparation == b.LineSeparation && a.ParagraphSeparation == b.ParagraphSeparation
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
