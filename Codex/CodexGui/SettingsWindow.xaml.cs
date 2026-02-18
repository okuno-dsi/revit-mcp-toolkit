using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace CodexGui;

public partial class SettingsWindow : Window
{
    public string ModelText => (ModelComboBox.Text ?? string.Empty).Trim();
    public string ReasoningEffortText => (ReasoningComboBox.Text ?? string.Empty).Trim();
    public string ProfileText => (ProfileComboBox.Text ?? string.Empty).Trim();
    public string BgColorHex => (BgColorTextBox.Text ?? string.Empty).Trim();
    public string FgColorHex => (FgColorTextBox.Text ?? string.Empty).Trim();
    public bool RequestStatusSend { get; private set; }
    public bool RequestModelRefresh { get; private set; }

    public SettingsWindow(
        IEnumerable<string> modelChoices,
        IEnumerable<string> reasoningChoices,
        IEnumerable<string> profileChoices,
        string modelText,
        string reasoningText,
        string profileText,
        string bgColorHex,
        string fgColorHex,
        string sessionId)
    {
        InitializeComponent();

        ModelComboBox.ItemsSource = NormalizeChoices(modelChoices);
        ReasoningComboBox.ItemsSource = NormalizeChoices(reasoningChoices);
        ProfileComboBox.ItemsSource = NormalizeChoices(profileChoices);

        ModelComboBox.Text = modelText ?? string.Empty;
        ReasoningComboBox.Text = reasoningText ?? string.Empty;
        ProfileComboBox.Text = profileText ?? string.Empty;
        BgColorTextBox.Text = bgColorHex ?? string.Empty;
        FgColorTextBox.Text = fgColorHex ?? string.Empty;
        SessionIdTextBox.Text = string.IsNullOrWhiteSpace(sessionId) ? "未取得" : sessionId;
    }

    private static List<string> NormalizeChoices(IEnumerable<string> source)
    {
        var list = new List<string>();
        foreach (var raw in source ?? Enumerable.Empty<string>())
        {
            var text = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (list.Any(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(text);
        }
        return list;
    }

    private void ModelDefaultButton_OnClick(object sender, RoutedEventArgs e)
    {
        ModelComboBox.Text = string.Empty;
    }

    private void ModelRefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        RequestModelRefresh = true;
        RequestStatusSend = false;
        DialogResult = true;
        Close();
    }

    private void ReasoningDefaultButton_OnClick(object sender, RoutedEventArgs e)
    {
        ReasoningComboBox.Text = string.Empty;
    }

    private void ProfileDefaultButton_OnClick(object sender, RoutedEventArgs e)
    {
        ProfileComboBox.Text = string.Empty;
    }

    private void ShowSessionIdButton_OnClick(object sender, RoutedEventArgs e)
    {
        var sid = SessionIdTextBox.Text ?? string.Empty;
        var dialog = new SimpleTextInputWindow(
            "Codex Session ID",
            "Session ID（コピー可能）",
            sid,
            readOnly: true,
            showCancel: false,
            selectAll: true)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void StatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        RequestStatusSend = true;
        DialogResult = true;
        Close();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        RequestModelRefresh = false;
        RequestStatusSend = false;
        DialogResult = true;
        Close();
    }
}
