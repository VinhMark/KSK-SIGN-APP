using System.Windows;
using GKSKLaiXe.Models;
using GKSKLaiXe.Services;

namespace GKSKLaiXe.Windows;

public partial class ApiSettingsWindow : Window
{
    private readonly ConfigService _config = new();

    public ApiSettingsWindow()
    {
        InitializeComponent();

        var settings = _config.LoadApi();
        UrlBox.Text = settings.Url;
        UsernameBox.Text = settings.Username;
        PasswordBox.Password = settings.Password;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new ApiSettings
        {
            Url = UrlBox.Text.Trim(),
            Username = UsernameBox.Text.Trim(),
            Password = PasswordBox.Password
        };

        _config.SaveApi(settings);

        MessageBox.Show(
            "Đã lưu cấu hình liên thông.",
            "GKSK Lái xe",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }
}
