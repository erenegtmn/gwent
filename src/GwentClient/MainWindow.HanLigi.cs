using GwentClient.HanLigi;

namespace GwentClient;

/// <summary>Han Ligi akışı (YENI_MOD.md): tek oyuncu yedi bota karşı; maç bitince yeniden oyna ya da menü.</summary>
public partial class MainWindow
{
    private void ShowHan()
    {
        var name = string.IsNullOrWhiteSpace(Settings.Nick) ? "Sen" : Settings.Nick;
        var session = new HanSession(Random.Shared.Next(1, int.MaxValue), name);
        var view = new HanView(session, ShowMenu, ShowHan);
        Music.Start();
        Host.Content = view;
        view.Focus();
    }
}
