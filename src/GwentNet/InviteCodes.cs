using System.Text;

namespace GwentNet;

/// <summary>Bir odaya katılmak için gereken her şey: sunucunun hub adresi ve oda kodu.</summary>
public readonly record struct Invite(string Url, string RoomCode);

/// <summary>
/// Tek parça, kopyalanıp yapıştırılabilir davet kodu: <c>GWENT-XXXXX-XXXXX-…</c>. İçinde adres (IPv4+port ya da internet adresi),
/// oda kodu ve yazım hatası denetimi vardır. Büyük/küçük harf, tire ve boşluk fark etmez; karışan harfler (O/0, I/L/1) düzeltilir.
/// </summary>
public static class InviteCodes
{
    private const string Prefix = "GWENT";
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32 (I, L, O, U yok)
    private const string TryCloudflare = ".trycloudflare.com";

    private const byte TypeIpv4 = 1;      // http://a.b.c.d:port/gwent
    private const byte TypeHttps = 2;     // https://host/gwent (443)
    private const byte TypeFull = 3;      // her adres (yedek)
    private const byte TypeTunnel = 4;    // https://<ad>.trycloudflare.com/gwent (soneki kısalt)

    public static string Encode(string hubUrl, string roomCode)
    {
        if (roomCode.Length != 5) throw new ArgumentException("Oda kodu 5 karakter olmalı.", nameof(roomCode));
        var uri = new Uri(hubUrl, UriKind.Absolute);
        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes(roomCode.ToUpperInvariant()));

        var hubPath = uri.AbsolutePath is "/" or "" ? "/gwent" : uri.AbsolutePath;
        if (hubPath == "/gwent" && uri.Scheme == "http" && System.Net.IPAddress.TryParse(uri.Host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            body.Add(TypeIpv4);
            body.AddRange(ip.GetAddressBytes());
            body.Add((byte)(uri.Port >> 8));
            body.Add((byte)(uri.Port & 0xff));
        }
        else if (hubPath == "/gwent" && uri.Scheme == "https" && uri.Port == 443 && uri.Host.EndsWith(TryCloudflare, StringComparison.OrdinalIgnoreCase))
        {
            body.Add(TypeTunnel);
            body.AddRange(Encoding.ASCII.GetBytes(uri.Host[..^TryCloudflare.Length]));
        }
        else if (hubPath == "/gwent" && uri.Scheme == "https" && uri.Port == 443)
        {
            body.Add(TypeHttps);
            body.AddRange(Encoding.ASCII.GetBytes(uri.Host));
        }
        else
        {
            body.Add(TypeFull);
            body.AddRange(Encoding.UTF8.GetBytes(uri.ToString()));
        }
        body.Add(Checksum(body));
        var text = ToBase32(body.ToArray());
        var groups = Enumerable.Range(0, (text.Length + 4) / 5).Select(i => text.Substring(i * 5, Math.Min(5, text.Length - i * 5)));
        return Prefix + "-" + string.Join("-", groups);
    }

    public static bool TryDecode(string? text, out Invite invite, out string? error)
    {
        invite = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "Davet kodu boş."; return false; }
        var clean = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (!clean.StartsWith(Prefix, StringComparison.Ordinal)) { error = "Bu bir Gwent davet kodu değil (GWENT- ile başlamalı)."; return false; }
        clean = clean[Prefix.Length..].Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (clean.Any(c => !Alphabet.Contains(c))) { error = "Davet kodunda geçersiz karakter var."; return false; }
        byte[] bytes;
        try { bytes = FromBase32(clean); }
        catch (FormatException) { error = "Davet kodu çözülemedi."; return false; }
        if (bytes.Length < 8) { error = "Davet kodu çok kısa."; return false; }
        var check = bytes[^1];
        var body = bytes[..^1];
        if (Checksum(body) != check) { error = "Davet kodunda yazım hatası var (sağlama uyuşmuyor). Baştan kopyala."; return false; }

        var code = Encoding.ASCII.GetString(body, 0, 5);
        var type = body[5];
        var data = body[6..];
        string url;
        switch (type)
        {
            case TypeIpv4 when data.Length == 6:
                url = $"http://{data[0]}.{data[1]}.{data[2]}.{data[3]}:{(data[4] << 8) | data[5]}/gwent";
                break;
            case TypeTunnel when data.Length > 0:
                url = $"https://{Encoding.ASCII.GetString(data)}{TryCloudflare}/gwent";
                break;
            case TypeHttps when data.Length > 0:
                url = $"https://{Encoding.ASCII.GetString(data)}/gwent";
                break;
            case TypeFull when data.Length > 0:
                url = Encoding.UTF8.GetString(data);
                break;
            default:
                error = "Davet kodunun türü tanınmıyor (oyun sürümün eski olabilir).";
                return false;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")) { error = "Davet kodundaki adres geçersiz."; return false; }
        invite = new Invite(url, code);
        return true;
    }

    // ---------- Yardımcılar ----------

    private static byte Checksum(IEnumerable<byte> data)
    {
        // basit ama her baytı ve sırayı hesaba katan 8 bit sağlama
        var sum = 0x5a;
        foreach (var b in data) sum = ((sum << 1) | (sum >> 7)) & 0xff ^ b;
        return (byte)sum;
    }

    private static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] FromBase32(string s)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException();
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }
}
