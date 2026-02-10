using System.Windows;

namespace CodexGui;

public partial class SimpleTextInputWindow : Window
{
    public string ResultText { get; private set; }

    public SimpleTextInputWindow(string title, string label, string initialText)
        : this(title, label, initialText, readOnly: false, showCancel: true, selectAll: true)
    {
    }

    public SimpleTextInputWindow(string title, string label, string initialText, bool readOnly, bool showCancel, bool selectAll)
    {
        InitializeComponent();
        Title = title;
        LabelTextBlock.Text = label;
        InputTextBox.Text = initialText ?? string.Empty;
        InputTextBox.IsReadOnly = readOnly;
        InputTextBox.IsReadOnlyCaretVisible = readOnly;
        if (!showCancel && CancelButton != null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
        if (readOnly && OkButton != null)
        {
            OkButton.Content = "閉じる";
        }

        if (selectAll)
        {
            InputTextBox.SelectAll();
        }
        Loaded += (_, _) => InputTextBox.Focus();
        ResultText = initialText ?? string.Empty;
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        ResultText = InputTextBox.Text;
        DialogResult = true;
        Close();
    }
}
