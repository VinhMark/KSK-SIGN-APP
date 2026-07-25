namespace GKSKLaiXe.Models;

public sealed class SqlSettings
{
    public string Server { get; set; } = "";
    public int Port { get; set; } = 1433;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public string DataSource
    {
        get
        {
            var server = (Server ?? "").Trim();

            if (string.IsNullOrWhiteSpace(server))
                return "";

            // Nếu người dùng đã nhập đầy đủ dạng SERVER\INSTANCE,PORT
            // thì dùng nguyên chuỗi và bỏ qua Port riêng.
            if (server.Contains(','))
                return server;

            return Port > 0
                ? $"{server},{Port}"
                : server;
        }
    }
}
