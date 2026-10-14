using GwentAI.Draft;
using GwentCore;
using GwentNet;
using GwentServer;

namespace GwentServer.Tests;

/// <summary>Online draft: oda kuran açar, iki oyuncu sunucunun yürüttüğü draft ile deste kurar, sonra draft desteleriyle oynar.</summary>
public class DraftRoomTests
{
    private static (RoomService Svc, FakeClient Host, FakeClient Guest) Room()
    {
        var svc = new RoomService(null, () => 1234);
        var host = new FakeClient(svc, "Geralt", seed: 3);
        var guest = new FakeClient(svc, "Yennefer", seed: 4);
        var code = host.Create().Value!;
        Assert.True(guest.Join(code).Ok);
        FakeClient.Pump(host, guest);
        return (svc, host, guest);
    }

    /// <summary>Oyuncunun draft'ını sunucu üzerinden baştan sona yapar (her seferinde teklifin ilkini seçer).</summary>
    private static void DraftAll(RoomService svc, FakeClient c, string faction)
    {
        Assert.True(svc.DraftChoose(c.Session, "faction", faction).Ok);
        c.Pump();
        Assert.True(svc.DraftChoose(c.Session, "leader", c.State!.YourDraft!.LeaderOffer[0]).Ok);
        c.Pump();
        while (c.State!.YourDraft!.Picks.Count < DraftLogic.DeckSize)
        {
            Assert.True(svc.DraftChoose(c.Session, "card", c.State.YourDraft.Offer[0], "avatar_geralt").Ok);
            c.Pump();
        }
    }

    [Fact]
    public void Only_the_host_turns_draft_on_and_both_must_draft_before_ready()
    {
        var (svc, host, guest) = Room();
        Assert.False(svc.SetDraft(guest.Session, true).Ok);
        Assert.True(guest.Choose("nilfgaard").Ok);
        Assert.True(svc.SetDraft(host.Session, true).Ok);
        FakeClient.Pump(host, guest);
        Assert.True(host.State!.Draft && guest.State!.Draft);
        Assert.Null(guest.State.Players[1].Loadout);                                  // önceki seçim sıfırlandı
        Assert.False(guest.Ready().Ok);                                              // draft bitmeden hazır olunmaz
        Assert.False(guest.Choose("monsters").Ok);                                   // draft odasında deste elle kurulmaz
        Assert.False(svc.DraftChoose(guest.Session, "card", "ballista").Ok);        // önce fraksiyon
    }

    [Fact]
    public void Server_refuses_picks_outside_the_offer_and_shows_progress_to_the_opponent()
    {
        var (svc, host, guest) = Room();
        svc.SetDraft(host.Session, true);
        Assert.True(svc.DraftChoose(host.Session, "faction", "northern_realms").Ok);
        host.Pump();
        var offer = host.State!.YourDraft!.LeaderOffer;
        var outside = CardCatalog.Default.Leaders("northern_realms").First(l => !offer.Contains(l.Id) && l.Edition != "nextgen");
        Assert.False(svc.DraftChoose(host.Session, "leader", outside.Id).Ok);
        Assert.True(svc.DraftChoose(host.Session, "leader", offer[0]).Ok);
        host.Pump();
        var notOffered = DeckValidator.Pool("northern_realms").First(d => !host.State!.YourDraft!.Offer.Contains(d.Id)).Id;
        Assert.False(svc.DraftChoose(host.Session, "card", notOffered).Ok);
        Assert.True(svc.DraftChoose(host.Session, "card", host.State!.YourDraft!.Offer[1 % host.State.YourDraft.Offer.Count]).Ok);
        FakeClient.Pump(host, guest);
        Assert.Equal(1, guest.State!.Players[0].DraftPicks);                          // rakip ilerlemeyi görür
        Assert.Equal("northern_realms", guest.State.Players[0].DraftFaction);
        Assert.Null(guest.State.YourDraft);                                           // rakibin teklifleri görünmez
        Assert.False(svc.DraftChoose(host.Session, "faction", "monsters").Ok);       // fraksiyon değişmez
    }

    [Fact]
    public void Drafted_decks_are_played_and_rematch_keeps_them()
    {
        var (svc, host, guest) = Room();
        svc.SetDraft(host.Session, true);
        DraftAll(svc, host, "skellige");
        DraftAll(svc, guest, "scoiatael");
        FakeClient.Pump(host, guest);
        var hostDeck = host.State!.YourDraft!.Picks;
        Assert.Equal(hostDeck, host.State.Players[0].Loadout!.CardIds);
        Assert.Equal("avatar_geralt", host.State.Players[0].Loadout!.Avatar);
        Assert.True(host.Ready().Ok);
        Assert.True(guest.Ready().Ok);
        FakeClient.Pump(host, guest);
        Assert.NotNull(host.Setup);
        Assert.Equal(hostDeck, host.Setup!.Seat0.CardIds);
        Assert.Equal("scoiatael", host.Setup.Seat1.Faction);
        for (var i = 0; i < 4000 && host.Result == null; i++) { FakeClient.Pump(host, guest); host.TryPlay(); guest.TryPlay(); }
        FakeClient.Pump(host, guest);
        Assert.NotNull(host.Result);
        Assert.True(svc.RequestRematch(host.Session).Ok);
        Assert.True(svc.RequestRematch(guest.Session).Ok);
        FakeClient.Pump(host, guest);
        Assert.Equal(hostDeck, host.Setup!.Seat0.CardIds);                            // rövanş aynı draft desteleriyle
    }
}
