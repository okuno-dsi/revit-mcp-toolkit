#nullable enable
// ================================================================
// File: RibbonViewWorkspace.cs
// Purpose: View Workspace (autosave / restore) の操作用リボンパネル
// Tab: RevitMCPServer
// Notes:
//  - UI からは「自動保存 ON/OFF」「設定リセット」を提供
//  - 復元時にビューが削除されている場合はスキップ（RestoreCoordinator 側で best-effort）
// ================================================================

using System;
using System.Globalization;
using System.Linq;
using System.Windows;                   // FlowDirection / Point / Rect / System.Windows.Font*
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core.ViewWorkspace;

// WPF メディア型の衝突回避
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;

namespace RevitMCPAddin.UI
{
    public static class RibbonViewWorkspace
    {
        public static void AddPanel(UIControlledApplication app, string dllPathForCommands)
        {
            var tab = RibbonPortUi.TabName; // "RevitMCPServer"
            try { app.CreateRibbonTab(tab); } catch { /* 既にある */ }

            var panel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "Workspace")
                        ?? app.CreateRibbonPanel(tab, "Workspace");

            Media.ImageSource Icon(string text, Media.Color color)
                => CreateSquareGlyph(text, color, size: 16, cornerRadius: 3);
            Media.ImageSource LargeIcon(string text, Media.Color color)
                => CreateSquareGlyph(text, color, size: 32, cornerRadius: 6);

            // ---- Autosave Toggle ----
            {
                var pd = new PushButtonData(
                    "VW_ToggleAutosave",
                    "Autosave\nToggle",
                    dllPathForCommands,
                    typeof(ToggleWorkspaceAutosave).FullName)
                {
                    ToolTip = "View Workspace の自動保存をON/OFFします（クラッシュ対策）。"
                };

                var btn = (PushButton)panel.AddItem(pd);
                btn.Image = Icon("A", Media.Color.FromRgb(0, 150, 136));
                btn.LargeImage = LargeIcon("A", Media.Color.FromRgb(0, 150, 136));
            }

            // ---- Reset (defaults) ----
            {
                var pd = new PushButtonData(
                    "VW_ResetDefaults",
                    "Reset\nDefaults",
                    dllPathForCommands,
                    typeof(ResetWorkspaceDefaults).FullName)
                {
                    ToolTip = "View Workspace の設定を既定値に戻します（autosave/restore/interval/retention 等）。"
                };

                var btn = (PushButton)panel.AddItem(pd);
                btn.Image = Icon("R", Media.Color.FromRgb(90, 90, 90));
                btn.LargeImage = LargeIcon("R", Media.Color.FromRgb(90, 90, 90));
            }
        }

        //============================================================
        // External Commands
        //============================================================

        [Transaction(TransactionMode.Manual), Regeneration(RegenerationOption.Manual)]
        public class ToggleWorkspaceAutosave : IExternalCommand
        {
            public Result Execute(ExternalCommandData data, ref string msg, ElementSet el)
            {
                try
                {
                    var before = ViewWorkspaceService.CurrentSettings;
                    bool newEnabled = !before.AutosaveEnabled;

                    ViewWorkspaceService.SetAutosave(newEnabled, before.AutosaveIntervalMinutes, before.Retention);

                    var after = ViewWorkspaceService.CurrentSettings;
                    TaskDialog.Show(
                        "Workspace Autosave",
                        $"Autosave: {(after.AutosaveEnabled ? "ON" : "OFF")}\n" +
                        $"Interval: {after.AutosaveIntervalMinutes} min\n" +
                        $"Retention: {after.Retention}\n" +
                        $"AutoRestore: {(after.AutoRestoreEnabled ? "ON" : "OFF")}");

                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Workspace Autosave", "Failed: " + ex.Message);
                    return Result.Failed;
                }
            }
        }

        [Transaction(TransactionMode.Manual), Regeneration(RegenerationOption.Manual)]
        public class ResetWorkspaceDefaults : IExternalCommand
        {
            public Result Execute(ExternalCommandData data, ref string msg, ElementSet el)
            {
                try
                {
                    var td = new TaskDialog("Reset View Workspace")
                    {
                        MainInstruction = "View Workspace settings will be reset to defaults.",
                        MainContent = "This affects autosave/restore, interval, retention, and capture options.\nProceed?",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No
                    };

                    if (td.Show() != TaskDialogResult.Yes)
                        return Result.Cancelled;

                    ViewWorkspaceService.ResetToDefaults();

                    var s = ViewWorkspaceService.CurrentSettings;
                    TaskDialog.Show(
                        "Reset View Workspace",
                        "Done.\n" +
                        $"AutoRestore: {(s.AutoRestoreEnabled ? "ON" : "OFF")}\n" +
                        $"Autosave: {(s.AutosaveEnabled ? "ON" : "OFF")} ({s.AutosaveIntervalMinutes} min)\n" +
                        $"Retention: {s.Retention}\n" +
                        $"IncludeZoom: {(s.IncludeZoom ? "ON" : "OFF")}\n" +
                        $"Include3D: {(s.Include3dOrientation ? "ON" : "OFF")}");

                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Reset View Workspace", "Failed: " + ex.Message);
                    return Result.Failed;
                }
            }
        }

        //============================================================
        // Icon helpers（WPF Drawing → BitmapSource）
        //============================================================
        private static Media.ImageSource CreateSquareGlyph(string letter, Media.Color color, int size, double cornerRadius)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 背景プレート
                var rect = new Rect(0, 0, size, size);
                var top = Media.Color.FromRgb(
                    (byte)Math.Min(color.R + 40, 255),
                    (byte)Math.Min(color.G + 40, 255),
                    (byte)Math.Min(color.B + 40, 255));
                var bg = new Media.LinearGradientBrush(top, color, 90.0);
                var pen = new Media.Pen(Media.Brushes.Transparent, 0);
                bg.Freeze(); pen.Freeze();
                dc.DrawRoundedRectangle(bg, pen, rect, cornerRadius, cornerRadius);

                // 文字（中央寄せ）
                DrawCenteredLetter(dc, letter, size, Media.Brushes.White, 0.0);
            }

            var bmp = new Imaging.RenderTargetBitmap(size, size, 96, 96, Media.PixelFormats.Pbgra32);
            Media.RenderOptions.SetBitmapScalingMode(bmp, Media.BitmapScalingMode.HighQuality);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static void DrawCenteredLetter(Media.DrawingContext dc, string letter, int s, Media.Brush fill, double yAdjust)
        {
            var typeface = new Media.Typeface(
                new Media.FontFamily("Segoe UI"),
                System.Windows.FontStyles.Normal,
                System.Windows.FontWeights.Bold,
                System.Windows.FontStretches.Normal);

            double fontSize = s * 0.55;

            var ft = new Media.FormattedText(
                letter,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                fill,
                1.0);

            double x = (s - ft.Width) / 2.0;
            double y = (s - ft.Height) / 2.0 + s * yAdjust;

            dc.DrawText(ft, new System.Windows.Point(x, y));
        }
    }
}

