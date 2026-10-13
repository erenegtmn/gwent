using GwentCore;
using Xunit;

namespace GwentCore.Tests;

public class SetupTests
{
    [Fact]
    public void Card_catalog_loads_and_has_expected_shape()
    {
        var cat = CardCatalog.Default;
        Assert.Equal(185, cat.All.Count);
        Assert.Equal(cat.All.Count, cat.All.Select(c => c.Id).Distinct().Count());
        Assert.Equal(22, cat.All.Count(c => c.IsLeader));
        foreach (var f in new[] { "northern_realms", "nilfgaard", "scoiatael", "monsters" })
            Assert.Equal(5, cat.Leaders(f).Count());
        Assert.Equal(2, cat.Leaders("skellige").Count());
        Assert.Equal(16, cat.Hash.Length);
        // Her kartın zorunlu alanları
        Assert.All(cat.All.Where(c => c.IsUnit), c => Assert.NotNull(c.Row));
        Assert.All(cat.All.Where(c => c.Summons != null), c => Assert.True(cat.TryGet(c.Summons!, out _)));
        Assert.All(cat.All.Where(c => c.TransformsInto != null), c => Assert.True(cat.TryGet(c.TransformsInto!, out _)));
    }

    [Theory]
    [InlineData("northern_realms")]
    [InlineData("nilfgaard")]
    [InlineData("scoiatael")]
    [InlineData("monsters")]
    [InlineData("skellige")]
    public void Default_deck_is_valid_for_every_faction(string faction)
    {
        var errs = DeckValidator.Validate(DeckValidator.DefaultDeck(faction));
        Assert.Empty(errs);
    }

    [Fact]
    public void Deck_validation_rules()
    {
        var d = DeckValidator.DefaultDeck("nilfgaard");
        Assert.Contains(DeckValidator.Validate(d with { CardIds = d.CardIds.Take(10).ToList() }), e => e.Contains("22"));
        Assert.Contains(DeckValidator.Validate(d with { LeaderId = "king_bran" }), e => e.Contains("lider"));
        Assert.Contains(DeckValidator.Validate(d with { CardIds = d.CardIds.Append("ballista").ToList() }), e => e.Contains("kullanılamaz"));
        Assert.Contains(DeckValidator.Validate(d with { CardIds = d.CardIds.Concat(Enumerable.Repeat("scorch", 5)).ToList() }), e => e.Contains("kopya") || e.Contains("özel"));
        Assert.Contains(DeckValidator.Validate(d with { CardIds = d.CardIds.Append("bovine_defense_force").ToList() }), e => e.Contains("eklenemez"));
        Assert.Contains(DeckValidator.Validate(d with { CardIds = d.CardIds.Append("foltest_king_of_temeria").ToList() }), e => e.Contains("lider"));
        Assert.Contains(DeckValidator.Validate(d with { CardIds = d.CardIds.Append("mardroeme").ToList() }), e => e.Contains("kullanılamaz"));
    }

    [Fact]
    public void Special_cards_are_capped_at_ten()
    {
        var d = DeckValidator.DefaultDeck("nilfgaard");
        var units = d.CardIds.Where(id => CardCatalog.Default.Get(id).IsUnit).ToList();
        var many = units.Concat(new[] { "decoy", "decoy", "decoy", "scorch", "scorch", "scorch", "commanders_horn", "commanders_horn", "commanders_horn", "biting_frost", "biting_frost" }).ToList();
        Assert.Contains(DeckValidator.Validate(d with { CardIds = many }), e => e.Contains("10"));
    }

    [Fact]
    public void Next_gen_content_is_off_by_default()
    {
        var d = DeckValidator.DefaultDeck("northern_realms");
        var withRoach = d with { CardIds = d.CardIds.Append(Ids.Roach).ToList() };
        Assert.Contains(DeckValidator.Validate(withRoach), e => e.Contains("yeni nesil"));
        Assert.DoesNotContain(DeckValidator.Validate(withRoach, new GameOptions { NextGen = true }), e => e.Contains("yeni nesil"));
    }

    [Fact]
    public void Geralt_only_musters_roach_in_next_gen_mode()
    {
        var off = T.New();
        var g1 = off.TestHand(0, "geralt_of_rivia");
        var r1 = off.TestHand(0, "roach");
        T.Filler(off, 0);
        T.Play(off, 0, g1);
        Assert.Contains(r1, off.Players[0].Hand);

        var on = T.New(options: new GameOptions { NextGen = true });
        var g2 = on.TestHand(0, "geralt_of_rivia");
        var r2 = on.TestHand(0, "roach");
        T.Filler(on, 0);
        T.Play(on, 0, g2);
        Assert.Contains(r2, on.Players[0].Row(RowKind.Close).Cards);
    }

    [Fact]
    public void New_game_deals_ten_cards_and_waits_for_mulligans()
    {
        var d = DeckValidator.DefaultDeck("northern_realms");
        var g = new Game(d, DeckValidator.DefaultDeck("monsters"), 5);
        Assert.Equal(Phase.Mulligan, g.Phase);
        Assert.All(g.Players, p => Assert.Equal(10, p.Hand.Count));
        T.Fails(g, new PlayCard(0, g.Players[0].Hand[0].Uid), "oynanamaz");
    }

    [Fact]
    public void Invalid_deck_is_rejected_by_the_constructor()
    {
        var d = DeckValidator.DefaultDeck("northern_realms");
        Assert.Throws<ArgumentException>(() => new Game(d with { CardIds = new[] { "ballista" } }, d, 1));
    }

    [Fact]
    public void Mulligan_swaps_up_to_two_cards_and_starts_when_both_are_done()
    {
        var g = new Game(DeckValidator.DefaultDeck("northern_realms"), DeckValidator.DefaultDeck("monsters"), 5);
        var hand = g.Players[0].Hand;
        var three = hand.Take(3).Select(c => c.Uid).ToList();
        T.Fails(g, new Mulligan(0, three), "En fazla 2");
        T.Fails(g, new Mulligan(0, new[] { 9999 }), "elinde değil");
        var keep = hand.Skip(2).Select(c => c.Uid).ToHashSet();
        T.Ok(g, new Mulligan(0, hand.Take(2).Select(c => c.Uid).ToList()));
        Assert.Equal(10, g.Players[0].Hand.Count);
        Assert.Equal(keep.Count, g.Players[0].Hand.Count(c => keep.Contains(c.Uid)));
        T.Fails(g, new Mulligan(0, Array.Empty<int>()), "zaten");
        Assert.Equal(Phase.Mulligan, g.Phase);
        T.Ok(g, new Mulligan(1, Array.Empty<int>()));
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.Equal(1, g.Round);
    }

    [Fact]
    public void Scoiatael_chooses_who_goes_first()
    {
        var g = new Game(DeckValidator.DefaultDeck("monsters"), DeckValidator.DefaultDeck("scoiatael"), 5);
        T.Ok(g, new Mulligan(0, Array.Empty<int>()));
        T.Ok(g, new Mulligan(1, Array.Empty<int>()));
        Assert.Equal(Phase.ChooseFirst, g.Phase);
        Assert.Equal(1, g.Current);
        T.Fails(g, new ChooseFirst(0, 0), "yapamazsın");
        T.Ok(g, new ChooseFirst(1, 0));
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.Equal(0, g.Current);
    }

    [Theory]
    [InlineData("northern_realms", false)]
    [InlineData("nilfgaard", true)]
    [InlineData("scoiatael", false)]
    [InlineData("monsters", true)]
    [InlineData("skellige", false)]
    public void Recommended_deck_is_valid_and_playable(string faction, bool nextGen)
    {
        var opts = new GameOptions { NextGen = nextGen };
        var deck = DeckValidator.RecommendedDeck(faction, opts);
        Assert.Empty(DeckValidator.Validate(deck, opts));
        var units = deck.CardIds.Count(id => CardCatalog.Default.Get(id).IsUnit);
        Assert.InRange(units, 22, 40);
        // oyun kurulabiliyor
        var g = new Game(deck, DeckValidator.DefaultDeck("monsters"), 3, opts);
        Assert.Equal(10, g.Players[0].Hand.Count);
    }

    [Fact]
    public void Pool_contains_faction_and_neutral_cards_only()
    {
        var pool = DeckValidator.Pool("skellige");
        Assert.All(pool, d => Assert.True(d.Faction is "skellige" or "neutral"));
        Assert.DoesNotContain(pool, d => d.Token || d.IsLeader);
        Assert.DoesNotContain(pool, d => d.Id == Ids.Roach);
        Assert.Contains(DeckValidator.Pool("skellige", new GameOptions { NextGen = true }), d => d.Id == Ids.Roach);
        Assert.Contains(pool, d => d.Id == Ids.Mardroeme);
        Assert.DoesNotContain(DeckValidator.Pool("monsters"), d => d.Id == Ids.Mardroeme);
    }
}
