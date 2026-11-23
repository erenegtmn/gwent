using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace GwentClient.HanLigi;

/// <summary>
/// Han Ligi'nin sahnesi: loş han içi, perspektifle yatırılmış ahşap masa ve üstünde Gwent tahtası. Sahne <b>durağandır</b>:
/// tahta görseli bir kez dokuya çizilir, bütün sahne GPU'da önbelleğe alınır (her kare yeniden çizilmez). Kartlar, satır
/// vurguları ve bırakma alanları ekran katmanlarında çizilir; yerlerini <see cref="ToScreen"/> izdüşümü verir. Böylece her şey
/// net kalır ve masanın perspektifine oturur.
/// </summary>
public sealed class HanScene : Grid
{
    public const double BoardW = 1600, BoardH = 1000;
    // Kamera ve tahtanın yerleşimi (ToScreen de bunları kullanır). Tahta ekranda y ≈ 60..865 aralığına oturur.
    private const double Tilt = -28, Lift = 1.3, CamZ = 18.65, Fov = 55, ShiftX = -0.595;
    private static ImageSource? _boardTex;
    private static bool _boardTexAssets;

    public HanScene(bool animate)
    {
        Width = 1920;
        Height = 1080;
        IsHitTestVisible = true;
        // zemin: han odasının karanlığı
        Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0x04, 0x03, 0x02)) });

        // han odası, masa ve tahta (HanTavern: masa yerel koordinatları) tahtanın dünyadaki yerine konur; açılıştaki kamera
        // yolculuğunun son karesi bu görüntüyle birebir aynıdır
        var rot = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), Tilt));
        var place = new Transform3DGroup { Children = { rot, new TranslateTransform3D(ShiftX, Lift, 0) } };
        var view = new Viewport3D { ClipToBounds = true, IsHitTestVisible = false };
        view.Camera = GameCameraWorld();
        var world = new Model3DGroup { Transform = place };
        world.Children.Add(HanTavern.Room());
        world.Children.Add(HanTavern.Table());
        world.Children.Add(HanTavern.Board(BoardTexture()));
        world.Children.Add(HanTavern.Lights());
        view.Children.Add(new ModelVisual3D { Content = world });
        Children.Add(view);

        // masanın uzak kenarı karanlığa karışır, ortada sıcak ışık, kenarlarda vinyet (hepsi durağan)
        Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0xff, 0x05, 0x03, 0x02), 0), new(Color.FromArgb(0xb0, 0x05, 0x03, 0x02), 0.04), new(Color.FromArgb(0, 0x05, 0x03, 0x02), 0.12),
                new(Color.FromArgb(0, 0, 0, 0), 0.72), new(Color.FromArgb(0xa0, 0, 0, 0), 1)
            }, 90)
        });
        Children.Add(new Rectangle { Fill = new RadialGradientBrush(Color.FromArgb(0x2c, 0xff, 0xb0, 0x58), Color.FromArgb(0, 0xff, 0xa0, 0x40)) { Center = new Point(0.5, 0.45), GradientOrigin = new Point(0.5, 0.45), RadiusX = 0.5, RadiusY = 0.6 } });
        Children.Add(new Rectangle { Fill = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xb0, 0, 0, 0)) { RadiusX = 0.8, RadiusY = 0.85 } });
    }

    /// <summary>Tahta dokusu: Gwent masasının satır bölgesi (yoksa çizim), altın çerçeve, iki taraf arasındaki çizgi. Bir kez üretilir.</summary>
    /// <summary>Tahta dokusunun dolu kısmı (çerçeve dahil); dışı saydam.</summary>
    internal static readonly Rect BoardTexRect = new(296 - 8, 20 - 8, 1127 + 16, 960 + 16);

    internal static ImageSource BoardTexture()
    {
        if (_boardTex != null && _boardTexAssets == Assets.Available) return _boardTex;
        const double bx = 296, by = 20, bw = 1127, bh = 960;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x0a, 0x06, 0x03)), new Pen(new LinearGradientBrush(Color.FromRgb(0xb8, 0x92, 0x46), Color.FromRgb(0x4a, 0x34, 0x14), 90), 6), new Rect(bx - 8, by - 8, bw + 16, bh + 16), 8, 8);
            if (Assets.Ui("board") is BitmapSource board && board.PixelWidth >= 1530 && board.PixelHeight >= 830)
                dc.DrawImage(new CroppedBitmap(board, new Int32Rect(566, 12, 956, 814)), new Rect(bx, by, bw, bh));
            else
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x4a, 0x2e, 0x16), Color.FromRgb(0x2c, 0x1a, 0x0c), 90), null, new Rect(bx, by, bw, bh));
                foreach (var top in new double[] { 32, 185, 345, 515, 678, 838 })
                {
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x44, 0, 0, 0)), new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x3a, 0x2a, 0x14)), 1), new Rect(463, top + 3, 955, 131), 5, 5);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x44, 0, 0, 0)), null, new Rect(303, top + 3, 150, 131), 5, 5);
                }
            }
            dc.DrawRectangle(new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, 0xe0, 0xb8, 0x5a), 0), new(Color.FromArgb(0xcc, 0xe0, 0xb8, 0x5a), 0.5), new(Color.FromArgb(0, 0xe0, 0xb8, 0x5a), 1) }, 0), null, new Rect(bx, 496, bw, 3));
        }
        var rtb = new RenderTargetBitmap((int)BoardW, (int)BoardH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        _boardTexAssets = Assets.Available;
        return _boardTex = rtb;
    }

    private static PerspectiveCamera GameCameraWorld() => new(new Point3D(0, 0, CamZ), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), Fov);

    /// <summary>Oyundaki masa kamerası, masa yerel koordinatlarında (HanTavern): açılış yolculuğu burada biter.</summary>
    public static PerspectiveCamera GameCamera() => HanTavern.GameCamera(Tilt, Lift, CamZ, Fov, ShiftX);

    /// <summary>Tahta üzerindeki bir noktanın ekrandaki (1920×1080) karşılığı.</summary>
    public static Point ToScreen(Point boardPoint)
    {
        // tahta düzlemi: x ∈ [-8, 8], y ∈ [5, -5]; X ekseninde döner, sonra kayar
        var x = (boardPoint.X / BoardW - 0.5) * 16 + ShiftX;
        var y = (0.5 - boardPoint.Y / BoardH) * 10;
        var a = Tilt * Math.PI / 180;
        var wy = y * Math.Cos(a) + Lift;
        var wz = y * Math.Sin(a);
        var d = CamZ - wz;
        var half = Math.Tan(Fov * Math.PI / 360);   // yatay görüş açısının yarısı
        var sx = x / d / half;
        var sy = wy / d / half * (1920.0 / 1080.0);
        return new Point(960 + sx * 960, 540 - sy * 540);
    }

    /// <summary>Tahtadaki bir dikdörtgenin ekrandaki dört köşesi (sol üst, sağ üst, sağ alt, sol alt): perspektifli yamuk.</summary>
    public static Point[] Trapezoid(Rect r) => new[]
    {
        ToScreen(r.TopLeft), ToScreen(r.TopRight), ToScreen(r.BottomRight), ToScreen(r.BottomLeft)
    };
}
