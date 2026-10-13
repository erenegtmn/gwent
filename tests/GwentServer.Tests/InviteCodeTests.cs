using GwentNet;

namespace GwentServer.Tests;

public class InviteCodeTests
{
    [Theory]
    [InlineData("http://192.168.0.100:54669/gwent", "B6FBE")]
    [InlineData("http://10.0.0.7:5000/gwent", "AB23C")]
    [InlineData("http://203.0.113.9:65535/gwent", "ZZZZZ")]
    [InlineData("https://quiet-river-fox-abc.trycloudflare.com/gwent", "K7M2Q")]
    [InlineData("https://gwent.example.org/gwent", "K7M2Q")]
    [InlineData("http://host.example.org:8080/gwent", "K7M2Q")]       // yedek tür (tam adres)
    [InlineData("http://192.168.1.5:5000/baska", "K7M2Q")]           // farklı yol
    public void Encode_then_decode_returns_the_same_invite(string url, string code)
    {
        var text = InviteCodes.Encode(url, code);
        Assert.StartsWith("GWENT-", text);
        Assert.True(InviteCodes.TryDecode(text, out var inv, out var err), err);
        Assert.Equal(url, inv.Url);
        Assert.Equal(code, inv.RoomCode);
    }

    [Fact]
    public void Decoding_ignores_case_dashes_spaces_and_confusable_letters()
    {
        var text = InviteCodes.Encode("http://192.168.0.100:54669/gwent", "B6FBE");
        var messy = "  " + text.ToLowerInvariant().Replace("-", "  ") + " ";
        Assert.True(InviteCodes.TryDecode(messy, out var inv, out _));
        Assert.Equal("B6FBE", inv.RoomCode);

        // O yerine 0, I/L yerine 1 yazılmış olsa da çözülür: kodu böyle bozup deneyelim
        var swapped = text.Replace('0', 'O').Replace('1', 'l');
        Assert.True(InviteCodes.TryDecode(swapped, out var inv2, out var err2), err2);
        Assert.Equal(inv, inv2);
    }

    [Fact]
    public void A_typo_is_caught_by_the_checksum_or_rejected()
    {
        var text = InviteCodes.Encode("http://192.168.0.100:54669/gwent", "B6FBE");
        var chars = text.ToCharArray();
        var firstData = text.IndexOf('-') + 1;
        var bad = 0;
        for (var i = firstData; i < chars.Length; i++)
        {
            if (chars[i] == '-') continue;
            var original = chars[i];
            chars[i] = original == '7' ? '8' : '7';
            if (!InviteCodes.TryDecode(new string(chars), out var inv, out _) || inv.RoomCode != "B6FBE" || inv.Url != "http://192.168.0.100:54669/gwent") bad++;
            chars[i] = original;
        }
        Assert.True(bad > 0);
        // tek karakter hatası neredeyse her zaman yakalanmalı
        var total = text.Count(c => c != '-') - 5;
        Assert.True(bad >= total - 1, $"yakalanan {bad}/{total}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("merhaba")]
    [InlineData("192.168.0.1:5000")]
    [InlineData("GWENT-")]
    [InlineData("GWENT-ABC")]
    [InlineData("GWENT-UUUUU-UUUUU")]
    public void Garbage_is_rejected_with_a_message(string input)
    {
        Assert.False(InviteCodes.TryDecode(input, out _, out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void Tunnel_invites_are_short_enough_to_paste()
    {
        var text = InviteCodes.Encode("https://quiet-river-fox-abc.trycloudflare.com/gwent", "K7M2Q");
        Assert.True(text.Length < 90, text);
        var lan = InviteCodes.Encode("http://192.168.0.100:54669/gwent", "B6FBE");
        Assert.True(lan.Length < 40, lan);
    }

    [Fact]
    public void Encode_rejects_a_room_code_of_the_wrong_length()
    {
        Assert.Throws<ArgumentException>(() => InviteCodes.Encode("http://1.2.3.4:5000/gwent", "ABC"));
    }
}
