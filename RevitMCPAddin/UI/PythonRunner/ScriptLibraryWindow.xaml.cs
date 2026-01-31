using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;

namespace RevitMCPAddin.UI.PythonRunner
{
    public partial class ScriptLibraryWindow : Window
    {
        private readonly ObservableCollection<ScriptLibraryItem> _items = new ObservableCollection<ScriptLibraryItem>();
        private readonly ICollectionView _view;
        private readonly Action<string> _openAction;
        private readonly string _defaultRoot;
        private string _filter = "";

        public ScriptLibraryWindow(string defaultRoot, Action<string> openAction)
        {
            _defaultRoot = defaultRoot;
            _openAction = openAction;

            InitializeComponent();

            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = FilterItem;

            BtnApplyFilter.Click += (_, __) => ApplyFilter();
            BtnClearFilter.Click += (_, __) => ClearFilter();
            BtnSortKeyword.Click += (_, __) => SortByKeyword();
            BtnRefresh.Click += (_, __) => RefreshList();
            BtnAddFolder.Click += (_, __) => AddFolder();
            BtnAddFile.Click += (_, __) => AddFile();
            BtnManageRoots.Click += (_, __) => ManageRoots();
            BtnOpen.Click += (_, __) => OpenSelected();
            BtnEdit.Click += (_, __) => EditSelected();
            BtnDelete.Click += (_, __) => DeleteSelected();
            BtnReveal.Click += (_, __) => RevealSelected();

            GridItems.ItemsSource = _items;
            RefreshList();
        }

        private bool FilterItem(object obj)
        {
            if (string.IsNullOrWhiteSpace(_filter)) return true;
            var item = obj as ScriptLibraryItem;
            if (item == null) return true;
            var needle = _filter.Trim();
            if (needle.Length == 0) return true;
            return item.Keywords.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                   || item.Feature.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                   || item.FileName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyFilter()
        {
            _filter = TxtFilter.Text ?? "";
            _view.Refresh();
        }

        private void ClearFilter()
        {
            TxtFilter.Text = "";
            _filter = "";
            _view.Refresh();
        }

        private void SortByKeyword()
        {
            _view.SortDescriptions.Clear();
            _view.SortDescriptions.Add(new SortDescription(nameof(ScriptLibraryItem.Keywords), ListSortDirection.Ascending));
            _view.SortDescriptions.Add(new SortDescription(nameof(ScriptLibraryItem.Feature), ListSortDirection.Ascending));
            _view.Refresh();
        }

        private void RefreshList()
        {
            try
            {
                _items.Clear();
                var roots = PythonRunnerScriptLibrary.BuildSearchRoots(_defaultRoot);
                var files = PythonRunnerScriptLibrary.LoadUserFiles();
                var excluded = PythonRunnerScriptLibrary.LoadExcludedFiles();
                foreach (var item in PythonRunnerScriptLibrary.ScanScripts(roots, files, excluded))
                    _items.Add(item);
                SortByKeyword();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Refresh failed: " + ex.Message, "Script Library", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private List<ScriptLibraryItem> SelectedItems()
        {
            return GridItems.SelectedItems?.Cast<ScriptLibraryItem>().Where(x => x != null).ToList()
                   ?? new List<ScriptLibraryItem>();
        }

        private void OpenSelected()
        {
            var items = SelectedItems();
            if (items.Count == 0) return;
            // Open only the first to avoid opening many editors at once.
            _openAction?.Invoke(items[0].FilePath);
            try { Close(); } catch { /* ignore */ }
        }

        private void DeleteSelected()
        {
            var items = SelectedItems();
            if (items.Count == 0) return;
            var res = MessageBox.Show(this, $"Remove from library?\n\n{items.Count} item(s)\n\n(ファイルは削除しません)", "Remove", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            try
            {
                var files = PythonRunnerScriptLibrary.LoadUserFiles();
                var excluded = PythonRunnerScriptLibrary.LoadExcludedFiles();
                bool changedFiles = false;
                bool changedExcluded = false;

                foreach (var item in items)
                {
                    if (item == null) continue;
                    // If explicitly added file, remove from file list.
                    if (string.Equals(item.Source, "file", StringComparison.OrdinalIgnoreCase))
                    {
                        int before = files.Count;
                        files.RemoveAll(x => string.Equals(x, item.FilePath, StringComparison.OrdinalIgnoreCase));
                        if (before != files.Count) changedFiles = true;
                    }
                    else
                    {
                        // Root-discovered file -> add to excluded list.
                        if (!excluded.Any(x => string.Equals(x, item.FilePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            excluded.Add(item.FilePath);
                            changedExcluded = true;
                        }
                    }

                    _items.Remove(item);
                }

                if (changedFiles) PythonRunnerScriptLibrary.SaveUserFiles(files);
                if (changedExcluded) PythonRunnerScriptLibrary.SaveExcludedFiles(excluded);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Remove failed: " + ex.Message, "Remove", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditSelected()
        {
            var items = SelectedItems();
            if (items.Count == 0) return;

            string feature = "";
            string keywords = "";
            if (items.Count == 1)
            {
                var meta = PythonRunnerScriptLibrary.ParseMetadata(items[0].FilePath);
                feature = meta.feature;
                keywords = meta.keywords;
            }
            else
            {
                var feats = items.Select(x => x.Feature ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var keys = items.Select(x => x.Keywords ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                feature = feats.Count == 1 ? feats[0] : "";
                keywords = keys.Count == 1 ? keys[0] : "";
            }

            var dlg = new ScriptMetadataWindow(feature, keywords)
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                foreach (var item in items)
                {
                    if (item == null) continue;
                    PythonRunnerScriptLibrary.UpdateMetadataInScript(item.FilePath, dlg.Feature, dlg.Keywords);
                    item.Feature = dlg.Feature ?? "";
                    item.Keywords = dlg.Keywords ?? "";
                    item.LastWriteTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                }
                _view.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Edit failed: " + ex.Message, "Edit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RevealSelected()
        {
            var items = SelectedItems();
            if (items.Count == 0) return;
            var item = items[0];
            try
            {
                var arg = "/select,\"" + item.FilePath + "\"";
                Process.Start("explorer.exe", arg);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Open folder failed: " + ex.Message, "Open Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ManageRoots()
        {
            var roots = PythonRunnerScriptLibrary.LoadUserRoots();
            var dlg = new ScriptRootsWindow(_defaultRoot, roots);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                PythonRunnerScriptLibrary.SaveUserRoots(dlg.CustomRoots);
                RefreshList();
            }
        }

        private void AddFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "ライブラリに追加するフォルダを選択してください（下位フォルダは対象外）",
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var path = dlg.SelectedPath;
            if (string.IsNullOrWhiteSpace(path)) return;

            var roots = PythonRunnerScriptLibrary.LoadUserRoots();
            if (roots.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                return;
            roots.Add(path);
            PythonRunnerScriptLibrary.SaveUserRoots(roots);
            RefreshList();
        }

        private void AddFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "ライブラリに追加する Python スクリプトを選択してください",
                Filter = "Python Script (*.py)|*.py|All Files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            var files = dlg.FileNames ?? Array.Empty<string>();
            if (files.Length == 0) return;

            var userFiles = PythonRunnerScriptLibrary.LoadUserFiles();
            var excluded = PythonRunnerScriptLibrary.LoadExcludedFiles();
            bool changed = false;
            bool changedExcluded = false;
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                if (!File.Exists(file)) continue;
                if (!userFiles.Any(x => string.Equals(x, file, StringComparison.OrdinalIgnoreCase)))
                {
                    userFiles.Add(file);
                    changed = true;
                }
                // Ensure it's not excluded.
                if (excluded.RemoveAll(x => string.Equals(x, file, StringComparison.OrdinalIgnoreCase)) > 0)
                    changedExcluded = true;
            }
            if (changed) PythonRunnerScriptLibrary.SaveUserFiles(userFiles);
            if (changedExcluded) PythonRunnerScriptLibrary.SaveExcludedFiles(excluded);
            RefreshList();
        }
    }
}
