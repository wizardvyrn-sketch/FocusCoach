// icon_gen - アプリのアイコンを描いて .ico(16〜256px、PNG 形式)にする使い捨てツール(Windows 専用)。
// コンセプト: 「近未来」+「ペンで描く」。深い青緑の角丸の面に、金属的なペンが斜めに立ち、ペン先から青緑→青に光る線が
// 流れ出して、先端に小さな節(回路の端点)が灯る。小さいサイズ(16〜32px)では、線を太く・飾りを減らして潰れないようにする。
// 実行: dotnet run --project prototype/icon_gen -- <出力フォルダ>   → app.ico と、確認用の preview_*.png を作る
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class P
{
    // App.xaml の配色(03 §3.4.1)と合わせる
    static readonly Color Bg1 = Color.FromRgb(0x1A, 0x30, 0x39), Bg2 = Color.FromRgb(0x0E, 0x1A, 0x20);
    static readonly Color Teal = Color.FromRgb(0x4F, 0xD1, 0xC5), Blue = Color.FromRgb(0x6C, 0xB4, 0xEE);

    [STAThread]
    static void Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : AppContext.BaseDirectory;
        Directory.CreateDirectory(outDir);
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var pngs = new List<(int Size, byte[] Data)>();
        foreach (var s in sizes)
        {
            var png = Render(s);
            pngs.Add((s, png));
            if (s is 16 or 32 or 64 or 256) File.WriteAllBytes(Path.Combine(outDir, $"preview_{s}.png"), png);
        }
        WriteIco(Path.Combine(outDir, "app.ico"), pngs);
        Console.WriteLine($"saved app.ico ({pngs.Count} sizes, {new FileInfo(Path.Combine(outDir, "app.ico")).Length} bytes)");
    }

    /// <summary>size px の正方形に描いた PNG。座標は 0〜100 の仮想の枠で書き、スケールして描く。</summary>
    static byte[] Render(int size)
    {
        var small = size <= 32;                 // 小さいサイズでは飾りを省き、線を太くする
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 100.0, size / 100.0));

            // 1) 地: 角丸の面(深い青緑のグラデーション)と、細い縁の光
            var tile = new Rect(3, 3, 94, 94);
            var bg = new LinearGradientBrush(Bg1, Bg2, 60);
            dc.DrawRoundedRectangle(bg, new Pen(new SolidColorBrush(Color.FromArgb(150, Teal.R, Teal.G, Teal.B)), small ? 3.2 : 1.8), tile, 20, 20);
            if (!small)
            {
                // 回路のような細い線(ごく薄く)
                var faint = new Pen(new SolidColorBrush(Color.FromArgb(34, Blue.R, Blue.G, Blue.B)), 1.1);
                dc.DrawLine(faint, new Point(14, 30), new Point(40, 30));
                dc.DrawLine(faint, new Point(40, 30), new Point(48, 38));
                dc.DrawLine(faint, new Point(60, 84), new Point(86, 84));
                dc.DrawEllipse(null, faint, new Point(14, 30), 2.2, 2.2);
                dc.DrawEllipse(null, faint, new Point(86, 84), 2.2, 2.2);
            }

            // 2) 光る線: ペン先から右へ流れる曲線(青緑→青)。太さは小さいサイズほど太い
            var strokeW = small ? 9.5 : 6.2;
            var curve = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(24, 76), IsClosed = false };
            fig.Segments.Add(new BezierSegment(new Point(38, 90), new Point(50, 66), new Point(62, 74), true));
            fig.Segments.Add(new BezierSegment(new Point(72, 80), new Point(76, 70), new Point(84, 66), true));
            curve.Figures.Add(fig);
            var lineBrush = new LinearGradientBrush(Teal, Blue, new Point(0, 0), new Point(1, 0));
            if (!small)
            {
                // 外側のにじみ(光)
                var glow = new Pen(new SolidColorBrush(Color.FromArgb(70, Teal.R, Teal.G, Teal.B)), strokeW + 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                dc.DrawGeometry(null, glow, curve);
            }
            var line = new Pen(lineBrush, strokeW) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, line, curve);

            // 3) 線の先端: 回路の端点のような小さな節
            if (!small)
            {
                dc.DrawEllipse(new SolidColorBrush(Bg2), new Pen(new SolidColorBrush(Blue), 2.2), new Point(88, 64), 4.2, 4.2);
            }

            // 4) ペン: 斜めに立つ金属のペン。ペン先(左下)が線の起点に触れる。軸は左下→右上の角度
            dc.PushTransform(new RotateTransform(40, 24, 76));          // ペン先を支点に回す(軸が右上へ伸びる)
            var body = new PathGeometry();
            // 真上に伸びる軸として描き、回転で斜めにする。ペン先(24,76)→ 上へ 52
            var f = new PathFigure { StartPoint = new Point(24, 76), IsClosed = true };
            f.Segments.Add(new LineSegment(new Point(18.6, 66), true));      // ペン先の根元(左)
            f.Segments.Add(new LineSegment(new Point(18.6, 26), true));
            f.Segments.Add(new LineSegment(new Point(29.4, 26), true));
            f.Segments.Add(new LineSegment(new Point(29.4, 66), true));      // 根元(右)
            body.Figures.Add(f);
            var metal = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0xF2, 0xFA, 0xFB), 0.0),
                new GradientStop(Color.FromRgb(0xA9, 0xC9, 0xD2), 0.45),
                new GradientStop(Color.FromRgb(0x5C, 0x86, 0x93), 1.0),
            }, new Point(0, 0), new Point(1, 0));
            dc.DrawGeometry(metal, new Pen(new SolidColorBrush(Color.FromArgb(200, 0xE6, 0xF6, 0xF8)), small ? 1.4 : 0.8), body);
            // 軸の上端の丸み・キャップ(青緑のアクセント)
            dc.DrawRoundedRectangle(new SolidColorBrush(Teal), null, new Rect(18.6, 20, 10.8, 8), 3.5, 3.5);
            if (!small)
            {
                // ペン先(金属のくさび)とスリット、軸の細い帯
                var nib = new PathGeometry();
                var nf = new PathFigure { StartPoint = new Point(24, 80.5), IsClosed = true };
                nf.Segments.Add(new LineSegment(new Point(19.2, 66), true));
                nf.Segments.Add(new LineSegment(new Point(28.8, 66), true));
                nib.Figures.Add(nf);
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xE9, 0xF7, 0xF8)), null, nib);
                dc.DrawLine(new Pen(new SolidColorBrush(Bg2), 0.9), new Point(24, 80), new Point(24, 70));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, Bg2.R, Bg2.G, Bg2.B)), null, new Rect(18.6, 56, 10.8, 3));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, Teal.R, Teal.G, Teal.B)), null, new Rect(18.6, 59.5, 10.8, 1.3));
            }
            else
            {
                var nib = new PathGeometry();
                var nf = new PathFigure { StartPoint = new Point(24, 82), IsClosed = true };
                nf.Segments.Add(new LineSegment(new Point(18.6, 66), true));
                nf.Segments.Add(new LineSegment(new Point(29.4, 66), true));
                nib.Figures.Add(nf);
                dc.DrawGeometry(Brushes.White, null, nib);
            }
            dc.Pop();   // Rotate
            dc.Pop();   // Scale
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream(); enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>PNG 形式の画像を並べた .ico(Windows Vista 以降は PNG 内包を読める。256px は幅・高さを 0 と書く)。</summary>
    static void WriteIco(string path, List<(int Size, byte[] Data)> imgs)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)imgs.Count);
        var offset = 6 + 16 * imgs.Count;
        foreach (var (size, data) in imgs)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write((uint)data.Length); w.Write((uint)offset);
            offset += data.Length;
        }
        foreach (var (_, data) in imgs) w.Write(data);
    }
}
