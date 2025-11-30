using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MemAlerts.Client.Views;

public partial class AddUrlVideoWindow : Window
{
    public string VideoUrl { get; private set; } = string.Empty;

    public AddUrlVideoWindow()
    {
        InitializeComponent();
        UrlTextBox.Focus();
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateUrl();
    }

    private void ValidateUrl()
    {
        var url = UrlTextBox.Text.Trim();
        bool isValid = Uri.TryCreate(url, UriKind.Absolute, out var uriResult) 
                       && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

        if (string.IsNullOrEmpty(url))
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
            AddButton.IsEnabled = false;
        }
        else if (isValid)
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
            AddButton.IsEnabled = true;
        }
        else
        {
            ErrorTextBlock.Text = "Некорректный формат ссылки";
            ErrorTextBlock.Visibility = Visibility.Visible;
            AddButton.IsEnabled = false;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        VideoUrl = UrlTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // Support dragging the window since WindowStyle is None
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}

