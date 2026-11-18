using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HanLigi;

namespace GwentClient.HanLigi;

/// <summary>
/// Savaş sırasında tezgâh: oyuncu tezgâha bakar ve kilitler (alım ve yenileme yalnız hazırlıkta). Açılınca alt şerit, dikey
/// dönen bir zar gibi (3B küp, X ekseni çevresinde) depo satırına döner; kapanınca ters yöne döner.
/// </summary>
public sealed partial class HanView
{
    /// <summary>Savaşta tezgâh açık mı (alt şerit depo satırıdır).</summary>
    private bool _battleShop;
    private Viewport3D? _roll;

    private bool BattleShopAllowed => _screen is Screen.Activation or Screen.Battle && M.Phase == Phase.Battle && Me.Alive;

    private void ToggleBattleShop()
    {
        if (_busy || !BattleShopAllowed) return;
        var before = _live ? SnapshotBar() : null;
        _battleShop = !_battleShop;
        Sfx.Play(_battleShop ? "han_open" : "han_click");
        Render();
        if (before != null && SnapshotBar() is { } after) DiceRoll(before, after, _battleShop);
        if (_live && _battleShop) _shop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(120) });
    }

    /// <summary>Savaş ekranının alt şeridinin üstünde tezgâh düğmesi (açıkken "kapat").</summary>
    private void BattleShopButton()
    {
        if (!BattleShopAllowed) return;
        var b = Act(_battleShop ? "Tezgâhı kapat" : "Tezgâh", "Boşluk", "han-battle-shop", ToggleBattleShop, _battleShop ? HanButtonKind.Primary : HanButtonKind.Secondary, 212, 54, !_busy,
            _battleShop ? "Alt şerit savaşa döner (Boşluk)." : "Savaş sürerken tezgâha bak ve kilitle; alım ve yenileme hazırlıkta (Boşluk).", HanArt.Icon("icon_lager", 22, ""));
        Put(b, 1696, BarY - 64);
    }

    /// <summary>Savaşta tezgâh açıkken alt şerit: altın ve gelir, depo (8 yuva, yalnız bakılır), kilit.</summary>
    private void BattleBenchTray()
    {
        // altın ve sonraki round geliri
        var gold = new StackPanel { Orientation = Orientation.Horizontal };
        gold.Children.Add(HanArt.Icon("icon_crowns", 34, "◉"));
        gold.Children.Add(HanUi.Number(" " + Me.Gold, Me.Gold >= 100 ? 32 : 40));
        Put(gold, EcoX + 12, BarY + 6);
        var inc = NextIncome();
        var next = HanUi.T($"+{inc.Sum(x => x.Value)} sonraki round", 13, HanUi.Br(HanUi.Muted));
        next.ToolTip = IncomeTip(inc);
        Put(next, EcoX + 16, BarY + 62);
        Put(HanUi.T($"Seviye {Me.Level}   masa {Me.Used}/{Me.Slots}", 15, HanUi.Br(HanUi.Parchment), FontWeights.Bold), EcoX + 16, BarY + 86);
        Put(HanUi.Body("Savaşta tezgâha bakabilir ve kilitleyebilirsin. Alım, satış ve yenileme hazırlıkta.", 12.5, HanUi.Br(HanUi.Muted)), EcoX + 16, BarY + 116).Width = EcoW - 30;

        // depo: kartlar yalnız gösterilir (savaşta taşınmaz)
        var bench = Me.Bench.ToList();
        var cap = Put(Caption($"DEPO  {bench.Count}/{Tuning.BenchSize}"), BenchX + 2, BarY + 8);
        cap.Foreground = bench.Count >= Tuning.BenchSize ? Theme.Bad : Theme.B("#a8905a");
        cap.Tag = "han-battle-bench";
        const double gap = (ShopX - BenchX - 16 - Tuning.BenchSize * BenchCardW) / (Tuning.BenchSize - 1);
        for (var i = 0; i < Tuning.BenchSize; i++)
        {
            var x = BenchX + i * (BenchCardW + gap);
            if (i < bench.Count) Put(CardEl(bench[i].Def, bench[i].Star, BenchCardW, null, null), x, BarY + 30);
            else Put(new Border { Width = BenchCardW, Height = BenchCardW * HanCardView.CardAspect, CornerRadius = new CornerRadius(6), BorderBrush = Theme.B("#30e8dcc4"), BorderThickness = new Thickness(1), Background = Theme.B("#26000000"), IsHitTestVisible = false }, x, BarY + 30);
        }
        Put(new System.Windows.Shapes.Rectangle { Width = 1, Height = BarH - 28, Fill = Theme.B("#405e451c") }, ShopX - 8, BarY + 14);

        // kilit ve açıklaması
        Put(LockButton(96, 96), ShopX + 8, BarY + 30);
        var sp = new StackPanel { Width = 520 };
        sp.Children.Add(HanUi.Label(Me.ShopLocked ? "Tezgâh kilitli" : "Tezgâh kilidi", 12, Me.ShopLocked ? HanUi.GoldText : null));
        sp.Children.Add(HanUi.Title(Me.ShopLocked ? "Sonraki round aynı kartlar" : "Kilitle: kartlar kalsın", 24, HanUi.GoldText));
        sp.Children.Add(HanUi.Body(Me.ShopLocked
            ? "Round başında tezgâh yenilenmez; şu an tezgâhtaki kartlar aynı yerlerinde gelir. Açmak için kilide bas (L)."
            : "Kilitlersen sonraki round tezgâh yenilenmez, şu anki kartlar aynen gelir (L).", 14, HanUi.Br(HanUi.Muted)));
        Put(sp, ShopX + 124, BarY + 34);
    }

    // ---------- 3B zar dönüşü ----------

    /// <summary>Alt şeridin şu anki görüntüsü (sahnenin o bölgesi).</summary>
    private BitmapSource? SnapshotBar()
    {
        if (_roll != null) { _fx.Children.Remove(_roll); _roll = null; }
        try
        {
            _stage.UpdateLayout();
            // yalnız şerit bölgesi çizilir (bütün sahneyi yazılımla çizmek saniyeler sürüyordu)
            var r = new Rect(12, BarY, 1896, BarH);
            var k = Math.Clamp(_caches.Count > 0 ? _caches[0].RenderAtScale : 1, 1, 1.5);
            // şeridin öğeleri ekran katmanındadır: yalnız bölgeye değenler çizilir (bütün sahneyi yazılımla çizmek saniyeler
            // sürüyordu). RenderTargetBitmap öğeyi tuvaldeki yerinde çizer; sonra şerit kırpılır. Zemin koyu ahşap.
            var full = new RenderTargetBitmap((int)(1920 * k), (int)(1080 * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x0e, 0x08)), null, r);
            full.Render(bg);
            foreach (UIElement child in _c.Children)
            {
                if (child is not FrameworkElement fe || fe.Visibility != Visibility.Visible || fe.ActualWidth <= 0) continue;
                var at = fe.TransformToAncestor(_c).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                if (at.IntersectsWith(r)) full.Render(fe);
            }
            var crop = new CroppedBitmap(full, new Int32Rect((int)(r.X * k), (int)(r.Y * k), (int)(r.Width * k), (int)(r.Height * k)));
            crop.Freeze();
            return crop;
        }
        catch { return null; }
    }

    /// <summary>
    /// Alt şerit bir zar gibi döner: eski görüntü küpün ön yüzü, yenisi açılırken üst yüzü (zar öne doğru yuvarlanır), kapanırken
    /// alt yüzü (geri yuvarlanır). Küp X ekseni çevresinde 90° döner, sonunda hafifçe sekip oturur ve dönerken öne kalkar;
    /// yüzler ışığa göre kararıp aydınlanır. Kamera z = 0 düzlemini ekrana 1:1 düşürür; animasyon bitince küp kalkar ve alttaki
    /// gerçek şerit (yeni görüntüyle aynı) kalır.
    /// </summary>
    private void DiceRoll(BitmapSource from, BitmapSource to, bool forward)
    {
        const double W = 1896, H = BarH, D = 2400;
        double w = W / 2, h = H / 2;
        static Point3D P(double x, double y, double z) => new(x, y, z);
        GeometryModel3D Face(BitmapSource img, Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { a, b, c, d },
                TextureCoordinates = new PointCollection { new(0, 0), new(1, 0), new(1, 1), new(0, 1) },
                TriangleIndices = new Int32Collection { 0, 3, 2, 0, 2, 1 }
            };
            var mat = new DiffuseMaterial(new ImageBrush(img) { Stretch = Stretch.Fill });
            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }
        var cube = new Model3DGroup();
        cube.Children.Add(Face(from, P(-w, h, h), P(w, h, h), P(w, -h, h), P(-w, -h, h)));                 // ön yüz: eski şerit
        cube.Children.Add(forward
            ? Face(to, P(-w, h, -h), P(w, h, -h), P(w, h, h), P(-w, h, h))                                   // üst yüz: öne yuvarlanınca öne gelir
            : Face(to, P(-w, -h, h), P(w, -h, h), P(w, -h, -h), P(-w, -h, -h)));                             // alt yüz: geri yuvarlanınca
        // yan kapaklar: koyu ahşap (dönerken uçlarda kutunun kalınlığı görünür)
        var side = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x2a, 0x1c, 0x0e)));
        foreach (var sx in new[] { -w, w })
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { P(sx, h, -h), P(sx, h, h), P(sx, -h, h), P(sx, -h, -h) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
            };
            cube.Children.Add(new GeometryModel3D(mesh, side) { BackMaterial = side });
        }
        var rot = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        var lift = new TranslateTransform3D(0, 0, 0);
        cube.Transform = new Transform3DGroup
        {
            Children = { new RotateTransform3D(rot), lift, new TranslateTransform3D(12 + w - 960, 540 - (BarY + h), -h) }
        };
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(0x78, 0x72, 0x6a)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(0x9a, 0x94, 0x8a), new Vector3D(0, -0.45, -1)));
        scene.Children.Add(cube);
        var vp = new Viewport3D
        {
            Width = 1920, Height = 1080, IsHitTestVisible = false, ClipToBounds = false,
            Camera = new PerspectiveCamera(new Point3D(0, 0, D), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 2 * Math.Atan(960 / D) * 180 / Math.PI) { NearPlaneDistance = 10, FarPlaneDistance = D * 3 }
        };
        RenderOptions.SetEdgeMode(vp, EdgeMode.Unspecified);
        vp.Children.Add(new ModelVisual3D { Content = scene });
        Canvas.SetLeft(vp, 0);
        Canvas.SetTop(vp, 0);
        _roll = vp;
        _fx.Children.Add(vp);

        // zar gibi: hızlanarak yuvarlanır, biraz fazla döner (~8°) ve geri oturur
        var dir = forward ? 1 : -1;
        var spin = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(680) };
        spin.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        spin.KeyFrames.Add(new EasingDoubleKeyFrame(dir * 98, KeyTime.FromPercent(0.66), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        spin.KeyFrames.Add(new EasingDoubleKeyFrame(dir * 87.5, KeyTime.FromPercent(0.84), new SineEase { EasingMode = EasingMode.EaseInOut }));
        spin.KeyFrames.Add(new EasingDoubleKeyFrame(dir * 90, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseInOut }));
        var gen = _gen;
        spin.Completed += (_, _) =>
        {
            if (_roll != vp) return;
            _fx.Children.Remove(vp);
            _roll = null;
            if (gen == _gen) Sfx.Play("han_place");
        };
        rot.BeginAnimation(AxisAngleRotation3D.AngleProperty, spin);
        // dönerken öne kalkar (zar masadan sekip döner)
        lift.BeginAnimation(TranslateTransform3D.OffsetZProperty, new DoubleAnimation(0, H * 0.35, TimeSpan.FromMilliseconds(230)) { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Sfx.Play("han_reroll");
    }
}
