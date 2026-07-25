using System.Windows;
using GKSKLaiXe.Services;
using GKSKLaiXe.Windows;

namespace GKSKLaiXe;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = new ConfigService();
        var sql = new SqlDataService();
        var needsSetup = !config.HasSqlConfig;

        if (!needsSetup)
        {
            try
            {
                await sql.TestConnectionAsync(config.LoadSql());
            }
            catch
            {
                needsSetup = true;
            }
        }

        if (needsSetup)
        {
            var setup = new SqlSetupWindow();

            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }
}
