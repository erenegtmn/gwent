using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GwentClient;

/// <summary>
/// Oyun içi güncelleme penceresi: indirme ilerlemesi (MB ve yüzde), iptal; bitince oyun yeni sürümle yeniden açılır.
/// Hata olursa dosyalar eski hâlinde kalır ve "Sayfadan indir" ile elle indirme seçeneği sunulur.
/// </summary>
public sealed class UpdateDialog : Grid
{
    private readonly ReleaseInfo _r;
    private readonly TextBlock _status = Ui.Text("", 20, Theme.Text, false, TextAlignment.Center, true);
    private readonly Border _bar = new() { Background = Ornate.GoldEdge, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
    private readonly CancellationTokenSource _cts = new();
    private const double BarW = 560;

    public event Action? Closed;

    public UpdateDialog(ReleaseInfo r)
    {
        _r = r;
        Background = new SolidColorBrush(Color.FromArgb(0xd0, 0, 0, 0));
        Tag = "update-dialog";
        var sp = new StackPanel { Width = 640 };
        sp.Children.Add(Ornate.Heading("Güncelleme", $"{UpdateCheck.CurrentText} → {r.Tag}", 36, HorizontalAlignment.Center));
        sp.Children.Add(Ornate.Rule(460, 12));
        sp.Children.Add(_status);
        var track = new Grid { Width = BarW, Height = 12, Margin = new Thickness(0, 16, 0, 0) };
        track.Children.Add(new Border { Background = Theme.B("#33ffffff"), CornerRadius = new CornerRadius(6) });
        track.Children.Add(_bar);
        sp.Children.Add(track);
        var note = Ui.Text("İlerlemen, profilin ve destelerin korunur. Bitince oyun yeni sürümle kendiliğinden açılır.", 14, Theme.TextDim, false, TextAlignment.Center, true);
        note.Margin = new Thickness(0, 12, 0, 0);
        sp.Children.Add(note);
        sp.Children.Add(_buttons);
        var panel = Ornate.Panel(sp, new Thickness(44, 34, 44, 34));
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(panel);
        Loaded += (_, _) => Run();
    }

    private void Buttons(params Button[] bs)
    {
        _buttons.Children.Clear();
        foreach (var b in bs) { b.Margin = new Thickness(6, 0, 6, 0); b.FontSize = 18; b.Padding = new Thickness(24, 8, 24, 8); _buttons.Children.Add(b); }
    }

    private async void Run()
    {
        var cancel = Ui.Button("İptal", () => _cts.Cancel());
        cancel.Tag = "update-cancel";
        Buttons(cancel);
        _status.Text = "İndiriliyor…";
        Music.Release();   // çalan müzik dosyası kilitli kalmasın
        var progress = new Progress<UpdateProgress>(p =>
        {
            switch (p.Phase)
            {
                case "download":
                    var pct = p.Total > 0 ? (double)p.Done / p.Total : 0;
                    _bar.Width = BarW * Math.Clamp(pct, 0, 1);
                    _status.Text = p.Total > 0 ? $"İndiriliyor… %{pct * 100:0}  ({p.Done / 1048576.0:0} / {p.Total / 1048576.0:0} MB)" : $"İndiriliyor… {p.Done / 1048576.0:0} MB";
                    break;
                case "extract":
                    _bar.Width = BarW;
                    _status.Text = "Paket açılıyor…";
                    _buttons.Children.Clear();   // bu aşamadan sonra iptal yok (yarım değiştirme olmasın)
                    break;
                case "apply":
                    _status.Text = "Dosyalar yenileniyor…";
                    break;
            }
        });
        try
        {
            var exe = await Updater.DownloadAndApplyAsync(_r, progress, _cts.Token);
            _status.Text = $"{_r.Tag} hazır. Oyun yeniden başlatılıyor…";
            await Task.Delay(700);
            Updater.Restart(exe);
        }
        catch (OperationCanceledException)
        {
            Music.Start();
            Closed?.Invoke();
        }
        catch (Exception ex)
        {
            Music.Start();
            _status.Text = "Güncelleme yapılamadı: " + ex.Message + " Oyunun dosyaları değişmedi.";
            _status.Foreground = Theme.Bad;
            var page = Ui.Button("Sayfadan indir", () => UpdateCheck.OpenPage(_r.Url), primary: true);
            var close = Ui.Button("Kapat", () => Closed?.Invoke());
            Buttons(page, close);
        }
    }
}
