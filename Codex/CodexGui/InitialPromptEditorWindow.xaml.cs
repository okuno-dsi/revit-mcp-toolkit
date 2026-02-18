using System;
using System.Windows;

namespace CodexGui;

public partial class InitialPromptEditorWindow : Window
{
    public string UserAppendPrompt { get; private set; } = string.Empty;

    public InitialPromptEditorWindow(string basePrompt, string userAppendPrompt)
    {
        InitializeComponent();
        BasePromptTextBox.Text = (basePrompt ?? string.Empty).Trim();
        UserAppendPromptTextBox.Text = (userAppendPrompt ?? string.Empty).Trim();
        UserAppendPrompt = UserAppendPromptTextBox.Text;

        Loaded += (_, _) =>
        {
            UserAppendPromptTextBox.Focus();
            UserAppendPromptTextBox.CaretIndex = UserAppendPromptTextBox.Text?.Length ?? 0;
        };
    }

    private void ClearUserAppendButton_OnClick(object sender, RoutedEventArgs e)
    {
        UserAppendPromptTextBox.Text = string.Empty;
        UserAppendPromptTextBox.Focus();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        UserAppendPrompt = (UserAppendPromptTextBox.Text ?? string.Empty).Trim();
        DialogResult = true;
        Close();
    }
}
