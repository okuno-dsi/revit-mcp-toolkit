#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Collections.Specialized;
// Revit UI はエイリアスで使う（TextBox/ComboBox の曖昧さを回避）
using RUI = Autodesk.Revit.UI;
// WinForms はフォルダダイアログ用だけ
using WF = System.Windows.Forms;

namespace SmartOpen
{
    public partial class SmartOpenWindow : Window
    {
        private readonly RUI.UIApplication _uiapp;
        private readonly ObservableCollection<RvtInfo> _items = new ObservableCollection<RvtInfo>();
        private readonly Dictionary<string, ImageSource?> _previewCache = new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

        // ====== FindName 経由の安全アクセサ（WPF型は完全修飾）======
        private System.Windows.Controls.TextBox? _txtFolderEl;
        private System.Windows.Controls.TextBox TxtFolderEl
            => _txtFolderEl ??= (System.Windows.Controls.TextBox)FindRequired("TxtFolder");

        private System.Windows.Controls.ComboBox? _cmbFilterEl;
        private System.Windows.Controls.ComboBox CmbFilterEl
            => _cmbFilterEl ??= (System.Windows.Controls.ComboBox)FindRequired("CmbFilter");

        private System.Windows.Controls.DataGrid? _filesGridEl;
        private System.Windows.Controls.DataGrid FilesGridEl
            => _filesGridEl ??= (System.Windows.Controls.DataGrid)FindRequired("FilesGrid");

        private System.Windows.Controls.Primitives.ToggleButton? _btnBrowseDropEl;
        private System.Windows.Controls.Primitives.ToggleButton BtnBrowseDropEl
            => _btnBrowseDropEl ??= (System.Windows.Controls.Primitives.ToggleButton)FindRequired("BtnBrowseDrop");

        private System.Windows.Controls.Primitives.Popup? _browseModePopupEl;
        private System.Windows.Controls.Primitives.Popup BrowseModePopupEl
            => _browseModePopupEl ??= (System.Windows.Controls.Primitives.Popup)FindRequired("BrowseModePopup");

        private System.Windows.Controls.Image? _previewImgEl;
        private System.Windows.Controls.Image PreviewImgEl
            => _previewImgEl ??= (System.Windows.Controls.Image)FindRequired("PreviewImage");

        private System.Windows.Controls.TextBlock? _txtSummaryEl;
        private System.Windows.Controls.TextBlock TxtSummaryEl
            => _txtSummaryEl ??= (System.Windows.Controls.TextBlock)FindRequired("TxtSummary");

        private System.Windows.Controls.CheckBox? _chkAuditEl;
        private System.Windows.Controls.CheckBox ChkAuditEl
            => _chkAuditEl ??= (System.Windows.Controls.CheckBox)FindRequired("ChkAudit");

        private System.Windows.Controls.CheckBox? _chkDetachEl;
        private System.Windows.Controls.CheckBox ChkDetachEl
            => _chkDetachEl ??= (System.Windows.Controls.CheckBox)FindRequired("ChkDetach");

        // Folder history UI
        private System.Windows.Controls.Primitives.ToggleButton? _btnFolderHistDropEl;
        private System.Windows.Controls.Primitives.ToggleButton BtnFolderHistDropEl
            => _btnFolderHistDropEl ??= (System.Windows.Controls.Primitives.ToggleButton)FindRequired("BtnFolderHistDrop");

        private System.Windows.Controls.Primitives.Popup? _folderHistoryPopupEl;
        private System.Windows.Controls.Primitives.Popup FolderHistoryPopupEl
            => _folderHistoryPopupEl ??= (System.Windows.Controls.Primitives.Popup)FindRequired("FolderHistoryPopup");

        private System.Windows.Controls.StackPanel? _folderHistoryPanelEl;
        private System.Windows.Controls.StackPanel FolderHistoryPanelEl
            => _folderHistoryPanelEl ??= (System.Windows.Controls.StackPanel)FindRequired("FolderHistoryPanel");

        private System.Windows.Controls.TextBox? _txtHistorySizeEl;
        private System.Windows.Controls.TextBox TxtHistorySizeEl
            => _txtHistorySizeEl ??= (System.Windows.Controls.TextBox)FindRequired("TxtHistorySize");

        private FrameworkElement FindRequired(string name)
        {
            var el = (FrameworkElement)FindName(name)!;
            if (el == null)
                throw new InvalidOperationException($"XAML element '{name}' was not found. Ensure x:Name matches and Build Action=Page.");
            return el;
        }
        // =====================================================================

        public SmartOpenWindow(RUI.UIApplication uiapp)
        {
            _uiapp = uiapp;
            InitializeComponent();

            // DataGrid にバインド
            FilesGridEl.ItemsSource = _items;

            // 念のため SelectionChanged を再バインド
            FilesGridEl.SelectionChanged -= FilesGrid_SelectionChanged;
            FilesGridEl.SelectionChanged += FilesGrid_SelectionChanged;

            // 履歴数の保存
            TxtHistorySizeEl.LostFocus += (s, e) => TxtHistorySize_Changed();
        }

        private enum BrowseMode { Explorer = 0, Rich = 1 }

        private BrowseMode LastBrowseMode
        {
            get
            {
                try
                {
                    object v = Properties.Settings.Default["BrowseMode"];
                    if (v is int i) return (BrowseMode)i;
                }
                catch { }
                return BrowseMode.Rich;
            }
            set
            {
                Properties.Settings.Default["BrowseMode"] = (int)value;
                Properties.Settings.Default.Save();
            }
        }

        // ==== Event handlers ====

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 設定から最終フォルダを復元（なければマイドキュメント）
            string last = Properties.Settings.Default.LastFolder ?? string.Empty;
            if (string.IsNullOrWhiteSpace(last))
                last = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            TxtFolderEl.Text = last;

            // 履歴数UIの初期化と履歴表示
            TxtHistorySizeEl.Text = GetHistorySize().ToString();
            RefreshFolderHistoryUI();

            Reload();
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => Reload();

        private void Reload()
        {
            try
            {
                _items.Clear();

                string folder = (TxtFolderEl.Text ?? "").Trim();
                if (!Directory.Exists(folder)) return;
                // 履歴に保存
                SetLastFolder(folder);

                string filter = (CmbFilterEl.Text ?? "*").Trim();
                var files = Directory.EnumerateFiles(folder, filter)
                    .Where(p => p.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => File.GetLastWriteTime(p));

                foreach (string path in files)
                {
                    var info = RvtInfoUtil.TryGet(path); // 戻り値で RvtInfo? を返す想定
                    if (info != null) _items.Add(info);
                }

                if (_items.Count > 0) FilesGridEl.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Reload error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FilesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var info = FilesGridEl.SelectedItem as RvtInfo;
            if (info == null)
            {
                PreviewImgEl.Source = null;
                TxtSummaryEl.Text = string.Empty;
                return;
            }

            if (File.Exists(info.Path))
            {
                PreviewImgEl.Source = GetPreviewImageForPath(info.Path, 256);
            }
            else
            {
                PreviewImgEl.Source = null;
            }

            string sizeMb = (info.SizeBytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            string modified = info.LastWrite.ToString("yyyy-MM-dd HH:mm");
            string buildText = info.RevitBuild ?? "";

            TxtSummaryEl.Text =
                "Name: " + info.Name + Environment.NewLine +
                "Revit Build: " + buildText + Environment.NewLine +
                "Workshared: " + info.IsWorkshared + Environment.NewLine +
                "Central: " + info.IsCentral + Environment.NewLine +
                "Links: " + info.LinkCount + Environment.NewLine +
                "Size / Modified: " + sizeMb + " / " + modified;
        }

        private ImageSource? GetPreviewImageForPath(string path, int size)
        {
            try
            {
                if (_previewCache.TryGetValue(path, out var cached))
                    return cached;

                ImageSource? src = null;

                // 1) Try Revit API preview (sameソースを使うため)
                src = GetRevitPreview(path, size);

                // 2) 取れなければ OS サムネイル / アイコンにフォールバック
                if (src == null)
                    src = SmartOpen.Utils.ThumbnailProvider.GetThumbnail(path, size);

                _previewCache[path] = src;
                return src;
            }
            catch
            {
                return null;
            }
        }

                private ImageSource? GetRevitPreview(string path, int size)
        {
            try
            {
                var app = _uiapp.Application;

                // すでに開いているドキュメントだけを対象にする（新しく開かない＝勝手にアップグレードしない）
                var doc = app.Documents
                    .Cast<Autodesk.Revit.DB.Document>()
                    .FirstOrDefault(d => string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase));
                if (doc == null) return null;

                var sz = new System.Drawing.Size(size, size);

                // Revit の Document に GetPreviewImage(Size) があればそれを呼ぶ（バージョン差吸収のためリフレクション）
                var mi = doc.GetType().GetMethod("GetPreviewImage", new[] { typeof(System.Drawing.Size) });
                if (mi == null) return null;

                using var img = mi.Invoke(doc, new object[] { sz }) as System.Drawing.Image;
                if (img == null) return null;

                using var bmp = new System.Drawing.Bitmap(img);
                return SmartOpen.Utils.ThumbnailProvider.ToImageSource(bmp);
            }
            catch
            {
                return null;
            }
        }
        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var info = FilesGridEl.SelectedItem as RvtInfo;
            if (info == null) return;

            try
            {
                bool audit = (ChkAuditEl.IsChecked == true);
                bool detach = (ChkDetachEl.IsChecked == true);

                OpenService.Open(_uiapp, info.Path, audit, detach); // void 戻り値
                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Open error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RevealInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var info = FilesGridEl.SelectedItem as RvtInfo;
            if (info == null) return;

            if (File.Exists(info.Path))
            {
                string args = "/select,\"" + info.Path + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            }
        }

        private void BtnBrowseMain_Click(object sender, RoutedEventArgs e)
        {
            // Execute current selection
            if (LastBrowseMode == BrowseMode.Explorer) BrowseWithExplorer();
            else BrowseWithRich();
        }

        private void BrowseWithExplorer_Click(object sender, RoutedEventArgs e)
        {
            LastBrowseMode = BrowseMode.Explorer;
            // Selection only; close popup
            BtnBrowseDropEl.IsChecked = false;
        }

        private void BrowseWithRich_Click(object sender, RoutedEventArgs e)
        {
            LastBrowseMode = BrowseMode.Rich;
            // Selection only; close popup
            BtnBrowseDropEl.IsChecked = false;
        }

        private void BrowseWithExplorer()
        {
            using (var dlg = new WF.FolderBrowserDialog())
            {
                if (Directory.Exists(TxtFolderEl.Text))
                    dlg.SelectedPath = TxtFolderEl.Text;
                dlg.Description = "フォルダを選択してください";

                if (dlg.ShowDialog(new Wpf32Window(this)) == WF.DialogResult.OK)
                {
                    TxtFolderEl.Text = dlg.SelectedPath;
                    SetLastFolder(dlg.SelectedPath);
                    Reload();
                }
            }
        }

        private void BrowseWithRich()
        {
            // Use Vista-style CommonOpenFileDialog as a "rich" browser
            try
            {
                using (var dlg = new CommonOpenFileDialog())
                {
                    dlg.IsFolderPicker = false;
                    dlg.EnsurePathExists = true;
                    dlg.EnsureReadOnly = false;
                    dlg.Multiselect = false;
                    dlg.Title = "フォルダーを選択してください (リッチブラウザ)";
                    if (Directory.Exists(TxtFolderEl.Text))
                        dlg.InitialDirectory = TxtFolderEl.Text;
                    dlg.Filters.Add(new CommonFileDialogFilter("Revit Files (*.rvt;*.rte)", "*.rvt;*.rte"));
                    dlg.Filters.Add(new CommonFileDialogFilter("All Files (*.*)", "*.*"));

                    var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    if (dlg.ShowDialog(owner) == CommonFileDialogResult.Ok)
                    {
                        var filePath = dlg.FileName;
                        var folder = System.IO.Path.GetDirectoryName(filePath) ?? string.Empty;
                        if (!string.IsNullOrEmpty(folder))
                        {
                            TxtFolderEl.Text = folder;
                            SetLastFolder(folder);
                        }
                        Reload();
                    }
                }
            }
            catch
            {
                // Fallback to Explorer-style if dialog fails for some reason
                BrowseWithExplorer();
            }
        }

        // ===== Folder history helpers =====
        private int GetHistorySize()
        {
            int size = Properties.Settings.Default.HistorySize;
            if (size <= 0) size = 10;
            if (size > 100) size = 100;
            return size;
        }

        private void SetLastFolder(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) return;
                Properties.Settings.Default.LastFolder = folder;

                var hist = Properties.Settings.Default.FolderHistory;
                if (hist == null) { hist = new StringCollection(); Properties.Settings.Default.FolderHistory = hist; }

                var list = hist.Cast<string>().ToList();
                list.RemoveAll(s => string.Equals(s, folder, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, folder);

                int limit = GetHistorySize();
                if (list.Count > limit) list = list.Take(limit).ToList();

                var sc = new StringCollection();
                sc.AddRange(list.ToArray());
                Properties.Settings.Default.FolderHistory = sc;

                Properties.Settings.Default.Save();
                RefreshFolderHistoryUI();
            }
            catch { }
        }

        private void RefreshFolderHistoryUI()
        {
            try
            {
                var panel = FolderHistoryPanelEl;
                panel.Children.Clear();
                var hist = Properties.Settings.Default.FolderHistory ?? new StringCollection();
                var pinned = Properties.Settings.Default.PinnedFolders ?? new StringCollection();

                System.Func<string, System.Windows.UIElement> makeRow = (path) =>
                {
                    var row = new System.Windows.Controls.DockPanel { LastChildFill = true };

                    // Delete button
                    var btnDel = new System.Windows.Controls.Button
                    {
                        Content = "✕",
                        Tag = path,
                        Width = 24,
                        Margin = new Thickness(0, 0, 4, 0)
                    };
                    btnDel.Click += FolderHistoryDelete_Click;
                    System.Windows.Controls.DockPanel.SetDock(btnDel, System.Windows.Controls.Dock.Right);
                    row.Children.Add(btnDel);

                    // Pin toggle
                    bool isPinned = pinned.Cast<string>().Any(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase));
                    var btnPin = new System.Windows.Controls.Button
                    {
                        Content = isPinned ? "★" : "☆",
                        Tag = path,
                        Width = 24,
                        Margin = new Thickness(0, 0, 4, 0)
                    };
                    btnPin.Click += FolderHistoryPin_Click;
                    System.Windows.Controls.DockPanel.SetDock(btnPin, System.Windows.Controls.Dock.Right);
                    row.Children.Add(btnPin);

                    // Main select button
                    var btn = new System.Windows.Controls.Button
                    {
                        Content = path,
                        Tag = path,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(8, 4, 8, 4),
                        BorderThickness = new Thickness(0)
                    };
                    btn.Click += FolderHistoryItem_Click;
                    row.Children.Add(btn);

                    return row;
                };

                // Pinned first
                var pinnedList = pinned.Cast<string>().ToList();
                if (pinnedList.Count > 0)
                {
                    panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "ピン留め", Margin = new Thickness(8, 6, 8, 2), FontWeight = FontWeights.Bold });
                    foreach (var s in pinnedList)
                        panel.Children.Add(makeRow(s));
                    panel.Children.Add(new System.Windows.Controls.Separator { Margin = new Thickness(0, 6, 0, 6) });
                }

                // Recent excluding pinned
                var recent = hist.Cast<string>().Where(s => !pinnedList.Any(p => string.Equals(p, s, StringComparison.OrdinalIgnoreCase))).ToList();
                if (recent.Count == 0 && pinnedList.Count == 0)
                {
                    panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "履歴はありません", Margin = new Thickness(8) });
                }
                else
                {
                    if (recent.Count > 0)
                    {
                        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "最近", Margin = new Thickness(8, 0, 8, 2), FontWeight = FontWeights.Bold });
                        foreach (var s in recent)
                            panel.Children.Add(makeRow(s));
                    }
                    // Clear button
                    var btnClear = new System.Windows.Controls.Button { Content = "履歴をクリア", Margin = new Thickness(8, 8, 8, 8) };
                    btnClear.Click += FolderHistoryClear_Click;
                    panel.Children.Add(btnClear);
                }
            }
            catch { }
        }

        private void FolderHistoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.Tag is string folder)
            {
                if (Directory.Exists(folder))
                {
                    TxtFolderEl.Text = folder;
                    SetLastFolder(folder);
                    Reload();
                }
                BtnFolderHistDropEl.IsChecked = false;
            }
        }

        private void FolderHistoryDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.Tag is string path)
            {
                var hist = Properties.Settings.Default.FolderHistory ?? new StringCollection();
                for (int i = hist.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(hist[i], path, StringComparison.OrdinalIgnoreCase)) hist.RemoveAt(i);
                }
                Properties.Settings.Default.FolderHistory = hist;

                var pinned = Properties.Settings.Default.PinnedFolders ?? new StringCollection();
                for (int i = pinned.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(pinned[i], path, StringComparison.OrdinalIgnoreCase)) pinned.RemoveAt(i);
                }
                Properties.Settings.Default.PinnedFolders = pinned;
                Properties.Settings.Default.Save();
                RefreshFolderHistoryUI();
            }
        }

        private void FolderHistoryPin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.Tag is string path)
            {
                var pinned = Properties.Settings.Default.PinnedFolders ?? new StringCollection();
                bool exists = pinned.Cast<string>().Any(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    for (int i = pinned.Count - 1; i >= 0; i--)
                        if (string.Equals(pinned[i], path, StringComparison.OrdinalIgnoreCase)) pinned.RemoveAt(i);
                }
                else
                {
                    // Add to top
                    var list = pinned.Cast<string>().ToList();
                    list.RemoveAll(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase));
                    list.Insert(0, path);
                    pinned = new StringCollection();
                    pinned.AddRange(list.ToArray());
                }
                Properties.Settings.Default.PinnedFolders = pinned;
                Properties.Settings.Default.Save();
                RefreshFolderHistoryUI();
            }
        }

        private void FolderHistoryClear_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.FolderHistory = new StringCollection();
            Properties.Settings.Default.Save();
            RefreshFolderHistoryUI();
        }

        private void TxtHistorySize_Changed()
        {
            if (int.TryParse((TxtHistorySizeEl.Text ?? string.Empty).Trim(), out int v))
            {
                if (v <= 0) v = 1;
                if (v > 100) v = 100;
                Properties.Settings.Default.HistorySize = v;
                Properties.Settings.Default.Save();

                // Trim existing history to new size
                var hist = Properties.Settings.Default.FolderHistory;
                if (hist != null && hist.Count > v)
                {
                    var trimmed = hist.Cast<string>().Take(v).ToArray();
                    var sc = new StringCollection();
                    sc.AddRange(trimmed);
                    Properties.Settings.Default.FolderHistory = sc;
                    Properties.Settings.Default.Save();
                }
                RefreshFolderHistoryUI();
            }
        }
    }

    // ====== 最小 ThumbnailProvider（依存なし）======
    internal sealed class Wpf32Window : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Wpf32Window(Window w)
        {
            Handle = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        }
    }

    internal static class ThumbnailProvider
    {
        public static ImageSource? GetThumbnail(string path, int size)
        {
            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon != null)
                    {
                        using var bmp = icon.ToBitmap();
                        return ToImageSource(bmp);
                    }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static ImageSource ToImageSource(System.Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
    }
}

