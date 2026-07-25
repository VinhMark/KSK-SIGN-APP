namespace GKSKLaiXe.Models;

public sealed class SigningSettings
{
    public string Mode { get; set; } = "DIRECT";
    public string ServerUrl { get; set; } = "http://127.0.0.1:7443";
    public string ApiKey { get; set; } = "";

    public bool UseLan => string.Equals(Mode, "LAN", StringComparison.OrdinalIgnoreCase);
}
