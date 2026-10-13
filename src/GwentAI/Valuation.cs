using GwentCore;

namespace GwentAI;

/// <summary>
/// Kartların "elde tutma değeri" ve "ileride sağlayabileceği puan" tahminleri.
/// Yalnız KENDİ kartlarına bakmak için kullanılır; rakibin eli bilinmez.
/// </summary>
public static class Valuation
{
    /// <summary>Bir kartı elde tutmanın değeri (kart ekonomisi). Hamle maliyeti bununla ölçülür.</summary>
    public static double CardValue(CardDef d)
    {
        if (d.IsWeather) return d.Id == Ids.ClearWeather ? 3 : d.Id == Ids.SkelligeStorm ? 6 : 5;
        if (d.IsSpecial)
        {
            return d.Id switch
            {
                Ids.Scorch => 8,
                Ids.Horn => 9,
                Ids.Decoy => 5,
                Ids.Mardroeme => 2,
                _ => 3
            };
        }

        var v = (double)d.Strength;
        foreach (var a in d.Abilities)
        {
            v += a switch
            {
                "spy" => 8 - d.Strength, // asıl değeri 2 kart çektirmesi; gücü rakibe yazılır
                "medic" => 4,
                "muster" => 3,
                "morale" => 3,
                "horn" => 3,
                "bond" => 1,
                "scorch" or "scorch_c" or "scorch_r" or "scorch_s" => 3,
                "avenger" => 4,
                _ => 0
            };
        }
        if (d.IsAgile) v += 1;
        if (d.Hero) v += 1;
        return v;
    }

    public static double HandValue(IEnumerable<CardInstance> cards) => cards.Sum(c => CardValue(c.Def));

    /// <summary>Kartın ileride sahaya ekleyebileceği tahmini puan (pas/teslim kararında kullanılır).</summary>
    public static double PotentialPoints(CardDef d)
    {
        if (d.IsUnit)
        {
            if (d.Has("spy")) return 0; // gücü rakibe gider
            var p = (double)d.Strength;
            if (d.Has("medic")) p += 4;
            if (d.Has("morale")) p += 2;
            if (d.Has("horn")) p += 3;
            return p;
        }
        if (d.IsSpecial)
        {
            return d.Id switch { Ids.Scorch => 6, Ids.Horn => 6, Ids.Decoy => 2, _ => 0 };
        }
        return d.IsWeather ? 3 : 0;
    }

    public static double PotentialPoints(IEnumerable<CardInstance> cards) => cards.Sum(c => PotentialPoints(c.Def));
}
