using GwentNet;

namespace GwentServer.Tests;

public class InviteCodeTests
{
    [Theory]
    [InlineData("http://192.168.0.100:54669/gwent", "B6FBE")]
    [InlineData("http://10.0.0.7:5000/gwent", "AB23C")]
    [InlineData("http://203.0.113.9:65535/gwent", "ZZZZZ")]
    [InlineData("https://quiet-river-fox-abc.trycloudflare.com/gwent", "K7M2Q")]
    [InlineData("https://evaluate-cheap-manager-wrapping.trycloudflare.com/gwent", "T9CGF")]
    [InlineData("https://room-42-b7.trycloudflare.com/gwent", "T9CGF")]     // rakamlı tünel adı
    [InlineData("https://odd_name.trycloudflare.com/gwent", "T9CGF")]       // tünel alfabesi dışı: yedek tür
    [InlineData("http://192.168.0.100:54669/gwent", "ABCD0")]              // sunucu alfabesi dışı oda kodu: eski biçim
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
        // Gerçek bir tünel adı uzunluğunda (4 kelime): harf başına tek karakter + 8 karakter başlık/sağlama
        var text = InviteCodes.Encode("https://evaluate-cheap-manager-wrapping.trycloudflare.com/gwent", "T9CGF");
        Assert.True(text.Replace("-", "").Length <= "GWENT".Length + 31 + 8, text);
        var lan = InviteCodes.Encode("http://192.168.0.100:54669/gwent", "B6FBE");
        Assert.True(lan.Length <= 27, lan);
    }

    [Fact]
    public void Codes_made_by_the_old_format_still_decode()
    {
        // Eski sürümün (v1) ürettiği tünel kodu: oda kodu ASCII + tür 4 + tünel adı baytları + 8 bit sağlama
        var body = new List<byte>();
        body.AddRange(System.Text.Encoding.ASCII.GetBytes("K7M2Q"));
        body.Add(4);
        body.AddRange(System.Text.Encoding.ASCII.GetBytes("quiet-river-fox-abc"));
        var sum = 0x5a;
        foreach (var b in body) sum = ((sum << 1) | (sum >> 7)) & 0xff ^ b;
        body.Add((byte)sum);
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var sb = new System.Text.StringBuilder("GWENT-");
        int buffer = 0, bits = 0;
        foreach (var b in body)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 31]);
        Assert.True(InviteCodes.TryDecode(sb.ToString(), out var inv, out var err), err);
        Assert.Equal("https://quiet-river-fox-abc.trycloudflare.com/gwent", inv.Url);
        Assert.Equal("K7M2Q", inv.RoomCode);
    }

    [Fact]
    public void Every_single_typo_and_adjacent_swap_in_a_tunnel_code_is_caught()
    {
        const string url = "https://evaluate-cheap-manager-wrapping.trycloudflare.com/gwent";
        var clean = InviteCodes.Encode(url, "T9CGF").Replace("-", "")[5..];
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        for (var i = 0; i < clean.Length; i++)
        {
            foreach (var c in alphabet.Where(c => c != clean[i]))
            {
                var bad = clean[..i] + c + clean[(i + 1)..];
                Assert.False(InviteCodes.TryDecode("GWENT" + bad, out _, out _), $"yakalanmadı: {i} → {c}");
            }
            if (i + 1 < clean.Length && clean[i] != clean[i + 1])
            {
                var swap = clean[..i] + clean[i + 1] + clean[i] + clean[(i + 2)..];
                Assert.False(InviteCodes.TryDecode("GWENT" + swap, out _, out _), $"yer değiştirme yakalanmadı: {i}");
            }
        }
    }

    [Fact]
    public void A_bare_room_code_explains_that_the_invite_code_is_needed()
    {
        Assert.False(InviteCodes.TryDecode(" t9cgf ", out _, out var err));
        Assert.Contains("davet kodunu", err);
    }

    [Fact]
    public void Encode_rejects_a_room_code_of_the_wrong_length()
    {
        Assert.Throws<ArgumentException>(() => InviteCodes.Encode("http://1.2.3.4:5000/gwent", "ABC"));
    }
}
