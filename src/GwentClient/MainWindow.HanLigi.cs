using GwentClient.HanLigi;

namespace GwentClient;

/// <summary>Han Ligi akışı (YENI_MOD.md): tek oyuncu yedi bota karşı ya da online (2-8 kişi, boş koltuklar bot).</summary>
public partial class MainWindow
{
    private void ShowHan()
    {
        var name = string.IsNullOrWhiteSpace(Settings.Nick) ? "Sen" : Settings.Nick;
        var session = new HanSession(Random.Shared.Next(1, int.MaxValue), name);
        var view = new HanView(session, ShowPlayHub, ShowHan);
        Music.Start();
        Host.Content = view;
        view.Focus();
    }

    /// <summary>Online Han maçı başladı (ya da yeniden bağlanınca kurulum geldi): oyun ekranı; çıkınca odadan ayrılır.</summary>
    private void OnHanStarted(HanSession session)
    {
        var view = new HanView(session, () => _ = LeaveOnlineAsync(), () => _ = LeaveOnlineAsync());
        Music.Start();
        Host.Content = view;
        view.Focus();
    }
}
