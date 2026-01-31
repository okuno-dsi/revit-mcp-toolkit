using System.Windows;

namespace RevitMCPAddin.UI.PythonRunner
{
    public partial class ScriptMetadataWindow : Window
    {
        public string Feature => TxtFeature.Text ?? string.Empty;
        public string Keywords => TxtKeywords.Text ?? string.Empty;

        public ScriptMetadataWindow(string feature, string keywords)
        {
            InitializeComponent();
            TxtFeature.Text = feature ?? string.Empty;
            TxtKeywords.Text = keywords ?? string.Empty;

            BtnOk.Click += (_, __) => { DialogResult = true; Close(); };
            BtnCancel.Click += (_, __) => { DialogResult = false; Close(); };
        }
    }
}
