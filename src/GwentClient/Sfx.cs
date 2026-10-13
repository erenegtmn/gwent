using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace GwentClient;

/// <summary>
/// Ses efektleri. Orijinal oyundan çıkarılmış dosyalar varsa onları çalar
/// (<c>assets/sfx/&lt;ad&gt;.wav|mp3</c> — exe'nin yanında ya da %AppData%\Gwent\sfx),
/// yoksa çalışma anında üretilen yer tutucu sesleri kullanır. Ses kartı yoksa ya da kapalıysa sessizce geçer.
/// </summary>
public static class Sfx
{
    private const int Rate = 22050;

    public static bool Enabled { get; set; } = true;
    private static readonly Dictionary<string, string> Files = new();
    private static readonly List<MediaPlayer> Pool = new();
    private static int _next;
    private static string? _tmpDir;

    // ---------- Ayar ----------

    public static void LoadSettings() => Settings.Load();

    public static void SaveSettings() => Settings.Save();

    // ---------- Çalma ----------

    // Oyundaki (yeni) efekt adları -> eski sentezlenmiş yer tutucu adları (dosya yoksa)
    private static readonly Dictionary<string, string> Fallback = new()
    {
        ["close_combat"] = "place", ["long_range"] = "place", ["siege_weapon"] = "place", ["hero"] = "place", ["agile"] = "place",
        ["dummy"] = "place", ["cow_death"] = "place", ["gem_destruction"] = "place",
        ["draw_card"] = "draw", ["draw_2"] = "draw", ["lock_in"] = "pass",
        ["frost"] = "weather", ["fog"] = "weather", ["rain"] = "weather", ["clear_weather"] = "weather", ["ske_tidal_wave"] = "weather",
        ["scorch"] = "scorch", ["horn"] = "horn", ["spy"] = "spy", ["resurrect"] = "medic", ["leader_change"] = "leader",
        ["summon_clones"] = "muster", ["battle_won"] = "round-win", ["battle_lost"] = "round-lose", ["battle_draw"] = "pass",
        ["clash_victory"] = "win", ["clash_defeat"] = "lose", ["coin_toss"] = "coin", ["your_turn"] = "click",
        ["ske_mushroom"] = "horn", ["ske_berserker"] = "horn"
    };
    private static readonly Random Rng = new();
    private static readonly Dictionary<string, List<string>> Variants = new();

    public static void Play(string name)
    {
        if (!Enabled) return;
        try
        {
            var path = Pick(name);
            if (path == null) return;
            if (Pool.Count < 6) Pool.Add(new MediaPlayer());
            var p = Pool[_next++ % Pool.Count];
            p.Volume = Settings.SfxVolume / 100.0;
            p.Stop();
            p.Open(new Uri(path));
            p.Play();
        }
        catch { /* ses cihazı yoksa sessiz */ }
    }

    /// <summary>Orijinal dosya varyantları (name.wav, name_1.wav, name_2.wav ...) içinden rastgele biri; yoksa yer tutucu.</summary>
    private static string? Pick(string name)
    {
        lock (Variants)
        {
            if (!Variants.TryGetValue(name, out var list))
            {
                list = new List<string>();
                var dirs = new List<string>();
                if (Assets.SfxDir != null) dirs.Add(Assets.SfxDir);
                dirs.Add(Path.Combine(AppContext.BaseDirectory, "assets", "sfx"));
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gwent", "sfx"));
                foreach (var dir in dirs.Distinct())
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        var stem = Path.GetFileNameWithoutExtension(f);
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext is not (".wav" or ".mp3")) continue;
                        if (stem == name || (stem.StartsWith(name + "_") && int.TryParse(stem[(name.Length + 1)..], out _)))
                            list.Add(f);
                    }
                    if (list.Count > 0) break;
                }
                Variants[name] = list;
            }
            if (list.Count > 0) return list[Rng.Next(list.Count)];
        }
        // Yer tutucu
        var legacy = Fallback.TryGetValue(name, out var l) ? l : name;
        return Resolve(legacy);
    }

    private static string? Resolve(string name)
    {
        if (Files.TryGetValue(name, out var cached)) return cached;
        var samples = Synthesize(name);
        if (samples == null) return Files[name] = null!;
        _tmpDir ??= Path.Combine(Path.GetTempPath(), "gwent-sfx");
        Directory.CreateDirectory(_tmpDir);
        var file = Path.Combine(_tmpDir, name + ".wav");
        File.WriteAllBytes(file, ToWav(samples));
        return Files[name] = file;
    }

    // ---------- Yer tutucu sentezi ----------

    private static float[]? Synthesize(string name) => name switch
    {
        "place" => Mix(Sweep(150, 55, 170, 0.8f), Noise(22, 0.35f, 0)),
        "draw" => Noise(110, 0.28f, 1, fadeIn: true),
        "pass" => Seq(Tone(330, 110, 0.5f), Tone(247, 160, 0.5f)),
        "weather" => Noise(520, 0.55f, 14, fadeIn: true),
        "scorch" => Mix(Noise(260, 0.6f, 3), Sweep(110, 45, 300, 0.7f)),
        "horn" => Mix(Square(196, 380, 0.28f), Square(294, 380, 0.22f)),
        "spy" => Seq(Tone(392, 70, 0.45f), Tone(494, 70, 0.45f), Tone(587, 130, 0.45f)),
        "medic" => Mix(Tone(880, 420, 0.4f, decay: true), Tone(1320, 420, 0.25f, decay: true)),
        "leader" => Seq(Tone(392, 110, 0.5f), Tone(523, 110, 0.5f), Tone(659, 220, 0.5f)),
        "muster" => Seq(Sweep(150, 70, 90, 0.7f), Sweep(150, 70, 90, 0.7f), Sweep(150, 70, 90, 0.7f), Sweep(180, 70, 140, 0.8f)),
        "round-win" => Seq(Tone(523, 120, 0.5f), Tone(659, 120, 0.5f), Tone(784, 260, 0.5f)),
        "round-lose" => Seq(Tone(392, 140, 0.45f), Tone(311, 140, 0.45f), Tone(262, 300, 0.45f)),
        "win" => Seq(Tone(523, 140, 0.5f), Tone(659, 140, 0.5f), Tone(784, 140, 0.5f), Tone(1047, 480, 0.5f, decay: true)),
        "lose" => Seq(Tone(330, 220, 0.45f), Tone(294, 220, 0.45f), Tone(262, 220, 0.45f), Tone(196, 600, 0.45f, decay: true)),
        "coin" => Seq(Tone(1568, 60, 0.35f, decay: true), Tone(2093, 220, 0.35f, decay: true)),
        "click" => Noise(9, 0.3f, 0),
        _ => null
    };

    private static float[] Tone(double hz, int ms, float vol, bool decay = false)
    {
        var n = Rate * ms / 1000;
        var a = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var env = decay ? Math.Pow(1 - t, 2.2) : Math.Min(1, t * 20) * Math.Min(1, (1 - t) * 8);
            a[i] = (float)(Math.Sin(2 * Math.PI * hz * i / Rate) * env * vol);
        }
        return a;
    }

    private static float[] Square(double hz, int ms, float vol)
    {
        var n = Rate * ms / 1000;
        var a = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var env = Math.Min(1, t * 12) * Math.Min(1, (1 - t) * 6);
            a[i] = (float)((Math.Sin(2 * Math.PI * hz * i / Rate) >= 0 ? 1 : -1) * env * vol);
        }
        return a;
    }

    private static float[] Sweep(double from, double to, int ms, float vol)
    {
        var n = Rate * ms / 1000;
        var a = new float[n];
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var hz = from + (to - from) * t;
            phase += 2 * Math.PI * hz / Rate;
            a[i] = (float)(Math.Sin(phase) * Math.Pow(1 - t, 1.6) * vol);
        }
        return a;
    }

    /// <param name="smooth">Kayan ortalama genişliği: büyüdükçe daha boğuk (düşük geçiren).</param>
    private static float[] Noise(int ms, float vol, int smooth, bool fadeIn = false)
    {
        var n = Rate * ms / 1000;
        var rng = new Random(ms * 31 + smooth);
        var raw = new float[n];
        for (var i = 0; i < n; i++) raw[i] = (float)(rng.NextDouble() * 2 - 1);
        var a = new float[n];
        for (var i = 0; i < n; i++)
        {
            var sum = 0f;
            var cnt = 0;
            for (var k = Math.Max(0, i - smooth); k <= i; k++) { sum += raw[k]; cnt++; }
            var t = (double)i / n;
            var env = fadeIn ? Math.Sin(Math.PI * t) : Math.Pow(1 - t, 2.0);
            a[i] = (float)(sum / cnt * env * vol * (smooth > 0 ? 2.2 : 1));
        }
        return a;
    }

    private static float[] Seq(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private static float[] Mix(params float[][] parts)
    {
        var n = parts.Max(p => p.Length);
        var a = new float[n];
        foreach (var p in parts)
            for (var i = 0; i < p.Length; i++) a[i] += p[i];
        // Katmanlar toplanınca kırpılmasın: tepe 0.9'u aşarsa ölçekle
        var peak = a.Max(Math.Abs);
        if (peak > 0.9f)
            for (var i = 0; i < n; i++) a[i] *= 0.9f / peak;
        return a;
    }

    private static byte[] ToWav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataLen = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataLen);
        foreach (var s in samples) w.Write((short)(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        return ms.ToArray();
    }

    /// <summary>Testler için: yer tutucu sesin geçerli bir WAV üretip üretmediğini döner.</summary>
    internal static byte[]? SynthesizeWav(string name)
    {
        var s = Synthesize(name);
        return s == null ? null : ToWav(s);
    }

    /// <summary>
    /// Sınama: her yer tutucu ses geçerli bir WAV mı ve MediaPlayer onu açabiliyor mu? Sorunların listesini döner.
    /// Ses çıkışı gerekmez; yalnız dosyanın açılabilirliği denetlenir.
    /// </summary>
    internal static List<string> Diagnose()
    {
        var problems = new List<string>();
        foreach (var name in AllNames)
        {
            var wav = SynthesizeWav(name);
            if (wav == null) { problems.Add($"{name}: sentezlenemedi"); continue; }
            if (wav.Length < 100 || wav[0] != 'R' || wav[1] != 'I' || wav[8] != 'W') { problems.Add($"{name}: geçersiz WAV başlığı"); continue; }
            var peak = 0;
            for (var i = 44; i + 1 < wav.Length; i += 2) peak = Math.Max(peak, Math.Abs((short)(wav[i] | (wav[i + 1] << 8))));
            if (peak < 500) problems.Add($"{name}: sessiz (tepe {peak})");
            if (peak >= short.MaxValue) problems.Add($"{name}: kırpılıyor (tepe {peak})");

            var path = Resolve(name);
            if (path == null || !File.Exists(path)) { problems.Add($"{name}: dosya yazılamadı"); continue; }
            var failed = false;
            var player = new MediaPlayer();
            player.MediaFailed += (_, _) => failed = true;
            player.Open(new Uri(path));
            // Açılışın sonuçlanması için mesaj döngüsünü kısa süre döndür
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            if (failed) problems.Add($"{name}: MediaPlayer açamadı");
            player.Close();
        }
        if (Synthesize("yok-boyle-ses") != null) problems.Add("bilinmeyen ad null dönmeli");

        bool OpenOk(string path)
        {
            var failed = false;
            var player = new MediaPlayer();
            player.MediaFailed += (_, _) => failed = true;
            player.Open(new Uri(path));
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            player.Close();
            return !failed;
        }

        foreach (var track in Assets.Music())
            if (!OpenOk(track)) problems.Add("müzik açılamadı: " + Path.GetFileName(track));

        // Oyunun kullandığı her efekt adı çalınabilir bir dosyaya çözülmeli (orijinal dosya ya da yer tutucu)
        foreach (var name in GameNames)
        {
            var path = Pick(name);
            if (path != null && File.Exists(path) && !OpenOk(path)) problems.Add($"{name}: MediaPlayer açamadı ({Path.GetFileName(path)})");
            if (path == null || !File.Exists(path)) problems.Add($"{name}: çözülemedi");
            else if (Assets.Available && Assets.SfxDir != null && Directory.Exists(Assets.SfxDir) && !path.StartsWith(Assets.SfxDir, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{name}: orijinal dosya bulunamadı, yer tutucu çalıyor");
        }
        return problems;
    }

    /// <summary>Oyun ekranının çaldığı efekt adları (GwintView.HandleEvents). Orijinal dosyaları assets/sfx içinde bulunmalı.</summary>
    internal static readonly string[] GameNames =
    {
        "close_combat", "long_range", "siege_weapon", "hero", "spy", "horn", "scorch", "dummy", "frost", "fog", "rain",
        "clear_weather", "ske_tidal_wave", "ske_mushroom", "ske_berserker", "summon_clones", "draw_card", "draw_2", "cow_death",
        "resurrect", "coin_toss", "lock_in", "leader_change", "battle_won", "battle_lost", "battle_draw", "gem_destruction",
        "clash_victory", "clash_defeat", "your_turn"
    };

    internal static readonly string[] AllNames =
    {
        "place", "draw", "pass", "weather", "scorch", "horn", "spy", "medic", "leader", "muster",
        "round-win", "round-lose", "win", "lose", "coin", "click"
    };
}
