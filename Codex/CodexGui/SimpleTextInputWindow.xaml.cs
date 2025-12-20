using System.Windows;

namespace CodexGui;

public partial class SimpleTextInputWindow : Window
{
    public string ResultText { get; private set; }

    public SimpleTextInputWindow(string title, string label, string initialText)
    {
        InitializeComponent();
        Title = title;
        LabelTextBlock.Text = label;
        InputTextBox.Text = initialText;
        InputTextBox.SelectAll();
        Loaded += (_, _) => InputTextBox.Focus();
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        ResultText = InputTextBox.Text;
        DialogResult = true;
        Close();
    }
}

