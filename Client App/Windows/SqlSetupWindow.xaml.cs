using System.Windows;
using GKSKLaiXe.Models;
using GKSKLaiXe.Services;

namespace GKSKLaiXe.Windows;

public partial class SqlSetupWindow : Window
{
    private readonly ConfigService _config = new();
    private readonly SqlDataService _sql = new();
    private bool _connectionVerified;

    public SqlSetupWindow()
    {
        InitializeComponent();

        var current = _config.LoadSql();
        ServerBox.Text =
            string.IsNullOrWhiteSpace(current.Server)
                ? ""
                : current.DataSource;

        DatabaseBox.Text = current.Database;
        UsernameBox.Text = current.Username;
        PasswordBox.Password = current.Password;
    }

    private SqlSettings ReadSettings()
    {
        var dataSource =
            ServerBox.Text.Trim();

        // Port giữ lại = 0 vì DataSource đã nhập đầy đủ dạng SERVER\INSTANCE,PORT.
        return new SqlSettings
        {
            Server = dataSource,
            Port = 0,
            Database = DatabaseBox.Text.Trim(),
            Username = UsernameBox.Text.Trim(),
            Password = PasswordBox.Password
        };
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        _connectionVerified = false;

        try
        {
            await _sql.TestConnectionAsync(ReadSettings());
            _connectionVerified = true;
            SaveButton.IsEnabled = true;

            MessageBox.Show(
                "Kết nối SQL thành công. Đại ca có thể lưu cấu hình.",
                "Kết nối thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không thể kết nối SQL:\n" + ex.Message,
                "Lỗi kết nối",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_connectionVerified)
            return;

        _config.SaveSql(ReadSettings());
        DialogResult = true;
        Close();
    }
}
