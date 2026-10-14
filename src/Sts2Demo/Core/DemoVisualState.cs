using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sts2Demo.Core;

public sealed record DemoAlphaMask
{
    public string Asset { get; init; } = "";
    public float[] Transform { get; init; } = [1, 0, 0, 1, 0, 0];
    public float[] Size { get; init; } = [1, 1];
    public int Stretch { get; init; }
    public int[] Patch { get; init; } = [];
}

// Asset references and scalar UI state only. Never pixels, scripts, models or actions.
public sealed record DemoVisual
{
    public string Id { get; init; } = "";
    public byte Kind { get; init; } // texture, plain text, rich text, solid rectangle
    public float[] Transform { get; init; } = [1, 0, 0, 1, 0, 0];
    public float[] Size { get; init; } = [1, 1];
    public float[] Tint { get; init; } = [1, 1, 1, 1];
    public string Asset { get; init; } = "";
    public string Animation { get; init; } = "";
    public float[] SpinePlayback { get; init; } = [];
    public string ParticleMaterial { get; init; } = "";
    public float[] Particles { get; init; } = [];
    public Dictionary<string, string> SpineMaterials { get; init; } = [];
    public Dictionary<string, Dictionary<string, string>> SpineSlots { get; init; } = [];
    public string[] HiddenBones { get; init; } = [];
    public string Skin { get; init; } = "";
    public float[] Region { get; init; } = [];
    public float[] Margin { get; init; } = [];
    public float[] Patch { get; init; } = [];
    public int Stretch { get; init; }
    public float[] Clip { get; init; } = [];
    public List<DemoAlphaMask> AlphaMasks { get; init; } = [];
    public string Shader { get; init; } = "";
    public Dictionary<string, float[]> Uniforms { get; init; } = [];
    public Dictionary<string, int> UniformKinds { get; init; } = [];
    public Dictionary<string, string> UniformTextures { get; init; } = [];
    public string Text { get; init; } = "";
    public float[] AncientBanner { get; init; } = [];
    public string Font { get; init; } = "";
    public string BoldFont { get; init; } = "";
    public string ItalicFont { get; init; } = "";
    public string BoldItalicFont { get; init; } = "";
    public bool FlipH { get; init; }
    public bool FlipV { get; init; }
    public int FontSize { get; init; } = 22;
    public int Align { get; init; }
    public int VerticalAlign { get; init; }
    public int Wrap { get; init; }
    public int OutlineSize { get; init; }
    public int LineSeparation { get; init; }
    public int ParagraphSeparation { get; init; }
    public float[] OutlineColor { get; init; } = [0, 0, 0, 0];
    public float[] ShadowColor { get; init; } = [0, 0, 0, 0];
    public int[] Shadow { get; init; } = [0, 0, 0]; // outline size, offset x, offset y
    public int Z { get; init; }
}

public sealed record DemoVisualUpdate
{
    public string Screen { get; init; } = "";
    public uint Base { get; init; }
    public bool Full { get; init; }
    public float[] Reference { get; init; } = [1920, 1080];
    public List<DemoVisual> Items { get; init; } = [];
    public List<string> Removed { get; init; } = [];
}

public static class DemoVisualCodec
{
    public const int MaxVisuals = 1024;
    public const int MaxDecodedBytes = 768 * 1024;
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 12 };
    public static bool SafeAsset(string path) => path != null && path.Length <= 512 && !path.Contains("..") && !path.Contains('\\')
        && (path == "" || path.StartsWith("res://images/", StringComparison.Ordinal)
            || path.StartsWith("res://atlases/", StringComparison.Ordinal)
            || path.StartsWith("res://fonts/", StringComparison.Ordinal)
            || path.StartsWith("res://animations/", StringComparison.Ordinal)
            || path.StartsWith("res://materials/", StringComparison.Ordinal)
            || path.StartsWith("res://scenes/", StringComparison.Ordinal));
    public static bool SafeShader(string path) => path != null && path.Length <= 512 && !path.Contains("..") && !path.Contains('\\')
        && (path == "" || path.StartsWith("res://shaders/", StringComparison.Ordinal) || path.StartsWith("res://scenes/", StringComparison.Ordinal));
    public static bool SafeText(string text) => text != null && text.Length <= 4096
        && Regex.Matches(text, @"\[img[^\]]*\](.*?)\[/img\]", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Cast<Match>().All(m => SafeAsset(m.Groups[1].Value.Trim()));
    public static bool Valid(DemoVisualUpdate update) => update != null && update.Screen != null && update.Screen.Length <= 80
        && update.Reference != null && update.Reference.Length == 2 && update.Reference.All(x => float.IsFinite(x) && x is > 0 and <= 8192)
        && update.Items != null && update.Removed != null
        && update.Items.Count <= MaxVisuals && update.Removed.Count <= MaxVisuals
        && update.Removed.All(x => x != null && x.Length <= 256)
        && update.Items.All(v => v != null && v.Id != null && v.Id.Length <= 256 && v.Kind <= 7 && SafeAsset(v.Asset) && SafeAsset(v.Font)
            && SafeAsset(v.ParticleMaterial) && v.Particles != null && v.Particles.Length is 0 or 9
            && v.Particles.All(x => float.IsFinite(x) && x is >= 0 and <= 2048)
            && SafeAsset(v.BoldFont) && SafeAsset(v.ItalicFont) && SafeAsset(v.BoldItalicFont)
            && v.Animation != null && v.Animation.Length <= 128
            && v.SpinePlayback != null && v.SpinePlayback.Length is 0 or 3
            && v.SpinePlayback.All(x => float.IsFinite(x) && x is >= 0 and <= 3600)
            && ValidSpineMaterials(v.SpineMaterials) && v.SpineSlots != null && v.SpineSlots.Count <= 32
            && v.SpineSlots.All(s => s.Key.Length <= 128 && ValidSpineMaterials(s.Value))
            && v.HiddenBones != null && v.HiddenBones.Length <= 128 && v.HiddenBones.All(b => b != null && b.Length <= 128)
            && v.Skin != null && v.Skin.Length <= 128
            && SafeShader(v.Shader) && v.Uniforms != null && v.UniformKinds != null && v.UniformTextures != null && v.Uniforms.Count <= 32 && v.UniformTextures.Count <= 8
            && v.UniformKinds.Count <= 32 && v.UniformKinds.All(p => p.Key.Length <= 80 && p.Value is >= 0 and <= 9)
            && v.Uniforms.All(p => p.Key.Length <= 80 && p.Value != null && p.Value.Length <= 363 && p.Value.All(x => float.IsFinite(x) && Math.Abs(x) <= 65536))
            && v.UniformTextures.All(p => p.Key.Length <= 80 && SafeAsset(p.Value))
            && SafeText(v.Text) && v.FontSize is > 0 and <= 180 && v.Align is >= 0 and <= 3 && v.Stretch is >= 0 and <= 6
            && v.AncientBanner != null && v.AncientBanner.Length is 0 or 3 && v.AncientBanner.All(x => float.IsFinite(x) && Math.Abs(x) <= 4096)
            && v.VerticalAlign is >= 0 and <= 3 && v.Wrap is >= 0 and <= 3 && v.OutlineSize is >= 0 and <= 64
            && v.LineSeparation is >= -64 and <= 128 && v.ParagraphSeparation is >= -64 and <= 128
            && v.OutlineColor != null && v.OutlineColor.Length == 4 && v.OutlineColor.All(x => float.IsFinite(x) && x is >= 0 and <= 1)
            && v.AlphaMasks != null && v.AlphaMasks.Count <= 4 && v.AlphaMasks.All(m => m != null && SafeAsset(m.Asset)
                && m.Asset.Length > 0 && m.Stretch is >= 0 and <= 6 && m.Transform != null && m.Transform.Length == 6
                && m.Size != null && m.Size.Length == 2 && m.Size.All(x => x > 0)
                && m.Patch != null && m.Patch.Length is 0 or 4 && m.Patch.All(x => x is >= 0 and <= 4096)
                && m.Transform.Concat(m.Size).All(x => float.IsFinite(x) && Math.Abs(x) <= 65536))
            && v.ShadowColor != null && v.ShadowColor.Length == 4 && v.ShadowColor.All(x => float.IsFinite(x) && x is >= 0 and <= 1)
            && v.Shadow != null && v.Shadow.Length == 3 && v.Shadow[0] is >= 0 and <= 64 && v.Shadow.All(x => x is >= -64 and <= 64)
            && v.Transform != null && v.Size != null && v.Tint != null && v.Region != null && v.Margin != null && v.Patch != null && v.Clip != null
            && v.Transform.Length == 6 && v.Size.Length == 2 && v.Tint.Length == 4
            && v.Region.Length is 0 or 4 && v.Margin.Length is 0 or 4 && v.Patch.Length is 0 or 4 && v.Clip.Length is 0 or 4
            && v.Transform.Concat(v.Size).Concat(v.Tint).Concat(v.Region).Concat(v.Margin).Concat(v.Patch).Concat(v.Clip)
                .All(x => float.IsFinite(x) && Math.Abs(x) <= 65536));
    private static bool ValidSpineMaterials(Dictionary<string, string>? materials) => materials != null && materials.Count <= 4
        && materials.All(p => p.Key is "normal_material" or "additive_material" or "multiply_material" or "screen_material" && SafeAsset(p.Value));
    public static byte[] Encode(DemoVisualUpdate update)
    {
        if (!Valid(update)) throw new InvalidDataException("Invalid UI metadata.");
        var raw = JsonSerializer.SerializeToUtf8Bytes(update, Json);
        if (raw.Length > MaxDecodedBytes) throw new InvalidDataException("UI metadata exceeds limit.");
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Fastest, true)) compressor.Write(raw);
        return output.ToArray();
    }
    public static DemoVisualUpdate Decode(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] block = new byte[4096]; int count;
        while ((count = decompressor.Read(block)) > 0)
        {
            if (output.Length + count > MaxDecodedBytes) throw new InvalidDataException("UI metadata expansion exceeds limit.");
            output.Write(block, 0, count);
        }
        var update = JsonSerializer.Deserialize<DemoVisualUpdate>(output.ToArray(), Json)
            ?? throw new InvalidDataException("Empty UI metadata.");
        if (!Valid(update)) throw new InvalidDataException("Invalid UI metadata.");
        return update;
    }
}

public sealed class DemoVisualState
{
    public Dictionary<string, DemoVisual> Items { get; } = [];
    public string Screen { get; private set; } = "";
    public uint Sequence { get; private set; }
    public bool Apply(DemoVisualUpdate update, uint sequence)
    {
        if (!DemoVisualCodec.Valid(update) || Sequence != 0 && !DemoSession.Newer(sequence, Sequence)) return false;
        if (!update.Full && (update.Base != Sequence || update.Screen != Screen)) return false;
        if (update.Full) Items.Clear();
        foreach (var id in update.Removed) Items.Remove(id);
        foreach (var visual in update.Items) Items[visual.Id] = visual;
        if (Items.Count > DemoVisualCodec.MaxVisuals) { Items.Clear(); Sequence = 0; return false; }
        Screen = update.Screen; Sequence = sequence;
        return true;
    }
}
