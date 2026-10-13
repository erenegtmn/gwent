namespace GwentCore;

public static class DeckValidator
{
    public const int MinUnits = 22;
    public const int MaxSpecials = 10;

    /// <summary>Boş liste = geçerli. Mesajlar Türkçe ve kullanıcıya gösterilebilir.</summary>
    public static List<string> Validate(Deck deck, GameOptions? options = null, CardCatalog? catalog = null)
    {
        options ??= new GameOptions();
        catalog ??= CardCatalog.Default;
        var errors = new List<string>();

        if (!catalog.TryGet(deck.LeaderId, out var leader) || !leader.IsLeader)
        {
            errors.Add("Geçerli bir lider seçilmedi.");
        }
        else if (leader.Faction != deck.Faction)
        {
            errors.Add($"{leader.Name} bu fraksiyonun lideri değil.");
        }
        else if (leader.Edition == "nextgen" && !options.NextGen)
        {
            errors.Add($"{leader.Name} yalnız yeni nesil modunda kullanılabilir.");
        }

        var counts = new Dictionary<string, int>();
        var units = 0;
        var specials = 0;
        foreach (var id in deck.CardIds)
        {
            if (!catalog.TryGet(id, out var def))
            {
                errors.Add($"Bilinmeyen kart: {id}");
                continue;
            }
            counts[id] = counts.GetValueOrDefault(id) + 1;
            if (def.IsLeader) { errors.Add($"{def.Name} lider kartıdır, desteye eklenemez."); continue; }
            if (def.Token) { errors.Add($"{def.Name} desteye eklenemez."); continue; }
            if (def.Edition == "nextgen" && !options.NextGen) { errors.Add($"{def.Name} yalnız yeni nesil modunda kullanılabilir."); continue; }
            if (def.Faction != "neutral" && def.Faction != deck.Faction)
                errors.Add($"{def.Name} bu fraksiyonda kullanılamaz.");
            if (def.IsUnit) units++; else specials++;
        }

        foreach (var (id, n) in counts)
        {
            if (catalog.TryGet(id, out var def) && n > def.Copies)
                errors.Add($"{def.Name}: en fazla {def.Copies} kopya kullanılabilir (destede {n}).");
        }

        if (units < MinUnits) errors.Add($"Destede en az {MinUnits} birim kartı olmalı (şu an {units}).");
        if (specials > MaxSpecials) errors.Add($"Destede en fazla {MaxSpecials} özel kart olabilir (şu an {specials}).");
        return errors;
    }

    /// <summary>Test ve yapay zekâ için basit, geçerli bir deste: fraksiyonun tüm birimleri + birkaç özel kart.</summary>
    public static Deck DefaultDeck(string faction, GameOptions? options = null, CardCatalog? catalog = null)
    {
        options ??= new GameOptions();
        catalog ??= CardCatalog.Default;
        var leader = catalog.Leaders(faction).First(l => l.Edition != "nextgen" || options.NextGen);
        var ids = new List<string>();

        void AddAll(IEnumerable<CardDef> defs)
        {
            foreach (var d in defs)
                for (var i = 0; i < d.Copies; i++) ids.Add(d.Id);
        }

        bool Allowed(CardDef d) => !d.Token && !d.IsLeader && (d.Edition != "nextgen" || options.NextGen);

        AddAll(catalog.All.Where(d => d.IsUnit && d.Faction == faction && Allowed(d)));
        // Yetersizse tarafsız birimlerle tamamla.
        if (ids.Count < MinUnits)
            AddAll(catalog.All.Where(d => d.IsUnit && d.Faction == "neutral" && Allowed(d)).Take(MinUnits));
        foreach (var sid in new[] { Ids.Decoy, Ids.Scorch, Ids.Horn, Ids.ClearWeather, Ids.BitingFrost, Ids.ImpenetrableFog, Ids.TorrentialRain })
            ids.Add(sid);
        if (faction == "skellige") ids.Add(Ids.Mardroeme);
        return new Deck(faction, leader.Id, ids);
    }

    /// <summary>Bir fraksiyonun destesine girebilecek kartlar (lider, jeton ve kapalı yeni nesil içerik hariç).</summary>
    public static List<CardDef> Pool(string faction, GameOptions? options = null, CardCatalog? catalog = null)
    {
        options ??= new GameOptions();
        catalog ??= CardCatalog.Default;
        return catalog.All
            .Where(d => !d.IsLeader && !d.Token && (d.Edition != "nextgen" || options.NextGen)
                        && (d.Faction == faction || d.Faction == "neutral"))
            .ToList();
    }

    /// <summary>
    /// "Önerilen" deste: tüm kahramanlar, en fazla 3 casus ve en güçlü birimlerle ~24 birim; toplanma gruplarını bozmaz.
    /// Birkaç temel özel kart ekler. Her zaman geçerlidir.
    /// </summary>
    public static Deck RecommendedDeck(string faction, GameOptions? options = null, CardCatalog? catalog = null, string? leaderId = null)
    {
        options ??= new GameOptions();
        catalog ??= CardCatalog.Default;
        var leader = leaderId != null ? catalog.Get(leaderId)
            : catalog.Leaders(faction).First(l => l.Edition != "nextgen" || options.NextGen);

        var units = Pool(faction, options, catalog).Where(d => d.IsUnit).ToList();
        var chosen = new Dictionary<string, int>(); // kart kimliği → adet
        int Count() => chosen.Values.Sum();
        void Take(CardDef d)
        {
            var add = d.Copies - chosen.GetValueOrDefault(d.Id);
            if (add > 0) chosen[d.Id] = d.Copies;
        }

        foreach (var d in units.Where(d => d.Hero)) Take(d);
        foreach (var d in units.Where(d => d.Has("spy") && !d.Hero).OrderByDescending(d => d.Strength).Take(3)) Take(d);
        foreach (var d in units.Where(d => !d.Hero && !d.Has("spy")).OrderByDescending(d => d.Strength * d.Copies == 0 ? 0 : d.Strength + (d.Has("bond") ? d.Copies : 0)))
        {
            if (Count() >= 24) break;
            Take(d);
        }
        // Toplanma grupları bütün olarak gelir.
        foreach (var g in units.Where(d => chosen.ContainsKey(d.Id) && d.MusterGroup != null).Select(d => d.MusterGroup!).Distinct().ToList())
            foreach (var d in units.Where(d => d.MusterGroup == g)) Take(d);
        // Yetersizse (nadiren) en güçlülerle tamamla.
        foreach (var d in units.OrderByDescending(d => d.Strength))
        {
            if (Count() >= MinUnits) break;
            Take(d);
        }

        var ids = chosen.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        foreach (var sid in new[] { Ids.Decoy, Ids.Scorch, Ids.Horn, Ids.ClearWeather })
            ids.Add(sid);
        return new Deck(faction, leader.Id, ids);
    }
}
