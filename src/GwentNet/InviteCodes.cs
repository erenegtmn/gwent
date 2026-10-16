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

    // ---------- Sıkıştırılmış biçim (v2) ----------
    // Her base32 karakteri bir 5 bitlik simgedir: [tür][oda kodu ×5][veri…][sağlama ×2].
    // Tür simgesi 16-19'dur; eski biçimin ilk karakteri (oda kodunun ilk ASCII baytının üst 5 biti) en çok 11 olabildiği için ikisi karışmaz.
    // Tünel adı harf başına tek simge (a-z, '-', rakam = kaçış + rakam): eski biçimde harf başına 8 bit gidiyordu, kod ~%40 kısaldı.
    private const string RoomAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";   // sunucunun oda kodu alfabesi (RoomService)
    private const int V2Tunnel = 16, V2Ipv4 = 17, V2Https = 18, V2Full = 19;
    private const int HostDash = 26, HostDigit = 27;

    public static string Encode(string hubUrl, string roomCode)
    {
        if (roomCode.Length != 5) throw new ArgumentException("Oda kodu 5 karakter olmalı.", nameof(roomCode));
        if (EncodeV2(hubUrl, roomCode.ToUpperInvariant()) is { } v2) return v2;
        return EncodeV1(hubUrl, roomCode);
    }

    private static string? EncodeV2(string hubUrl, string roomCode)
    {
        if (roomCode.Any(c => !RoomAlphabet.Contains(c))) return null;
        var uri = new Uri(hubUrl, UriKind.Absolute);
        var hubPath = uri.AbsolutePath is "/" or "" ? "/gwent" : uri.AbsolutePath;
        var sym = new List<int>();
        void Header(int type) { sym.Add(type); sym.AddRange(roomCode.Select(c => RoomAlphabet.IndexOf(c))); }

        if (hubPath == "/gwent" && uri.Scheme == "http" && System.Net.IPAddress.TryParse(uri.Host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            Header(V2Ipv4);
            sym.AddRange(ToSymbols(ip.GetAddressBytes().Append((byte)(uri.Port >> 8)).Append((byte)(uri.Port & 0xff)).ToArray()));
        }
        else if (hubPath == "/gwent" && uri.Scheme == "https" && uri.Port == 443 && uri.Host.EndsWith(TryCloudflare, StringComparison.OrdinalIgnoreCase)
                 && HostSymbols(uri.Host[..^TryCloudflare.Length].ToLowerInvariant()) is { } host)
        {
            Header(V2Tunnel);
            sym.AddRange(host);
        }
        else if (hubPath == "/gwent" && uri.Scheme == "https" && uri.Port == 443)
        {
            Header(V2Https);
            sym.AddRange(ToSymbols(Encoding.ASCII.GetBytes(uri.Host)));
        }
        else
        {
            Header(V2Full);
            sym.AddRange(ToSymbols(Encoding.UTF8.GetBytes(uri.ToString())));
        }
        var check = Checksum10(sym);
        sym.Add(check >> 5);
        sym.Add(check & 31);
        return Group(new string(sym.Select(s => Alphabet[s]).ToArray()));
    }

    /// <summary>Tünel adı: a-z ve '-' tek simge, rakam iki simge; başka karakter varsa null (yedek türe düşülür).</summary>
    private static List<int>? HostSymbols(string host)
    {
        var sym = new List<int>();
        foreach (var c in host)
        {
            if (c is >= 'a' and <= 'z') sym.Add(c - 'a');
            else if (c == '-') sym.Add(HostDash);
            else if (c is >= '0' and <= '9') { sym.Add(HostDigit); sym.Add(c - '0'); }
            else return null;
        }
        return sym.Count > 0 ? sym : null;
    }

    private static string? HostFromSymbols(int[] sym)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < sym.Length; i++)
        {
            var s = sym[i];
            if (s < 26) sb.Append((char)('a' + s));
            else if (s == HostDash) sb.Append('-');
            else if (s == HostDigit && i + 1 < sym.Length && sym[i + 1] < 10) sb.Append((char)('0' + sym[++i]));
            else return null;
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// 10 bitlik sağlama: Σ simge × (2i+1). Ağırlıklar tek olduğu için tek karakter hatası (fark &lt; 32) ve yan yana iki karakterin
    /// yer değiştirmesi (fark × 2) mod 1024'te sıfır olamaz: ikisi de her zaman yakalanır.
    /// </summary>
    private static int Checksum10(IReadOnlyList<int> sym)
    {
        var sum = 0x2b5;
        for (var i = 0; i < sym.Count; i++) sum += sym[i] * (2 * i + 1);
        return sum & 1023;
    }

    private static int[] ToSymbols(byte[] data) => ToBase32(data).Select(c => Alphabet.IndexOf(c)).ToArray();

    private static string Group(string text) =>
        Prefix + "-" + string.Join("-", Enumerable.Range(0, (text.Length + 4) / 5).Select(i => text.Substring(i * 5, Math.Min(5, text.Length - i * 5))));

    private static bool TryDecodeV2(int[] sym, out Invite invite, out string? error)
    {
        invite = default;
        error = null;
        if (sym.Length < 9) { error = "Davet kodu çok kısa."; return false; }
        var body = sym[..^2];
        if (Checksum10(body) != ((sym[^2] << 5) | sym[^1])) { error = "Davet kodunda yazım hatası var (sağlama uyuşmuyor). Baştan kopyala."; return false; }
        if (body[1..6].Any(s => s >= RoomAlphabet.Length)) { error = "Davet kodu çözülemedi."; return false; }
        var code = new string(body[1..6].Select(s => RoomAlphabet[s]).ToArray());
        var data = body[6..];
        string? url = null;
        try
        {
            switch (body[0])
            {
                case V2Ipv4:
                    var b = FromBase32(new string(data.Select(s => Alphabet[s]).ToArray()));
                    if (b.Length == 6) url = $"http://{b[0]}.{b[1]}.{b[2]}.{b[3]}:{(b[4] << 8) | b[5]}/gwent";
                    break;
                case V2Tunnel:
                    if (HostFromSymbols(data) is { } host) url = $"https://{host}{TryCloudflare}/gwent";
                    break;
                case V2Https:
                    url = $"https://{Encoding.ASCII.GetString(FromBase32(new string(data.Select(s => Alphabet[s]).ToArray())))}/gwent";
                    break;
                case V2Full:
                    url = Encoding.UTF8.GetString(FromBase32(new string(data.Select(s => Alphabet[s]).ToArray())));
                    break;
            }
        }
        catch (FormatException) { url = null; }
        if (url == null) { error = "Davet kodunun türü tanınmıyor (oyun sürümün eski olabilir)."; return false; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")) { error = "Davet kodundaki adres geçersiz."; return false; }
        invite = new Invite(url, code);
        return true;
    }

    // ---------- Eski biçim (v1): hâlâ çözülür; yalnız oda kodu sunucu alfabesinde değilse üretilir ----------

    private static string EncodeV1(string hubUrl, string roomCode)
    {
        var uri = new Uri(hubUrl, UriKind.Absolute);
        var body = new List<byte>();
        // (eski biçim) oda kodu ASCII + tür baytı + adres baytları + 8 bit sağlama
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
        return Group(ToBase32(body.ToArray()));
    }

    public static bool TryDecode(string? text, out Invite invite, out string? error)
    {
        invite = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "Davet kodu boş."; return false; }
        var clean = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (clean.Length == 5 && !clean.StartsWith(Prefix, StringComparison.Ordinal))
        { error = "Bu bir oda kodu: tek başına yetmez, çünkü oyun odanın hangi adreste olduğunu bilemez. Odayı kuran kişiden GWENT- ile başlayan davet kodunu iste."; return false; }
        if (!clean.StartsWith(Prefix, StringComparison.Ordinal)) { error = "Bu bir Gwent davet kodu değil (GWENT- ile başlamalı)."; return false; }
        clean = clean[Prefix.Length..].Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (clean.Any(c => !Alphabet.Contains(c))) { error = "Davet kodunda geçersiz karakter var."; return false; }
        if (clean.Length > 0 && Alphabet.IndexOf(clean[0]) >= V2Tunnel) return TryDecodeV2(clean.Select(c => Alphabet.IndexOf(c)).ToArray(), out invite, out error);
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
