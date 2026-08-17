using Alpha.Branding.Services;
using System.Windows;

namespace Alpha.Branding;

public partial class SessionConfirmationDialog : Window
{
    public SessionPromptResult Result { get; private set; } = SessionPromptResult.Cancel;

    public SessionConfirmationDialog(string? title = null, string? message = null)
    {
        InitializeComponent();
        WindowThemeHelper.EnableDarkTitleBar(this);

        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
            TitleTextBlock.Text = title;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            MessageTextBlock.Text = message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = SessionPromptResult.Cancel;
        DialogResult = false;
        Close();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        Result = SessionPromptResult.DiscardAndContinue;
        DialogResult = true;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = SessionPromptResult.SaveAndContinue;
        DialogResult = true;
        Close();
    }
}
