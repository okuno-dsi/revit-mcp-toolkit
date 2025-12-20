// ================================================================
// File: UI/RibbonUnits.cs  (Beautiful Icons)
// 概要: "Units" プルダウン（SI / Project / Raw / Both）にアイコンを付与
//      画像が無い場合も WPFベクターで自動描画（PortIconBuilder と同系）
// 依存: Autodesk.Revit.UI / System.Windows.Media (WPF)
//       RevitMCPAddin.UI.PortIconBuilder を再利用（画像ロード/解像度）
// ================================================================
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;

namespace RevitMCPAddin.UI
{
    internal static class RibbonUnits
    {
        private const string TabName = "RevitMCPServer";
        private const string PanelName = "Units";
        private const string PullDownName = "MCP_Units_PullDown";

        /// <summary>Units 切替用のリボンパネルを追加（重複安全）</summary>
        public static void AddUnitsPanel(UIControlledApplication app, string iconDir = null)
        {
            try { app.CreateRibbonTab(TabName); } catch { /* 既存なら無視 */ }

            var panel = FindOrCreatePanel(app, TabName, PanelName);

            // 既存のプルダウンがあれば再利用
            var pullDown = panel.GetItems()
                .OfType<PulldownButton>()
                .FirstOrDefault(x => x.Name == PullDownName);

            if (pullDown == null)
            {
                var pdb = new PulldownButtonData(PullDownName, "Units");
                pullDown = panel.AddItem(pdb) as PulldownButton;
            }
            else
            {
                pullDown.ItemText = "Units";
            }

            // --- プルダウン本体のアイコン（汎用） ---
            ApplyPulldownIcon(pullDown, iconDir);

            // 既存の子ボタンは残しつつ、足りないものだけ追加
            EnsureUnitButton(pullDown, "SI (mm/m²/m³/deg)",
                "RevitMCPAddin.Commands.Misc.SwitchUnitsSiCommand", iconDir, "units_si.png");

            EnsureUnitButton(pullDown, "Project (表示単位)",
                "RevitMCPAddin.Commands.Misc.SwitchUnitsProjectCommand", iconDir, "units_project.png");

            EnsureUnitButton(pullDown, "Raw (ft/ft²/ft³/rad)",
                "RevitMCPAddin.Commands.Misc.SwitchUnitsRawCommand", iconDir, "units_raw.png");

            EnsureUnitButton(pullDown, "Both (SI + Project)",
                "RevitMCPAddin.Commands.Misc.SwitchUnitsBothCommand", iconDir, "units_both.png");

            if (pullDown != null)
            {
                pullDown.ToolTip =
                    "単位の出力ポリシーを切り替えます。\n" +
                    "SI=mm/m²/m³/deg、Project=プロジェクト表示単位、Raw=内部値(ft/rad)、Both=両方を返す。";
            }
        }

        private static RibbonPanel FindOrCreatePanel(UIControlledApplication app, string tab, string panel)
        {
            var found = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name.Equals(panel, StringComparison.OrdinalIgnoreCase));
            return found ?? app.CreateRibbonPanel(tab, panel);
        }

        private static void EnsureUnitButton(PulldownButton pd, string text, string className, string iconDir, string iconFile)
        {
            if (pd == null) return;

            // 既に同クラス名のボタンがあればスキップ
            bool exists = pd.GetItems()
                .OfType<PushButton>()
                .Any(b => string.Equals(b.ClassName, className, StringComparison.Ordinal));

            if (!exists)
            {
                var pbd = new PushButtonData(className, text,
                    Assembly.GetExecutingAssembly().Location, className);

                // --- 画像割り当て ---
                // 1) ファイルがあればそれを使用、2) 無ければベクター描画
                var largePath = PortIconBuilder.ResolveIconPath(iconDir, iconFile);
                var smallPath = largePath; // 同名でOK（なければ自動描画）

                var img16 = PortIconBuilder.TryLoadBitmapAsImage(smallPath);
                var img32 = PortIconBuilder.TryLoadBitmapAsImage(largePath);

                if (img16 == null || img32 == null)
                {
                    // クラスで出し分け
                    if (className.EndsWith("SiCommand", StringComparison.OrdinalIgnoreCase))
                    {
                        img16 ??= UnitsIconBuilder.BuildSiIcon(16);
                        img32 ??= UnitsIconBuilder.BuildSiIcon(32);
                    }
                    else if (className.EndsWith("ProjectCommand", StringComparison.OrdinalIgnoreCase))
                    {
                        img16 ??= UnitsIconBuilder.BuildProjectIcon(16);
                        img32 ??= UnitsIconBuilder.BuildProjectIcon(32);
                    }
                    else if (className.EndsWith("RawCommand", StringComparison.OrdinalIgnoreCase))
                    {
                        img16 ??= UnitsIconBuilder.BuildRawIcon(16);
                        img32 ??= UnitsIconBuilder.BuildRawIcon(32);
                    }
                    else if (className.EndsWith("BothCommand", StringComparison.OrdinalIgnoreCase))
                    {
                        img16 ??= UnitsIconBuilder.BuildBothIcon(16);
                        img32 ??= UnitsIconBuilder.BuildBothIcon(32);
                    }
                }

                if (img16 != null) pbd.Image = img16;
                if (img32 != null) pbd.LargeImage = img32;

                var created = pd.AddPushButton(pbd) as PushButton;
                if (created != null && created.LargeImage == null)
                {
                    // 念のため
                    created.LargeImage = img32 ?? created.Image;
                }
            }
        }

        private static void ApplyPulldownIcon(PulldownButton? pd, string? iconDir)
        {
            if (pd == null) return;

            // 画像があれば優先、無ければベクター生成
            var img16 = PortIconBuilder.TryLoadBitmapAsImage(
                            PortIconBuilder.ResolveIconPath(iconDir, "units_top_16.png"))
                        ?? UnitsIconBuilder.BuildRulerProtractorIcon(16);

            var img32 = PortIconBuilder.TryLoadBitmapAsImage(
                            PortIconBuilder.ResolveIconPath(iconDir, "units_top_32.png"))
                        ?? UnitsIconBuilder.BuildRulerProtractorIcon(32);

            // Revitバージョン差を吸収（PulldownButton は Image/LargeImage が無い版もある）
            var t = pd.GetType();
            var pSmall = t.GetProperty("Image");
            var pLarge = t.GetProperty("LargeImage");
            var pTip = t.GetProperty("ToolTipImage");

            try { pSmall?.SetValue(pd, img16); }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"ApplyPulldownIcon: set Image failed: {ex.Message}");
            }
            try { pLarge?.SetValue(pd, img32); }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"ApplyPulldownIcon: set LargeImage failed: {ex.Message}");
            }
            try { if (pTip != null && pTip.GetValue(pd) == null) pTip.SetValue(pd, img32); }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"ApplyPulldownIcon: set ToolTipImage failed: {ex.Message}");
            }
        }
    }

    // ===============================================================
    // UnitsIconBuilder: 画像が無くても綺麗に描けるようにする専用ビルダー
    //   - PortIconBuilder と質感を揃えるため、背景プレートとアウトラインを共有
    // ===============================================================
    internal static class UnitsIconBuilder
    {
        // 共通プレート
        private static void DrawPlate(Media.DrawingContext dc, int sizePx)
        {
            // PortIconBuilder と似た質感：薄いエンボス
            var plate = new Media.LinearGradientBrush(Media.Color.FromRgb(245, 245, 245), Media.Color.FromRgb(220, 220, 220), 90);
            var border = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(180, 180, 180)), Math.Max(1, sizePx * 0.04));
            plate.Freeze(); border.Freeze();
            dc.DrawRoundedRectangle(plate, border, new System.Windows.Rect(0.5, 0.5, sizePx - 1, sizePx - 1), sizePx * 0.18, sizePx * 0.18);
        }

        private static Imaging.RenderTargetBitmap ToBitmap(Media.DrawingVisual dv, int sizePx)
        {
            var rtb = new Imaging.RenderTargetBitmap(sizePx, sizePx, 96, 96, Media.PixelFormats.Pbgra32);
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(rtb, System.Windows.Media.BitmapScalingMode.HighQuality);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static Media.Typeface Typeface()
            => new Media.Typeface(new Media.FontFamily("Segoe UI"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.Bold, System.Windows.FontStretches.Normal);

        private static void DrawCenteredText(Media.DrawingContext dc, string text, int sizePx, double ratio, Media.Brush fill, Media.Brush? stroke = null)
        {
            double fontSize = sizePx * ratio;
            var tf = Typeface();
            var ft = new Media.FormattedText(text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                tf, fontSize, fill, 1.0);

            double x = (sizePx - ft.Width) / 2.0;
            double y = (sizePx - ft.Height) / 2.0;

            if (stroke != null)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var fts = new Media.FormattedText(text,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            tf, fontSize, stroke, 1.0);
                        dc.DrawText(fts, new System.Windows.Point(x + dx, y + dy));
                    }
            }
            dc.DrawText(ft, new System.Windows.Point(x, y));
        }

        public static Media.ImageSource BuildSiIcon(int sizePx)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                // 定規っぽいベース
                double pad = sizePx * 0.18;
                var body = new System.Windows.Rect(pad, sizePx * 0.58, sizePx - pad * 2, sizePx * 0.18);
                var fill = new Media.SolidColorBrush(Media.Color.FromRgb(18, 164, 128)); // ティール
                var pen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(10, 110, 90)), Math.Max(1, sizePx * 0.05));
                fill.Freeze(); (pen.Brush as Media.SolidColorBrush)!.Freeze(); pen.Freeze();
                dc.DrawRoundedRectangle(fill, pen, body, sizePx * 0.05, sizePx * 0.05);

                // “mm”
                var textFill = Media.Brushes.White;
                var textStroke = new Media.SolidColorBrush(Media.Color.FromArgb(230, 0, 0, 0));
                textStroke.Freeze();
                DrawCenteredText(dc, "mm", sizePx, sizePx <= 16 ? 0.50 : 0.48, textFill, textStroke);
            }
            return ToBitmap(dv, sizePx);
        }

        public static Media.ImageSource BuildProjectIcon(int sizePx)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                // 用紙アイコン
                double pad = sizePx * 0.18;
                var rect = new System.Windows.Rect(pad, pad, sizePx - pad * 2, sizePx - pad * 2);
                var fill = new Media.LinearGradientBrush(Media.Color.FromRgb(230, 226, 255), Media.Color.FromRgb(200, 190, 255), 90);
                var pen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(120, 100, 200)), Math.Max(1, sizePx * 0.04));
                (pen.Brush as Media.SolidColorBrush)!.Freeze(); pen.Freeze();
                dc.DrawRoundedRectangle(fill, pen, rect, sizePx * 0.10, sizePx * 0.10);

                // “P”
                var textFill = new Media.SolidColorBrush(Media.Color.FromRgb(80, 60, 180));
                textFill.Freeze();
                var stroke = new Media.SolidColorBrush(Media.Color.FromArgb(230, 255, 255, 255));
                stroke.Freeze();
                DrawCenteredText(dc, "P", sizePx, 0.60, textFill, stroke);
            }
            return ToBitmap(dv, sizePx);
        }

        public static Media.ImageSource BuildRawIcon(int sizePx)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                // メジャー（黄色ベルト）
                double pad = sizePx * 0.16;
                var belt = new System.Windows.Rect(pad, sizePx * 0.60, sizePx - pad * 2, sizePx * 0.18);
                var beltFill = new Media.LinearGradientBrush(Media.Color.FromRgb(255, 230, 120), Media.Color.FromRgb(240, 190, 60), 0);
                var beltPen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(170, 130, 70)), Math.Max(1, sizePx * 0.04));
                (beltPen.Brush as Media.SolidColorBrush)!.Freeze(); beltPen.Freeze();
                dc.DrawRoundedRectangle(beltFill, beltPen, belt, sizePx * 0.06, sizePx * 0.06);

                // “ft”
                var textFill = new Media.SolidColorBrush(Media.Color.FromRgb(60, 60, 60));
                textFill.Freeze();
                var stroke = new Media.SolidColorBrush(Media.Color.FromArgb(230, 255, 255, 255));
                stroke.Freeze();
                DrawCenteredText(dc, "ft", sizePx, 0.58, textFill, stroke);
            }
            return ToBitmap(dv, sizePx);
        }

        public static Media.ImageSource BuildBothIcon(int sizePx)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                // 左右ハーフ
                double pad = sizePx * 0.12;
                var left = new System.Windows.Rect(pad, pad, (sizePx - pad * 2) / 2.0 - sizePx * 0.02, sizePx - pad * 2);
                var right = new System.Windows.Rect(left.X + left.Width + sizePx * 0.04, pad, left.Width, left.Height);

                var pen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(160, 160, 160)), Math.Max(1, sizePx * 0.03));
                (pen.Brush as Media.SolidColorBrush)!.Freeze(); pen.Freeze();

                var lFill = new Media.SolidColorBrush(Media.Color.FromRgb(18, 164, 128));
                var rFill = new Media.LinearGradientBrush(Media.Color.FromRgb(230, 226, 255), Media.Color.FromRgb(200, 190, 255), 90);
                lFill.Freeze(); rFill.Freeze();

                dc.DrawRoundedRectangle(lFill, pen, left, sizePx * 0.08, sizePx * 0.08);
                dc.DrawRoundedRectangle(rFill, pen, right, sizePx * 0.08, sizePx * 0.08);

                var white = Media.Brushes.White;
                var dark = new Media.SolidColorBrush(Media.Color.FromRgb(80, 60, 180)); dark.Freeze();
                var stroke = new Media.SolidColorBrush(Media.Color.FromArgb(230, 0, 0, 0)); stroke.Freeze();

                // 左 “mm”、右 “P”
                DrawCenteredText(dc, "mm", sizePx, 0.42, white, stroke);
                // 右に寄せるため少しオフセット
                var tf = Typeface();
                var ftP = new Media.FormattedText("P",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    tf, sizePx * 0.55, dark, 1.0);
                double x = right.X + (right.Width - ftP.Width) / 2.0;
                double y = right.Y + (right.Height - ftP.Height) / 2.0;
                // ストローク
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var fts = new Media.FormattedText("P",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            tf, sizePx * 0.55, Media.Brushes.White, 1.0);
                        dc.DrawText(fts, new System.Windows.Point(x + dx, y + dy));
                    }
                dc.DrawText(ftP, new System.Windows.Point(x, y));
            }
            return ToBitmap(dv, sizePx);
        }

        public static Media.ImageSource BuildPulldownIcon(int sizePx)
        {
            // 汎用 "U" マーク（グレー基調）
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);
                var fill = new Media.SolidColorBrush(Media.Color.FromRgb(90, 90, 90)); fill.Freeze();
                var stroke = new Media.SolidColorBrush(Media.Color.FromArgb(220, 255, 255, 255)); stroke.Freeze();
                DrawCenteredText(dc, "U", sizePx, 0.60, fill, stroke);
            }
            return ToBitmap(dv, sizePx);
        }

        public static Media.ImageSource BuildRulerProtractorIcon(int sizePx)
        {
            const double RULER_ANGLE_DEG = -33.0;
            double protractorBaseY = sizePx * 0.78;  // 分度器の底辺（下寄せ）
            double protractorRadius = sizePx * 0.40;  // 半径
            double rulerWidth = sizePx * 0.88;
            double rulerHeight = sizePx * 0.17;
            double rulerCenterX = sizePx * 0.47;
            double rulerCenterY = sizePx * 0.30;

            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                // ===== 分度器（上凸 ∩） =====
                double cx = sizePx * 0.50;
                double cy = protractorBaseY;
                double r = protractorRadius;

                var pL = new System.Windows.Point(cx - r, cy);
                var pR = new System.Windows.Point(cx + r, cy);

                var path = new Media.StreamGeometry();
                using (var g = path.Open())
                {
                    g.BeginFigure(pL, isFilled: true, isClosed: true);
                    // ★ ここを Clockwise にすることで上向きの半円（∩）になります
                    g.ArcTo(pR, new System.Windows.Size(r, r), 0,
                            isLargeArc: false,
                            Media.SweepDirection.Clockwise,  // ← 修正
                            isStroked: true, isSmoothJoin: false);
                }
                path.Freeze();

                var proFill = new Media.LinearGradientBrush(
                    Media.Color.FromRgb(224, 248, 240),
                    Media.Color.FromRgb(190, 236, 224), 90);
                var proPen = new Media.Pen(
                    new Media.SolidColorBrush(Media.Color.FromRgb(18, 164, 128)),
                    Math.Max(1, sizePx * 0.05));
                (proPen.Brush as Media.SolidColorBrush)!.Freeze();
                proFill.Freeze(); proPen.Freeze();

                dc.DrawGeometry(proFill, proPen, path);

                // 放射目盛（20°刻み程度）
                var tickPen = new Media.Pen(
                    new Media.SolidColorBrush(Media.Color.FromRgb(18, 164, 128)),
                    Math.Max(1, sizePx * 0.024));
                (tickPen.Brush as Media.SolidColorBrush)!.Freeze(); tickPen.Freeze();

                int ticks = 9; // 0〜180
                for (int i = 0; i <= ticks; i++)
                {
                    double angle = Math.PI - (Math.PI * i / ticks); // 左→右
                    double x1 = cx + (r - sizePx * 0.06) * Math.Cos(angle);
                    double y1 = cy - (r - sizePx * 0.06) * Math.Sin(angle); // ← 上側に出すので「-」
                    double x2 = cx + (r - sizePx * 0.16) * Math.Cos(angle);
                    double y2 = cy - (r - sizePx * 0.16) * Math.Sin(angle); // ← 上側に出すので「-」
                    dc.DrawLine(tickPen, new System.Windows.Point(x1, y1), new System.Windows.Point(x2, y2));
                }

                // 中央の小さな十字
                var crossPen = new Media.Pen(
                    new Media.SolidColorBrush(Media.Color.FromRgb(18, 164, 128)),
                    Math.Max(1, sizePx * 0.02));
                (crossPen.Brush as Media.SolidColorBrush)!.Freeze(); crossPen.Freeze();
                double c = sizePx * 0.05;
                dc.DrawLine(crossPen, new System.Windows.Point(cx - c, cy), new System.Windows.Point(cx + c, cy));
                dc.DrawLine(crossPen, new System.Windows.Point(cx, cy - c), new System.Windows.Point(cx, cy + c));

                // ===== 定規（左下→右上） =====
                var rulerRect = new System.Windows.Rect(
                    rulerCenterX - rulerWidth / 2.0,
                    rulerCenterY - rulerHeight / 2.0,
                    rulerWidth, rulerHeight);

                dc.PushTransform(new Media.RotateTransform(RULER_ANGLE_DEG, rulerCenterX, rulerCenterY));

                var shadow = new Media.SolidColorBrush(Media.Color.FromArgb(36, 0, 0, 0)); shadow.Freeze();
                dc.DrawRoundedRectangle(
                    shadow, null,
                    new System.Windows.Rect(rulerRect.X + sizePx * 0.02, rulerRect.Y + sizePx * 0.02, rulerRect.Width, rulerRect.Height),
                    sizePx * 0.06, sizePx * 0.06);

                var rFill = new Media.LinearGradientBrush(
                    Media.Color.FromRgb(255, 236, 170),
                    Media.Color.FromRgb(250, 210, 120), 0);
                var rPen = new Media.Pen(
                    new Media.SolidColorBrush(Media.Color.FromRgb(170, 130, 70)),
                    Math.Max(1, sizePx * 0.04));
                (rPen.Brush as Media.SolidColorBrush)!.Freeze(); rFill.Freeze(); rPen.Freeze();

                dc.DrawRoundedRectangle(rFill, rPen, rulerRect, sizePx * 0.06, sizePx * 0.06);

                var mPen = new Media.Pen(
                    new Media.SolidColorBrush(Media.Color.FromRgb(150, 110, 60)),
                    Math.Max(1, sizePx * 0.03));
                (mPen.Brush as Media.SolidColorBrush)!.Freeze(); mPen.Freeze();

                int div = 10;
                for (int i = 1; i < div; i++)
                {
                    double x = rulerRect.X + rulerRect.Width * i / div;
                    double y1 = rulerRect.Y + rulerRect.Height * 0.18;
                    double y2 = rulerRect.Y + rulerRect.Height * 0.82;
                    dc.DrawLine(mPen, new System.Windows.Point(x, y1), new System.Windows.Point(x, y2));
                }
                dc.Pop();
            }
            return ToBitmap(dv, sizePx);
        }

    }
}
