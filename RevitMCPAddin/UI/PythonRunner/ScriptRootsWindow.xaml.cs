using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace RevitMCPAddin.UI.PythonRunner
{
    public partial class ScriptRootsWindow : Window
    {
        private readonly string _defaultRoot;
        private readonly List<string> _customRoots;

        public IReadOnlyList<string> CustomRoots => _customRoots;

        public ScriptRootsWindow(string defaultRoot, IEnumerable<string> customRoots)
        {
            _defaultRoot = defaultRoot;
            _customRoots = customRoots?.ToList() ?? new List<string>();

            InitializeComponent();

            BtnAdd.Click += (_, __) => AddRoot();
            BtnRemove.Click += (_, __) => RemoveRoot();
            BtnOk.Click += (_, __) => { DialogResult = true; Close(); };
            BtnCancel.Click += (_, __) => { DialogResult = false; Close(); };

            RefreshList();
        }

        private void RefreshList()
        {
            ListRoots.Items.Clear();
            ListRoots.Items.Add($"[Default] {_defaultRoot}");
            foreach (var r in _customRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ListRoots.Items.Add(r);
            }
        }

        private void AddRoot()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "追加するスクリプトフォルダを選択してください。",
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var path = dlg.SelectedPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (_customRoots.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                return;
            _customRoots.Add(path);
            RefreshList();
        }

        private void RemoveRoot()
        {
            if (ListRoots.SelectedItem == null) return;
            var text = ListRoots.SelectedItem.ToString() ?? "";
            if (text.StartsWith("[Default]", StringComparison.OrdinalIgnoreCase)) return;
            _customRoots.RemoveAll(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase));
            RefreshList();
        }
    }
}
