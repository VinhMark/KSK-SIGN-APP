using System.Windows;
using GKSKLaiXe.Models;
using GKSKLaiXe.Services;

namespace GKSKLaiXe.Windows;

public partial class SqlSettingsWindow : Window
{
    private readonly ConfigService _config = new();
    private readonly SqlDataService _sql = new();

    public SqlSettingsWindow()
    {
        InitializeComponent();
        var s = _config.LoadSql();
        ServerBox.Text = s.Server;
        PortBox.Text = s.Port.ToString();
        DatabaseBox.Text = s.Database;
        UsernameBox.Text = s.Username;
        PasswordBox.Password = s.Password;
    }

    private SqlSettings Read()
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port)) port = 1433;
        return new SqlSettings
        {
            Server = ServerBox.Text.Trim(),
            Port = port,
            Database = DatabaseBox.Text.Trim(),
            Username = UsernameBox.Text.Trim(),
            Password = PasswordBox.Password
        };
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _sql.TestConnectionAsync(Read());
            MessageBox.Show("Kết nối SQL thành công.", "GKSK Lái xe",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không thể kết nối SQL:\n" + ex.Message, "Lỗi",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.SaveSql(Read());
        DialogResult = true;
        Close();
    }
}
