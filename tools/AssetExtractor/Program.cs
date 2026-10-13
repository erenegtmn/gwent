using System.Collections;
using System.Reflection;
using WolvenKit.Bundles;
using WolvenKit.Cache;

// Witcher 3 oyun dosyalarından Gwent varlıklarını çıkaran araç. Yalnız kişisel kullanım için.
//   AssetExtractor probe
//   AssetExtractor list <bundle|texture|sound> <altdize> [en-fazla]
//   AssetExtractor extract <bundle|texture|sound> <altdize> <çıkış klasörü>
internal static class Program
{
    private static readonly string Game = Environment.GetEnvironmentVariable("W3_GAME") ?? @"C:\Program Files (x86)\Steam\steamapps\common\The Witcher 3";

    private static int Main(string[] args)
    {
        try
        {
            var exedir = Path.Combine(Game, "bin", "x64_dx12");
            var cmd = args.Length > 0 ? args[0] : "probe";
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (cmd == "probe")
            {
                foreach (var kind in new[] { "bundle", "texture", "sound" })
                {
                    var (mgr, items) = Load(kind, exedir);
                    Console.WriteLine($"[{kind}] {items.Count} dosya  ({sw.Elapsed.TotalSeconds:F1} sn)");
                    foreach (var n in items.Select(i => i.Item1).Where(n => n.Contains("gwint", StringComparison.OrdinalIgnoreCase)).Take(12))
                        Console.WriteLine("   gwint örnek: " + n);
                }
                return 0;
            }

            if (cmd == "cr2w")
            {
                var cr = new WolvenKit.CR2W.CR2WFile();
                using (var fs = File.OpenRead(args[1]))
                using (var br = new BinaryReader(fs))
                    Console.WriteLine("Read: " + cr.Read(br));
                Console.WriteLine("chunks: " + cr.chunks.Count);
                string Dump(object o) => string.Join(" ", o.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string))).Select(p => { try { return p.Name + "=" + p.GetValue(o); } catch { return p.Name + "=?"; } }));
                foreach (var c in cr.chunks.Take(60))
                    Console.WriteLine($"  chunk {c.REDType}  {Dump(c)}");
                try { Console.WriteLine("buffers: " + cr.buffers.Count); foreach (var b in cr.buffers.Take(40)) Console.WriteLine("  buffer " + Dump(b)); } catch (Exception ex) { Console.WriteLine("buffers okunamadi: " + ex.Message); }
                return 0;
            }

            if (cmd == "strings")
            {
                // strings <anahtar-altdizesi> [dil=en]: oyunun yerelleştirme metinlerini anahtar adıyla arar
                var lang = args.Length > 2 ? args[2] : "en";
                var path = Path.Combine(Game, "content", "content0", lang + ".w3strings");
                var file = new WolvenKit.W3Strings.W3StringFile();
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                    file.Read(br);
                Console.WriteLine($"{file.block1.Count} metin, {file.block2.Count} anahtar");
                // blok2: anahtar adı → kimlik; blok1: kimlik → metin (magic ile şifreli olabilir)
                var texts = new Dictionary<uint, string>();
                foreach (dynamic b in file.block1) { try { texts[(uint)b.str_id] = (string)b.str; } catch { } }
                var shown = 0;
                foreach (dynamic kk in file.block2)
                {
                    string key = kk.str_key;
                    if (!key.Contains(args[1], StringComparison.OrdinalIgnoreCase)) continue;
                    uint id = (uint)kk.str_id;
                    Console.WriteLine($"{key} = {(texts.TryGetValue(id, out var t) ? t : "?")}");
                    if (++shown > 400) break;
                }
                return 0;
            }

            if (cmd == "prefix")
            {
                var (_, items) = Load(args[1], exedir);
                var depth = args.Length > 2 ? int.Parse(args[2]) : 2;
                foreach (var g in items.GroupBy(i => string.Join("/", i.Item1.Split((char)92).Take(depth))).OrderByDescending(g => g.Count()).Take(60))
                    Console.WriteLine($"{g.Count(),7}  {g.Key}");
                return 0;
            }

            if (cmd == "stat")
            {
                var (_, items) = Load(args[1], exedir);
                var groups = items.GroupBy(i => System.IO.Path.GetFileName(((dynamic)i.Item2).Bundle.ArchiveAbsolutePath?.ToString() ?? "?")).OrderByDescending(g => g.Count());
                foreach (var g in groups) Console.WriteLine($"{g.Count(),8}  {g.Key}   örnek: {g.First().Item1}");
                return 0;
            }

            var k = args[1];
            var sub = args[2];
            var (m, all) = Load(k, exedir);
            var hits = all.Where(i => i.Item1.Contains(sub, StringComparison.OrdinalIgnoreCase)).ToList();
            if (cmd == "list")
            {
                var max = args.Length > 3 ? int.Parse(args[3]) : 200;
                foreach (var h in hits.Take(max)) Console.WriteLine(h.Item1);
                Console.WriteLine($"-- {hits.Count} eşleşme");
                return 0;
            }
            if (cmd == "extract")
            {
                var outDir = args[3];
                Directory.CreateDirectory(outDir);
                var ok = 0; var fail = 0;
                foreach (var (name, item) in hits)
                {
                    try
                    {
                        var path = Path.Combine(outDir, name.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        using var fs = File.Create(path);
                        item.GetType().GetMethod("Extract", new[] { typeof(Stream) })!.Invoke(item, new object[] { fs });
                        ok++;
                    }
                    catch (Exception ex) { fail++; Console.WriteLine($"HATA {name}: {(ex.InnerException ?? ex).Message}"); }
                }
                Console.WriteLine($"çıkarılan: {ok}, hata: {fail}");
                return fail == 0 ? 0 : 2;
            }
            Console.WriteLine("Bilinmeyen komut");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("HATA: " + ex);
            return 1;
        }
    }

    /// <summary>Yöneticiyi yükler; (ad, öğe) çiftlerinin listesini döner.</summary>
    private static (object, List<(string, object)>) Load(string kind, string exedir)
    {
        object mgr = kind switch
        {
            "bundle" => new BundleManager(),
            "texture" => new TextureManager(),
            "sound" => new SoundManager(),
            _ => throw new ArgumentException("bundle|texture|sound")
        };
        mgr.GetType().GetMethod("LoadAll", new[] { typeof(string) })!.Invoke(mgr, new object[] { exedir });

        var list = new List<(string, object)>();
        // Hepsinde bir "Items" sözlüğü var: ad → liste (ya da tek öğe)
        var itemsProp = mgr.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (itemsProp?.GetValue(mgr) is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict)
            {
                var name = e.Key.ToString()!;
                if (e.Value is IEnumerable en && e.Value is not string)
                    foreach (var it in en) { list.Add((name, it)); break; } // aynı ad birden çok pakette: ilkini al
                else if (e.Value != null) list.Add((name, e.Value));
            }
        }
        else
        {
            Console.WriteLine($"[{kind}] 'Items' bulunamadı; üyeler: " + string.Join(", ", mgr.GetType().GetProperties().Select(p => p.Name)));
        }
        return (mgr, list);
    }
}
