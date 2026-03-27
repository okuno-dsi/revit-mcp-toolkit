// ================================================================
// File: UI/RibbonPortUi.cs
// Tab: RevitMCPServer
// Panels:
//   - RevitMCPServer（既存）: Port:#### / Start Server(緑) / Stop Server(赤) / Start Bridge(青) / Stop Bridge(橙)
//   - Developer（拡張）   : Open Addin Folder / Open Logs Folder / Open Settings / Reload Settings
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using Imaging = System.Windows.Media.Imaging;
// WPF
using Media = System.Windows.Media;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Ledger;
using RevitMCPAddin.Core.Net;
using Forms = System.Windows.Forms;

namespace RevitMCPAddin.UI
{
    public static class RibbonPortUi
    {
        // ---- Tab / Panels ----
        public const string TabName = "RevitMCPServer";
        public const string PanelName = "RevitMCPServer";   // 既存
        public const string DevPanelName = "Developer";     // 追加
        public const string GuiPanelName = "GUI";           // Codex GUI / Python Runner / Chat
        public const string PickPanelName = "Tools";        // Pick Info only

        // ---- Port ----
        public const string ButtonNamePort = "ShowPort";
        public const string ButtonClassPort = "RevitMCPAddin.UI.ShowPortCommand";

        // ---- Server ----
        public const string ButtonNameStartServer = "StartServer";
        public const string ButtonNameStopServer = "StopServer";
        public const string ButtonClassStartServer = "RevitMCPAddin.Commands.ServerControl.StartMcpServerCommand";
        public const string ButtonClassStopServer = "RevitMCPAddin.Commands.ServerControl.StopMcpServerCommand";

        // ---- Bridge（色変更：緑→青、赤→オレンジ）----
        public const string ButtonNameStartBridge = "StartBridge";
        public const string ButtonNameStopBridge = "StopBridge";
        // ★ 既存の Bridge コマンド クラス名に合わせてください（下は一般的な例）
        public const string ButtonClassStartBridge = "RevitMCPAddin.Commands.BridgeControl.StartBridgeCommand";
        public const string ButtonClassStopBridge = "RevitMCPAddin.Commands.BridgeControl.StopBridgeCommand";

        // ---- Developer: 「開く」系 + 設定系（拡張）----
        public const string ButtonNameOpenAddin = "OpenAddinFolder";
        public const string ButtonNameOpenLogs = "OpenLogsFolder";
        public const string ButtonNameOpenProjectFolder = "OpenProjectFolder";
        public const string ButtonClassOpenAddin = "RevitMCPAddin.Commands.Dev.OpenAddinFolderCommand";
        public const string ButtonClassOpenLogs = "RevitMCPAddin.Commands.Dev.OpenLogFolderCommand";
        public const string ButtonClassOpenProjectFolder = "RevitMCPAddin.Commands.Dev.OpenActiveProjectFolderCommand";

        // 新規: Codex GUI 起動
        public const string ButtonNameLaunchCodexGui = "LaunchCodexGui";
        public const string ButtonClassLaunchCodexGui = "RevitMCPAddin.Commands.Dev.LaunchCodexGuiCommand";

        // 新規: MCP Chat Pane（DockablePane）
        public const string ButtonNameToggleChatPane = "ToggleChatPane";
        public const string ButtonClassToggleChatPane = "RevitMCPAddin.Commands.Chat.ToggleChatPaneCommand";

        // 新規: Python Script Runner（人間専用）
        public const string ButtonNamePythonRunner = "PythonRunner";
        public const string ButtonClassPythonRunner = "RevitMCPAddin.Commands.ShowPythonRunnerCommand";
        // 新規: Pick Info（クリック情報収集）
        public const string ButtonNameInfoPick = "InfoPick";
        public const string ButtonClassInfoPick = "RevitMCPAddin.Commands.ShowInfoPickCommand";

        // ---- Build Info ----
        public const string ButtonNameBuildInfo = "ShowBuildInfo";
        public const string ButtonClassShowBuildInfo = "RevitMCPAddin.Commands.SystemOps.ShowBuildInfoCommand";
        public const string ButtonNameRoomHtmlLink = "ShowRoomHtmlLink";
        public const string ButtonClassShowRoomHtmlLink = "RevitMCPAddin.UI.ShowRoomHtmlLinkCommand";
        public const string ButtonNameRunScheduleQueue = "RunScheduleQueue";
        public const string ButtonClassRunScheduleQueue = "RevitMCPAddin.UI.RunScheduleQueueCommand";

        // ★ 新規: Settings 操作用
        public const string ButtonNameOpenSettings = "OpenSettings";
        public const string ButtonNameReloadSettings = "ReloadSettings";
        public const string ButtonClassOpenSettings = "RevitMCPAddin.Commands.SystemOps.OpenSettingsCommand";
        public const string ButtonClassReloadSettings = "RevitMCPAddin.Commands.SystemOps.ReloadServerSettingsCommand";


        private static int _currentPort;
        private static DateTime _lastQueueRefreshUtc = DateTime.MinValue;
        private static string _lastQueueDocGuid = string.Empty;
        private static int _lastQueueCount = -1;

        public static void Setup(UIControlledApplication app, int? port = null, string? iconDir = null)
        {
            _currentPort = port ?? PortSettings.GetPort();

            try { app.CreateRibbonTab(TabName); } catch { /* 既存なら無視 */ }

            // ===== 既存の RevitMCPServer パネル =====
            var panel = app.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == PanelName)
                        ?? app.CreateRibbonPanel(TabName, PanelName);

            string asm = Assembly.GetExecutingAssembly().Location;

            // --- Port ---
            var portBtn = EnsurePushButton(panel, ButtonNamePort, "Port:" + _currentPort, asm, ButtonClassPort);
            portBtn.ToolTip = "MCP サーバーの待ち受けポート：" + _currentPort;
            portBtn.LongDescription = "外部 MCP サーバーとの通信に使用する現在のポート番号。";
            {
                var base16 = PortIconBuilder.ResolveIconPath(iconDir, "port_base_16.png");
                var base32 = PortIconBuilder.ResolveIconPath(iconDir, "port_base_32.png");
                portBtn.Image = PortIconBuilder.BuildPortBadge(_currentPort, 16, base16);
                portBtn.LargeImage = PortIconBuilder.BuildPortBadge(_currentPort, 32, base32);
            }

            // --- Build Info (version/commit) ---
            var buildVer = BuildInfo.GetDisplayVersion();
            var buildBtn = EnsurePushButton(panel, ButtonNameBuildInfo, "Build\n" + buildVer, asm, ButtonClassShowBuildInfo);
            buildBtn.ToolTip = "RevitMCP Add-in build: " + buildVer;
            buildBtn.LongDescription = "ビルド日時＋コミットIDを含むバージョン表記です。";
            {
                buildBtn.Image = PortIconBuilder.BuildSquareIcon(16,
                    fill: Media.Color.FromRgb(90, 90, 90),
                    stroke: Media.Color.FromRgb(40, 40, 40));
                buildBtn.LargeImage = PortIconBuilder.BuildSquareIcon(32,
                    fill: Media.Color.FromRgb(90, 90, 90),
                    stroke: Media.Color.FromRgb(40, 40, 40));
            }

            var roomHtmlBtn = EnsurePushButton(panel, ButtonNameRoomHtmlLink, BuildScheduleHtmlLinkText(), asm, ButtonClassShowRoomHtmlLink);
            roomHtmlBtn.ToolTip = "集計表 Excel 往復編集用の HTML リンクを表示します。LAN 用 URL をクリップボードへコピーします。";
            roomHtmlBtn.LongDescription = "現在の文書に対する signed HTML URL を生成し、ローカルホスト用と LAN 用のアドレスを表示します。";
            {
                roomHtmlBtn.Image = PortIconBuilder.BuildTextBadge("URL", 16);
                roomHtmlBtn.LargeImage = PortIconBuilder.BuildTextBadge("URL", 32);
            }

            var runQueueBtn = EnsurePushButton(panel, ButtonNameRunScheduleQueue, BuildRunQueueText(0), asm, ButtonClassRunScheduleQueue);
            runQueueBtn.ToolTip = "アクティブ文書に対するキュー済み HTML 変更リクエストの次の1件を、今すぐ確認・処理します。";
            runQueueBtn.LongDescription = "キュー済み変更の確認ダイアログを即時に表示し、反映可否を判断できます。";
            {
                runQueueBtn.Image = PortIconBuilder.BuildTextBadge("Q", 16);
                runQueueBtn.LargeImage = PortIconBuilder.BuildTextBadge("Q", 32);
            }

            RefreshScheduleHtmlQueueButtonsCore(app, null, force: true);

            // =============== Server（緑／赤） ===============
            {
                // Start Server（緑）
                var startBtn = EnsurePushButton(panel, ButtonNameStartServer, "Start Server", asm, ButtonClassStartServer);
                startBtn.ToolTip = "MCP サーバーを開始します。";
                var i16 = PortIconBuilder.ResolveIconPath(iconDir, "server_start_green_16.png");
                var i32 = PortIconBuilder.ResolveIconPath(iconDir, "server_start_green_32.png");
                startBtn.Image = PortIconBuilder.TryLoadBitmapAsImage(i16) ?? PortIconBuilder.BuildTriangleIcon(16,
                                            fill: Media.Color.FromRgb(12, 158, 93),   // 緑
                                            stroke: Media.Color.FromRgb(0, 100, 60));
                startBtn.LargeImage = PortIconBuilder.TryLoadBitmapAsImage(i32) ?? PortIconBuilder.BuildTriangleIcon(32,
                                            fill: Media.Color.FromRgb(12, 158, 93),
                                            stroke: Media.Color.FromRgb(0, 100, 60));

                // Stop Server（赤）
                var stopBtn = EnsurePushButton(panel, ButtonNameStopServer, "Stop Server", asm, ButtonClassStopServer);
                stopBtn.ToolTip = "MCP サーバーを停止します。";
                var j16 = PortIconBuilder.ResolveIconPath(iconDir, "server_stop_red_16.png");
                var j32 = PortIconBuilder.ResolveIconPath(iconDir, "server_stop_red_32.png");
                stopBtn.Image = PortIconBuilder.TryLoadBitmapAsImage(j16) ?? PortIconBuilder.BuildSquareIcon(16,
                                            fill: Media.Color.FromRgb(225, 38, 38),   // 赤
                                            stroke: Media.Color.FromRgb(140, 0, 0));
                stopBtn.LargeImage = PortIconBuilder.TryLoadBitmapAsImage(j32) ?? PortIconBuilder.BuildSquareIcon(32,
                                            fill: Media.Color.FromRgb(225, 38, 38),
                                            stroke: Media.Color.FromRgb(140, 0, 0));
            }

            // =============== Bridge（青／オレンジ） ===============
            {
                // Start Bridge（青）
                var startBtn = EnsurePushButton(panel, ButtonNameStartBridge, "Start Bridge", asm, ButtonClassStartBridge);
                startBtn.ToolTip = "Bridge を開始します。";
                var i16 = PortIconBuilder.ResolveIconPath(iconDir, "bridge_start_blue_16.png");
                var i32 = PortIconBuilder.ResolveIconPath(iconDir, "bridge_start_blue_32.png");
                startBtn.Image = PortIconBuilder.TryLoadBitmapAsImage(i16) ?? PortIconBuilder.BuildTriangleIcon(16,
                                            fill: Media.Color.FromRgb(45, 120, 220),  // 青
                                            stroke: Media.Color.FromRgb(20, 70, 160));
                startBtn.LargeImage = PortIconBuilder.TryLoadBitmapAsImage(i32) ?? PortIconBuilder.BuildTriangleIcon(32,
                                            fill: Media.Color.FromRgb(45, 120, 220),
                                            stroke: Media.Color.FromRgb(20, 70, 160));

                // Stop Bridge（オレンジ）
                var stopBtn = EnsurePushButton(panel, ButtonNameStopBridge, "Stop Bridge", asm, ButtonClassStopBridge);
                stopBtn.ToolTip = "Bridge を停止します。";
                var j16 = PortIconBuilder.ResolveIconPath(iconDir, "bridge_stop_orange_16.png");
                var j32 = PortIconBuilder.ResolveIconPath(iconDir, "bridge_stop_orange_32.png");
                stopBtn.Image = PortIconBuilder.TryLoadBitmapAsImage(j16) ?? PortIconBuilder.BuildSquareIcon(16,
                                            fill: Media.Color.FromRgb(240, 140, 0),   // オレンジ
                                            stroke: Media.Color.FromRgb(180, 90, 0));
                stopBtn.LargeImage = PortIconBuilder.TryLoadBitmapAsImage(j32) ?? PortIconBuilder.BuildSquareIcon(32,
                                            fill: Media.Color.FromRgb(240, 140, 0),
                                            stroke: Media.Color.FromRgb(180, 90, 0));
            }

            // ===== Developer パネル（拡張）=====
            var devPanel = app.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == DevPanelName)
                           ?? app.CreateRibbonPanel(TabName, DevPanelName);

            var btnOpenAddin = EnsurePushButton(devPanel, ButtonNameOpenAddin, "Open\nAddin Folder", asm, ButtonClassOpenAddin);
            btnOpenAddin.ToolTip = "アドインDLLが配置されているフォルダを開きます。";
            {
                var a16 = PortIconBuilder.ResolveIconPath(iconDir, "open_folder_16.png");
                var a32 = PortIconBuilder.ResolveIconPath(iconDir, "open_folder_32.png");
                btnOpenAddin.Image = PortIconBuilder.TryLoadBitmapAsImage(a16) ?? PortIconBuilder.BuildFolderIcon(16);
                btnOpenAddin.LargeImage = PortIconBuilder.TryLoadBitmapAsImage(a32) ?? PortIconBuilder.BuildFolderIcon(32);
            }

            var btnOpenLogs = EnsurePushButton(devPanel, ButtonNameOpenLogs, "Open\nLogs Folder", asm, ButtonClassOpenLogs);
            btnOpenLogs.ToolTip = "%LOCALAPPDATA%\\RevitMCP\\logs を開きます。";
            {
                var l16 = PortIconBuilder.ResolveIconPath(iconDir, "open_logs_16.png");
                var l32 = PortIconBuilder.ResolveIconPath(iconDir, "open_logs_32.png");
                btnOpenLogs.Image = PortIconBuilder.TryLoadBitmapAsImage(l16) ?? PortIconBuilder.BuildFolderIcon(16, isLogs: true);
                btnOpenLogs.LargeImage = PortIconBuilder.TryLoadBitmapAsImage(l32) ?? PortIconBuilder.BuildFolderIcon(32, isLogs: true);
            }

            var btnOpenProjectFolder = EnsurePushButton(devPanel, ButtonNameOpenProjectFolder, "Open\nProject Folder", asm, ButtonClassOpenProjectFolder);
            btnOpenProjectFolder.ToolTip = "現在アクティブな文書に対応する Revit_MCP のプロジェクトフォルダを開きます。";
            {
                btnOpenProjectFolder.Image = PortIconBuilder.BuildFolderIcon(16);
                btnOpenProjectFolder.LargeImage = PortIconBuilder.BuildFolderIcon(32);
            }

            // ★ 新規: Open Settings / Reload Settings
            var btnOpenSettings = EnsurePushButton(devPanel, ButtonNameOpenSettings, "Open\nSettings", asm, ButtonClassOpenSettings);
            btnOpenSettings.ToolTip = "MCPサーバーの settings.json を開きます。";
            {
                btnOpenSettings.Image = PortIconBuilder.BuildFolderIcon(16);
                btnOpenSettings.LargeImage = PortIconBuilder.BuildFolderIcon(32);
            }

            var btnReloadSettings = EnsurePushButton(devPanel, ButtonNameReloadSettings, "Reload\nSettings", asm, ButtonClassReloadSettings);
            btnReloadSettings.ToolTip = "サーバーに設定再読込（/rpc/reload_config）を要求します。";
            {
                // 既存ビルダーを流用（色だけ変える）
                btnReloadSettings.Image = PortIconBuilder.BuildTriangleIcon(16,
                    fill: Media.Color.FromRgb(120, 120, 120), stroke: Media.Color.FromRgb(60, 60, 60));
                btnReloadSettings.LargeImage = PortIconBuilder.BuildTriangleIcon(32,
                    fill: Media.Color.FromRgb(120, 120, 120), stroke: Media.Color.FromRgb(60, 60, 60));
            }
        }

        public static void UpdatePort(UIControlledApplication app, int newPort, string? iconDir = null)
        {
            _currentPort = newPort;

            var panel = app.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == PanelName);
            if (panel == null) { Setup(app, newPort, iconDir); return; }

            var btn = panel.GetItems().OfType<PushButton>().FirstOrDefault(b => b.Name == ButtonNamePort);
            if (btn == null) { Setup(app, newPort, iconDir); return; }

            btn.ItemText = "Port:" + newPort;
            btn.ToolTip = "MCP サーバーの待ち受けポート：" + newPort;

            var base16 = PortIconBuilder.ResolveIconPath(iconDir, "port_base_16.png");
            var base32 = PortIconBuilder.ResolveIconPath(iconDir, "port_base_32.png");
            btn.Image = PortIconBuilder.BuildPortBadge(newPort, 16, base16);
            btn.LargeImage = PortIconBuilder.BuildPortBadge(newPort, 32, base32);
        }

        public static void UpdatePort(UIApplication uiapp, int newPort, string? iconDir = null)
        {
            _currentPort = newPort;
            try
            {
                Environment.SetEnvironmentVariable("REVIT_MCP_PORT", newPort.ToString());
                RevitMCPAddin.Core.PortLocator.SaveCurrentPort(newPort);
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"Set REVIT_MCP_PORT/SaveCurrentPort failed: {ex.Message}");
            }

            var uica = RevitMCPAddin.AppServices.UIControlledApp;
            if (uica != null)
            {
                try
                {
                    UpdatePort(uica, newPort, iconDir);
                    RefreshScheduleHtmlQueueButtonsCore(uica, uiapp, force: true);
                    return;
                }
                catch { /* 最低限: 環境変数は更新済み */ }
            }
        }

        public static void RefreshScheduleHtmlQueueButtons(UIApplication? uiapp, bool force = false)
        {
            var uica = global::RevitMCPAddin.AppServices.UIControlledApp;
            if (uica == null)
                return;
            RefreshScheduleHtmlQueueButtonsCore(uica, uiapp, force);
        }

        private static void RefreshScheduleHtmlQueueButtonsCore(UIControlledApplication application, UIApplication? uiapp, bool force)
        {
            var now = DateTime.UtcNow;
            var docGuid = TryGetActiveDocGuid(uiapp) ?? string.Empty;
            if (!force
                && string.Equals(docGuid, _lastQueueDocGuid, StringComparison.OrdinalIgnoreCase)
                && _lastQueueRefreshUtc != DateTime.MinValue
                && (now - _lastQueueRefreshUtc).TotalSeconds < 5)
            {
                return;
            }

            var count = HtmlScheduleImportQueueService.GetQueuedCountForActiveDocument(uiapp);
            if (!force && string.Equals(docGuid, _lastQueueDocGuid, StringComparison.OrdinalIgnoreCase) && count == _lastQueueCount)
            {
                _lastQueueRefreshUtc = now;
                return;
            }

            _lastQueueRefreshUtc = now;
            _lastQueueDocGuid = docGuid;
            _lastQueueCount = count;

            var panel = application.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == PanelName);
            if (panel == null)
                return;

            var linkBtn = panel.GetItems().OfType<PushButton>().FirstOrDefault(b => b.Name == ButtonNameRoomHtmlLink);
            if (linkBtn != null)
            {
                linkBtn.ItemText = BuildScheduleHtmlLinkText();
                linkBtn.ToolTip = "集計表 Excel 往復編集用の HTML リンクを表示します。LAN 用 URL をクリップボードへコピーします。";
            }

            var runBtn = panel.GetItems().OfType<PushButton>().FirstOrDefault(b => b.Name == ButtonNameRunScheduleQueue);
            if (runBtn != null)
            {
                runBtn.ItemText = BuildRunQueueText(count);
                runBtn.ToolTip = count > 0
                    ? $"アクティブ文書に対する未処理キュー {count} 件のうち、次の1件を今すぐ確認・処理します。"
                    : "アクティブ文書に対するキュー済み HTML 変更リクエストはありません。";
            }
        }

        private static string BuildScheduleHtmlLinkText()
            => "Schedule HTML\nLink";

        private static string BuildRunQueueText(int queueCount)
            => queueCount > 0 ? $"Run Queue\n({queueCount})" : "Run Queue";

        private static string? TryGetActiveDocGuid(UIApplication? uiapp)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return null;

            try
            {
                return DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out _);
            }
            catch
            {
                return null;
            }
        }

        private static PushButton EnsurePushButton(RibbonPanel panel, string name, string text, string assemblyPath, string className)
        {
            var exists = panel.GetItems().OfType<PushButton>().FirstOrDefault(b => b.Name == name);
            if (exists != null) { exists.ItemText = text; return exists; }

            var data = new PushButtonData(name, text, assemblyPath, className);
            var created = panel.AddItem(data) as PushButton;
            return created ?? throw new InvalidOperationException("Failed to create PushButton: " + name);
        }

        // Additional panel for CPU priority toggling
        public static void AddPerformancePanel(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); } catch { }
            var panel = app.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == "Performance")
                        ?? app.CreateRibbonPanel(TabName, "Performance");

            string asm = Assembly.GetExecutingAssembly().Location;

            var btnHigh = EnsurePushButton(panel, "SetPriorityHigh", "CPU High", asm, "RevitMCPAddin.Commands.SystemOps.SetPriorityHighCommand");
            btnHigh.ToolTip = "Revitプロセスの CPU 優先度を High に設定します。";
            btnHigh.Image = PortIconBuilder.BuildTriangleIcon(16, fill: Media.Color.FromRgb(255, 215, 0), stroke: Media.Color.FromRgb(180, 150, 0));
            btnHigh.LargeImage = PortIconBuilder.BuildTriangleIcon(32, fill: Media.Color.FromRgb(255, 215, 0), stroke: Media.Color.FromRgb(180, 150, 0));

            var btnNorm = EnsurePushButton(panel, "SetPriorityNormal", "CPU Normal", asm, "RevitMCPAddin.Commands.SystemOps.SetPriorityNormalCommand");
            btnNorm.ToolTip = "Revitプロセスの CPU 優先度を Normal に設定します。";
            btnNorm.Image = PortIconBuilder.BuildTriangleIcon(16, fill: Media.Color.FromRgb(120, 120, 120), stroke: Media.Color.FromRgb(60, 60, 60));
            btnNorm.LargeImage = PortIconBuilder.BuildTriangleIcon(32, fill: Media.Color.FromRgb(120, 120, 120), stroke: Media.Color.FromRgb(60, 60, 60));
        }

        // Codex GUI 用パネル（リボン右端）
        public static void AddGuiPanel(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); } catch { }
            var pickPanel = app.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == PickPanelName)
                        ?? app.CreateRibbonPanel(TabName, PickPanelName);
            var panel = app.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == GuiPanelName)
                        ?? app.CreateRibbonPanel(TabName, GuiPanelName);

            string asm = Assembly.GetExecutingAssembly().Location;

            // Pick Info（クリック情報収集）
            var btnInfoPick = EnsurePushButton(pickPanel, ButtonNameInfoPick, "Pick\nInfo", asm, ButtonClassInfoPick);
            btnInfoPick.ToolTip = "クリックした点や要素の情報（座標/カテゴリ/ID/タイプ）を収集します。";
            {
                btnInfoPick.Image = PortIconBuilder.BuildTextBadge("Pick", 16);
                btnInfoPick.LargeImage = PortIconBuilder.BuildTextBadge("Pick", 32);
            }

            // Python Runner（人間専用）: Codex GUI の左に配置
            var btnPythonRunner = EnsurePushButton(panel, ButtonNamePythonRunner, "Python\nRunner", asm, ButtonClassPythonRunner);
            btnPythonRunner.ToolTip = "人間専用の Python Script Runner を開きます（AI/MCP からは起動不可）。";
            btnPythonRunner.LongDescription = "設計者が用意した Python スクリプトを実行するためのUI。";
            {
                btnPythonRunner.Image = PortIconBuilder.BuildTextBadge("Py", 16);
                btnPythonRunner.LargeImage = PortIconBuilder.BuildTextBadge("Py", 32);
            }

            var btnCodexGui = EnsurePushButton(panel, ButtonNameLaunchCodexGui, "Codex\nGUI", asm, ButtonClassLaunchCodexGui);
            btnCodexGui.ToolTip = "Codex GUI (Revit MCP 用フロントエンド) を起動します。";
            {
                btnCodexGui.Image = PortIconBuilder.BuildHalIcon(16);
                btnCodexGui.LargeImage = PortIconBuilder.BuildHalIcon(32);
            }

            var btnChat = EnsurePushButton(panel, ButtonNameToggleChatPane, "Chat", asm, ButtonClassToggleChatPane);
            btnChat.ToolTip = "MCP Chat（DockablePane）を表示/非表示します。";
            {
                btnChat.Image = PortIconBuilder.BuildTriangleIcon(16, fill: Media.Color.FromRgb(90, 160, 255), stroke: Media.Color.FromRgb(40, 90, 160));
                btnChat.LargeImage = PortIconBuilder.BuildTriangleIcon(32, fill: Media.Color.FromRgb(90, 160, 255), stroke: Media.Color.FromRgb(40, 90, 160));
            }
        }
    }

    //================================================================
    // ShowPort & PortSettings
    //================================================================
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class ShowPortCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            int port = PortSettings.GetPort();
            TaskDialog.Show("MCP Server", "現在のポート番号は " + port + " です。");
            return Result.Succeeded;
        }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class ShowRoomHtmlLinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var uiapp = data.Application;
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("Schedule HTML Link", "アクティブな文書がありません。");
                    return Result.Cancelled;
                }

                if (!LedgerDocKeyProvider.TryGetOrCreateDocKey(doc, createIfMissing: true, out var docGuid, out _, out var err)
                    || string.IsNullOrWhiteSpace(docGuid))
                {
                    TaskDialog.Show("Schedule HTML Link", "docGuid を取得できませんでした。\n" + (err ?? ""));
                    return Result.Failed;
                }

                var port = PortLocator.GetCurrentPortOrDefault(5210);
                var baseUrls = RoomHtmlLinkHelper.GetPreferredBaseUrls(port);
                var primaryBaseUrl = baseUrls.FirstOrDefault() ?? ("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture));
                var paramNames = RoomHtmlLinkHelper.CollectDefaultRoomParamNames(doc);

                var projectName = RoomHtmlLinkHelper.GetProjectName(doc);
                var editorUser = Environment.UserName ?? string.Empty;
                var queuedCount = HtmlScheduleImportQueueService.GetQueuedCountForActiveDocument(uiapp);
                var token = RoomHtmlLinkHelper.CreateRoomRoundtripToken(
                    port,
                    docGuid,
                    doc.Title ?? string.Empty,
                    projectName,
                    editorUser,
                    paramNames,
                    primaryBaseUrl);
                if (string.IsNullOrWhiteSpace(token))
                {
                    TaskDialog.Show("Schedule HTML Link", "サーバーから signed link を生成できませんでした。");
                    return Result.Failed;
                }

                var urls = baseUrls
                    .Select(u => u.TrimEnd('/') + "/room-excel-roundtrip?token=" + Uri.EscapeDataString(token))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var primaryUrl = urls.FirstOrDefault() ?? string.Empty;
                try
                {
                    if (!string.IsNullOrWhiteSpace(primaryUrl))
                        Forms.Clipboard.SetText(primaryUrl);
                }
                catch { /* best-effort */ }

                var td = new TaskDialog("Schedule HTML Link")
                {
                    MainInstruction = "Schedule Excel Roundtrip HTML リンク",
                    MainContent = "Primary URL をクリップボードへコピーしました。\n同一ローカルネットワーク上からアクセスできます。Windows Firewall で TCP " + port.ToString(CultureInfo.InvariantCulture) + " が許可されている必要があります。",
                    ExpandedContent =
                        "Primary URL\n" + primaryUrl + "\n\n" +
                        "All URLs\n" + string.Join("\n", urls) + "\n\n" +
                        "Queued requests\n" + queuedCount.ToString(CultureInfo.InvariantCulture) + "\n\n" +
                        "Project\n" + (projectName ?? string.Empty) + "\n\n" +
                        "Editor user\n" + (editorUser ?? string.Empty) + "\n\n" +
                        "docGuid\n" + docGuid,
                    FooterText = "Link generation API is local-only. Shared users open the signed URL itself."
                };
                td.Show();
                RibbonPortUi.RefreshScheduleHtmlQueueButtons(uiapp, force: true);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class RunScheduleQueueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var uiapp = data.Application;
                if (!HtmlScheduleImportQueueService.TryProcessNextNowForActiveDocument(uiapp, out var info))
                {
                    TaskDialog.Show("Run Queue", string.IsNullOrWhiteSpace(info) ? "キュー済みリクエストはありません。" : info);
                    RibbonPortUi.RefreshScheduleHtmlQueueButtons(uiapp, force: true);
                    return Result.Cancelled;
                }

                RibbonPortUi.RefreshScheduleHtmlQueueButtons(uiapp, force: true);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    public static class PortSettings
    {
        public static int GetPort()
        {
            var env = Environment.GetEnvironmentVariable("REVIT_MCP_PORT");
            if (int.TryParse(env, out var p) && p > 0 && p < 65536) return p;
            return 5210;
        }
    }

    internal static class RoomHtmlLinkHelper
    {
        public static List<string> GetPreferredBaseUrls(int port)
        {
            var urls = new List<string>();
            foreach (var ip in GetPrivateIpv4Addresses())
            {
                urls.Add("http://" + ip + ":" + port.ToString(CultureInfo.InvariantCulture));
            }
            urls.Add("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture));
            return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string CreateRoomRoundtripToken(int port, string docGuid, string docTitle, string projectName, string editorUser, IList<string> paramNames, string publicBaseUrl)
        {
            try
            {
                return CreateRoomRoundtripTokenCore(port, docGuid, docTitle, projectName, editorUser, paramNames, publicBaseUrl);
            }
            catch (HttpRequestException)
            {
                return RetryCreateRoomRoundtripToken(port, docGuid, docTitle, projectName, editorUser, paramNames, publicBaseUrl);
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0)
                    return RetryCreateRoomRoundtripToken(port, docGuid, docTitle, projectName, editorUser, paramNames, publicBaseUrl);
                throw;
            }
        }

        private static string RetryCreateRoomRoundtripToken(int port, string docGuid, string docTitle, string projectName, string editorUser, IList<string> paramNames, string publicBaseUrl)
        {
            var ownerPid = Process.GetCurrentProcess().Id;
            try { ServerProcessManager.StopByLock(ownerPid, port); } catch { }
            var started = ServerProcessManager.StartOrAttach(ownerPid);
            var effectivePort = started.port > 0 ? started.port : port;
            return CreateRoomRoundtripTokenCore(effectivePort, docGuid, docTitle, projectName, editorUser, paramNames, publicBaseUrl);
        }

        private static string CreateRoomRoundtripTokenCore(int port, string docGuid, string docTitle, string projectName, string editorUser, IList<string> paramNames, string publicBaseUrl)
        {
            var query = new List<string>
            {
                "docGuid=" + Uri.EscapeDataString(docGuid ?? string.Empty),
                "docTitle=" + Uri.EscapeDataString(docTitle ?? string.Empty),
                "projectName=" + Uri.EscapeDataString(projectName ?? string.Empty),
                "editorUser=" + Uri.EscapeDataString(editorUser ?? string.Empty),
                "baseUrl=" + Uri.EscapeDataString(publicBaseUrl ?? string.Empty)
            };

            if (paramNames != null && paramNames.Count > 0)
                query.Add("paramNames=" + Uri.EscapeDataString(string.Join(",", paramNames)));

            var url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)
                    + "/api/room-excel-roundtrip/create-link?"
                    + string.Join("&", query);

            using (var hc = new HttpClient())
            {
                hc.Timeout = TimeSpan.FromSeconds(8);
                var raw = hc.GetStringAsync(url).GetAwaiter().GetResult();
                var jo = JObject.Parse(raw);
                if (jo.Value<bool?>("ok") != true) return string.Empty;
                return jo.Value<string>("token") ?? string.Empty;
            }
        }

        public static List<string> CollectDefaultRoomParamNames(Document doc)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Take(20)
                .ToList();

            foreach (var room in rooms)
            {
                foreach (Parameter p in room.Parameters)
                {
                    var name = SafeParamName(p);
                    if (!IsEligibleRoomParam(p, name)) continue;
                    if (!counts.ContainsKey(name)) counts[name] = 0;
                    counts[name]++;
                }
            }

            var minHits = rooms.Count <= 1 ? 1 : Math.Min(3, rooms.Count);
            return counts
                .Where(kvp => kvp.Value >= minHits)
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        public static string GetProjectName(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi != null)
                {
                    var name = pi.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        return name.Trim();
                }
            }
            catch { }

            try
            {
                var title = doc?.Title;
                if (!string.IsNullOrWhiteSpace(title))
                    return title.Trim();
            }
            catch { }

            return string.Empty;
        }

        private static bool IsEligibleRoomParam(Parameter p, string name)
        {
            if (p == null || string.IsNullOrWhiteSpace(name)) return false;
            if (p.IsReadOnly) return false;

            var st = p.StorageType;
            if (st == StorageType.ElementId || st == StorageType.None) return false;

            switch (name.Trim())
            {
                case "Number":
                case "Name":
                case "Level":
                case "番号":
                case "名前":
                case "レベル":
                case "面積":
                case "Area":
                case "Volume":
                case "体積":
                case "Perimeter":
                case "周長":
                case "位相":
                case "Phase":
                    return false;
            }

            return true;
        }

        private static string SafeParamName(Parameter p)
        {
            try { return p.Definition?.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static IEnumerable<string> GetPrivateIpv4Addresses()
        {
            var ips = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni == null) continue;
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    IPInterfaceProperties props;
                    try { props = ni.GetIPProperties(); }
                    catch { continue; }

                    foreach (var ua in props.UnicastAddresses)
                    {
                        var ip = ua?.Address;
                        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ip)) continue;
                        if (!IsPrivateIpv4(ip)) continue;
                        ips.Add(ip.ToString());
                    }
                }
            }
            catch { /* ignore */ }

            return ips.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsPrivateIpv4(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            if (b == null || b.Length != 4) return false;
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            return false;
        }
    }

    //================================================================
    // Icon Builder（既存）
    //================================================================
    internal static class PortIconBuilder
    {
        public static string? ResolveIconPath(string? iconDir, string fileName)
        {
            string? dllDir = null;
            try { dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"GetExecutingAssembly.Location failed: {ex.Message}");
            }

            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            IEnumerable<string> Candidates()
            {
                if (!string.IsNullOrWhiteSpace(iconDir))
                {
                    yield return Path.Combine(iconDir, fileName);
                    yield return Path.Combine(iconDir, "icons", fileName);
                }
                if (!string.IsNullOrWhiteSpace(dllDir))
                {
                    yield return Path.Combine(dllDir, fileName);
                    yield return Path.Combine(dllDir, "icons", fileName);
                }
                foreach (var ver in new[] { "2025", "2024", "2023" })
                {
                    yield return Path.Combine(common, "Autodesk", "Revit", "Addins", ver, "RevitMCPAddin", "icons", fileName);
                    yield return Path.Combine(roaming, "Autodesk", "Revit", "Addins", ver, "RevitMCPAddin", "icons", fileName);
                }
            }
            try { return Candidates().FirstOrDefault(File.Exists); }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"ResolveIconPath Candidates evaluation failed: {ex.Message}");
                return null;
            }
        }

        public static Media.ImageSource? TryLoadBitmapAsImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                var bi = new Imaging.BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.CacheOption = Imaging.BitmapCacheOption.OnLoad;
                bi.CreateOptions = Imaging.BitmapCreateOptions.IgnoreImageCache;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"TryLoadBitmapAsImage primary load failed for '{path}': {ex.Message}");
                try
                {
                    using var fs = File.OpenRead(path);
                    var frame = Imaging.BitmapFrame.Create(fs, Imaging.BitmapCreateOptions.None, Imaging.BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    return frame;
                }
                catch (Exception ex2)
                {
                    RevitMCPAddin.Core.RevitLogger.Warn($"TryLoadBitmapAsImage fallback failed for '{path}': {ex2.Message}");
                    return null;
                }
            }
        }

        // Port 数字バッジ
        public static Media.ImageSource BuildPortBadge(int port, int sizePx, string? basePngPath = null)
        {
            var baseImg = TryLoadBitmapAsImage(basePngPath);

            double padding = sizePx <= 16 ? 2.0 : Math.Max(3.0, sizePx * 0.10);
            string text = port.ToString(CultureInfo.InvariantCulture);

            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (baseImg != null)
                {
                    dc.DrawImage(baseImg, new System.Windows.Rect(0, 0, sizePx, sizePx));
                }
                else
                {
                    var plate = new Media.RadialGradientBrush(
                        Media.Color.FromRgb(34, 34, 34),
                        Media.Color.FromRgb(60, 60, 60))
                    { Center = new System.Windows.Point(0.35, 0.35), RadiusX = 0.9, RadiusY = 0.9 };
                    var borderPen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(0, 0, 0)), sizePx * 0.06);
                    borderPen.Freeze();
                    dc.DrawEllipse(plate, borderPen,
                        new System.Windows.Point(sizePx / 2.0, sizePx / 2.0),
                        sizePx * 0.48, sizePx * 0.48);
                }

                var typeface = new Media.Typeface(new Media.FontFamily("Segoe UI"),
                    System.Windows.FontStyles.Normal, System.Windows.FontWeights.Bold, System.Windows.FontStretches.Normal);

                double maxW = sizePx - padding * 2;
                double maxH = sizePx - padding * 2;
                double fontSize = sizePx * (sizePx <= 16 ? 0.70 : 0.62);

                double MeasureWidth(double fs)
                {
                    var ft = new System.Windows.Media.FormattedText(
                        text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, typeface, fs, Media.Brushes.White, 1.0);
                    return Math.Max(ft.Width, ft.Height * 0.9);
                }
                while (fontSize > 2 && MeasureWidth(fontSize) > Math.Min(maxW, maxH)) fontSize -= 0.5;

                var mainBrush = Media.Brushes.White;
                var strokeBrush = new Media.SolidColorBrush(Media.Color.FromArgb(230, 0, 0, 0));
                strokeBrush.Freeze();

                var ftMain = new System.Windows.Media.FormattedText(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, typeface, fontSize, mainBrush, 1.0);
                double x = (sizePx - ftMain.Width) / 2.0;
                double y = (sizePx - ftMain.Height) / 2.0;

                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var ftStroke = new System.Windows.Media.FormattedText(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, typeface, fontSize, strokeBrush, 1.0);
                        dc.DrawText(ftStroke, new System.Windows.Point(x + dx, y + dy));
                    }

                dc.DrawText(ftMain, new System.Windows.Point(x, y));
            }

            var rtb = new Imaging.RenderTargetBitmap(sizePx, sizePx, 96, 96, Media.PixelFormats.Pbgra32);
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(rtb, System.Windows.Media.BitmapScalingMode.HighQuality);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        // Text badge (e.g., "Py")
        public static Media.ImageSource BuildTextBadge(string text, int sizePx)
        {
            if (string.IsNullOrWhiteSpace(text)) text = "?";

            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                var typeface = new Media.Typeface(new Media.FontFamily("Segoe UI"),
                    System.Windows.FontStyles.Normal, System.Windows.FontWeights.Bold, System.Windows.FontStretches.Normal);

                double padding = sizePx * 0.18;
                double maxW = sizePx - padding * 2;
                double maxH = sizePx - padding * 2;
                double fontSize = sizePx * 0.60;

                double MeasureWidth(double fs)
                {
                    var ft = new System.Windows.Media.FormattedText(
                        text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, typeface, fs, Media.Brushes.Black, 1.0);
                    return Math.Max(ft.Width, ft.Height * 0.9);
                }
                while (fontSize > 2 && MeasureWidth(fontSize) > Math.Min(maxW, maxH)) fontSize -= 0.5;

                var brush = new Media.SolidColorBrush(Media.Color.FromRgb(30, 30, 30));
                brush.Freeze();

                var ftMain = new System.Windows.Media.FormattedText(text, CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, typeface, fontSize, brush, 1.0);
                double x = (sizePx - ftMain.Width) / 2.0;
                double y = (sizePx - ftMain.Height) / 2.0;
                dc.DrawText(ftMain, new System.Windows.Point(x, y));
            }

            return ToBitmap(dv, sizePx);
        }

        // 共通：三角（Start）／四角（Stop）
        public static Media.ImageSource BuildTriangleIcon(int sizePx, Media.Color fill, Media.Color stroke)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                double pad = sizePx * 0.25;
                var p1 = new System.Windows.Point(pad, pad * 0.8);
                var p2 = new System.Windows.Point(sizePx - pad * 0.8, sizePx / 2.0);
                var p3 = new System.Windows.Point(pad, sizePx - pad * 0.8);

                var geo = new Media.StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(p1, true, true);
                    ctx.LineTo(p2, true, false);
                    ctx.LineTo(p3, true, false);
                }
                geo.Freeze();

                var fillBrush = new Media.SolidColorBrush(fill);
                var penBrush = new Media.SolidColorBrush(stroke);
                var pen = new Media.Pen(penBrush, Math.Max(1, sizePx * 0.05));
                fillBrush.Freeze(); penBrush.Freeze(); pen.Freeze();

                dc.DrawGeometry(fillBrush, pen, geo);
            }
            return ToBitmap(dv, sizePx);
        }

        public static Media.ImageSource BuildSquareIcon(int sizePx, Media.Color fill, Media.Color stroke)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                double pad = sizePx * 0.26;
                var rect = new System.Windows.Rect(pad, pad, sizePx - pad * 2, sizePx - pad * 2);

                var fillBrush = new Media.SolidColorBrush(fill);
                var penBrush = new Media.SolidColorBrush(stroke);
                var pen = new Media.Pen(penBrush, Math.Max(1, sizePx * 0.05));
                fillBrush.Freeze(); penBrush.Freeze(); pen.Freeze();

                dc.DrawRoundedRectangle(fillBrush, pen, rect, sizePx * 0.08, sizePx * 0.08);
            }
            return ToBitmap(dv, sizePx);
        }

        // 新規：フォルダアイコン（Developer パネル用）
        public static Media.ImageSource BuildFolderIcon(int sizePx, bool isLogs = false)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                double pad = sizePx * 0.16;
                var topH = sizePx * 0.26;           // タブ部分の高さ
                var bodyTop = pad + topH * 0.6;     // 本体開始位置

                // タブ
                var tabRect = new System.Windows.Rect(pad, pad, sizePx * 0.50, topH);
                var tabFill = new Media.SolidColorBrush(isLogs ? Media.Color.FromRgb(255, 196, 120) : Media.Color.FromRgb(255, 230, 140));
                var tabStroke = new Media.SolidColorBrush(Media.Color.FromRgb(170, 130, 70));
                var tabPen = new Media.Pen(tabStroke, Math.Max(1, sizePx * 0.04));
                tabFill.Freeze(); tabStroke.Freeze(); tabPen.Freeze();
                dc.DrawRoundedRectangle(tabFill, tabPen, tabRect, sizePx * 0.06, sizePx * 0.06);

                // 本体
                var bodyRect = new System.Windows.Rect(pad, bodyTop, sizePx - pad * 2, sizePx - bodyTop - pad);
                var bodyFill = new Media.LinearGradientBrush(
                    isLogs ? Media.Color.FromRgb(255, 180, 90) : Media.Color.FromRgb(255, 210, 110),
                    isLogs ? Media.Color.FromRgb(240, 150, 60) : Media.Color.FromRgb(240, 185, 90),
                    90);
                var bodyStroke = new Media.SolidColorBrush(Media.Color.FromRgb(170, 130, 70));
                var bodyPen = new Media.Pen(bodyStroke, Math.Max(1, sizePx * 0.04));
                bodyStroke.Freeze(); bodyPen.Freeze();
                dc.DrawRoundedRectangle(bodyFill, bodyPen, bodyRect, sizePx * 0.08, sizePx * 0.08);

                // ログ用の「doc」っぽいマーク
                if (isLogs)
                {
                    var paper = new System.Windows.Rect(bodyRect.X + bodyRect.Width * 0.60, bodyRect.Y + bodyRect.Height * 0.18, bodyRect.Width * 0.28, bodyRect.Height * 0.60);
                    var paperFill = new Media.SolidColorBrush(Media.Color.FromRgb(255, 255, 255));
                    var paperPen = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(180, 180, 180)), Math.Max(1, sizePx * 0.03));
                    (paperFill as Media.SolidColorBrush).Freeze();
                    (paperPen.Brush as Media.SolidColorBrush)!.Freeze(); paperPen.Freeze();
                    dc.DrawRoundedRectangle(paperFill, paperPen, paper, sizePx * 0.02, sizePx * 0.02);
                }
            }
            return ToBitmap(dv, sizePx);
        }

        // HAL icon (for Codex GUI button)
        public static Media.ImageSource BuildHalIcon(int sizePx)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Match CodexGUI taskbar icon (HAL-like)
                var center = new System.Windows.Point(sizePx / 2.0, sizePx / 2.0);
                var outerRadius = sizePx * 0.48;
                var ringRadius = sizePx * 0.36;
                var redRadius = sizePx * 0.26 * 0.5; // small red lens

                // Black disk
                var diskFill = new Media.SolidColorBrush(Media.Color.FromRgb(0x11, 0x11, 0x11));
                diskFill.Freeze();
                dc.DrawEllipse(diskFill, null, center, outerRadius, outerRadius);

                // Silver ring
                var ringBrush = new Media.SolidColorBrush(Media.Color.FromRgb(0xC0, 0xC0, 0xC0));
                ringBrush.Freeze();
                var ringPen = new Media.Pen(ringBrush, Math.Max(1, sizePx * 0.06));
                ringPen.Freeze();
                dc.DrawEllipse(null, ringPen, center, ringRadius, ringRadius);

                // Red lens
                var redBrush = new Media.RadialGradientBrush
                {
                    Center = new System.Windows.Point(0.5, 0.5),
                    GradientOrigin = new System.Windows.Point(0.45, 0.35),
                    RadiusX = 0.6,
                    RadiusY = 0.6
                };
                redBrush.GradientStops.Add(new Media.GradientStop(Media.Color.FromRgb(0xFF, 0x44, 0x44), 0.0));
                redBrush.GradientStops.Add(new Media.GradientStop(Media.Color.FromRgb(0xCC, 0x00, 0x00), 0.4));
                redBrush.GradientStops.Add(new Media.GradientStop(Media.Color.FromRgb(0x00, 0x00, 0x00), 1.0));
                redBrush.Freeze();
                dc.DrawEllipse(redBrush, null, center, redRadius, redRadius);

                // Highlight (small)
                var highlight = new Media.RadialGradientBrush
                {
                    Center = new System.Windows.Point(0.35, 0.3),
                    GradientOrigin = new System.Windows.Point(0.32, 0.25),
                    RadiusX = 0.4,
                    RadiusY = 0.4,
                    Opacity = 0.7
                };
                highlight.GradientStops.Add(new Media.GradientStop(Media.Color.FromArgb(180, 255, 255, 255), 0.0));
                highlight.GradientStops.Add(new Media.GradientStop(Media.Color.FromArgb(0, 255, 255, 255), 1.0));
                highlight.Freeze();
                dc.DrawEllipse(
                    highlight,
                    null,
                    new System.Windows.Point(center.X - sizePx * 0.06, center.Y - sizePx * 0.10),
                    redRadius * 0.55,
                    redRadius * 0.55);
            }
            return ToBitmap(dv, sizePx);
        }

        // 指差し（白い手袋・上から見た手）アイコン（Pick Info 用）
        public static Media.ImageSource BuildFingerIcon(int sizePx)
        {
            var dv = new Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawPlate(dc, sizePx);

                var glove = new Media.SolidColorBrush(Media.Color.FromRgb(245, 245, 245));
                var shadow = new Media.SolidColorBrush(Media.Color.FromRgb(220, 220, 220));
                var outline = new Media.Pen(new Media.SolidColorBrush(Media.Color.FromRgb(80, 80, 80)), Math.Max(1, sizePx * 0.04));
                glove.Freeze();
                shadow.Freeze();
                (outline.Brush as Media.SolidColorBrush)!.Freeze();
                outline.Freeze();

                // Palm (bottom)
                double pad = sizePx * 0.18;
                var palm = new System.Windows.Rect(pad, sizePx * 0.56, sizePx - pad * 2, sizePx * 0.30);
                dc.DrawRoundedRectangle(shadow, null, new System.Windows.Rect(palm.X + 1, palm.Y + 1, palm.Width, palm.Height), sizePx * 0.10, sizePx * 0.10);
                dc.DrawRoundedRectangle(glove, outline, palm, sizePx * 0.10, sizePx * 0.10);

                // Index finger (upwards)
                var finger = new System.Windows.Rect(sizePx * 0.43, sizePx * 0.16, sizePx * 0.22, sizePx * 0.46);
                dc.DrawRoundedRectangle(shadow, null, new System.Windows.Rect(finger.X + 1, finger.Y + 1, finger.Width, finger.Height), sizePx * 0.12, sizePx * 0.12);
                dc.DrawRoundedRectangle(glove, outline, finger, sizePx * 0.12, sizePx * 0.12);

                // Thumb (right)
                var thumb = new System.Windows.Rect(sizePx * 0.62, sizePx * 0.50, sizePx * 0.18, sizePx * 0.18);
                dc.DrawRoundedRectangle(shadow, null, new System.Windows.Rect(thumb.X + 1, thumb.Y + 1, thumb.Width, thumb.Height), sizePx * 0.08, sizePx * 0.08);
                dc.DrawRoundedRectangle(glove, outline, thumb, sizePx * 0.08, sizePx * 0.08);

                // Fingertip circle
                var tipCenter = new System.Windows.Point(finger.X + finger.Width / 2.0, finger.Y + sizePx * 0.05);
                dc.DrawEllipse(glove, outline, tipCenter, sizePx * 0.11, sizePx * 0.11);
            }
            return ToBitmap(dv, sizePx);
        }

        private static void DrawPlate(System.Windows.Media.DrawingContext dc, int sizePx)
        {
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
    }
}

