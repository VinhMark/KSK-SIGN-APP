using System.Windows;
using GKSKLaiXe.Models;
using GKSKLaiXe.Services;
using GKSKLaiXe.Services.LanSigning;

namespace GKSKLaiXe.Windows;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _config = new();
    private readonly SqlDataService _sql = new();

    public SettingsWindow()
    {
        InitializeComponent();

        LoadSettings();
    }

    private void LoadSettings()
    {
var api =
            _config.LoadApi();

        ApiUrlBox.Text =
            api.Url;

        ApiUsernameBox.Text =
            api.Username;

        ApiPasswordBox.Password =
            api.Password;

        var signing = _config.LoadSigning();
        DirectSigningRadio.IsChecked = !signing.UseLan;
        LanSigningRadio.IsChecked = signing.UseLan;
        SigningServerUrlBox.Text = signing.ServerUrl;
        SigningApiKeyBox.Password = signing.ApiKey;
        UpdateSigningControls();

        var defaults =
            _config.LoadDriverDefaults();

        DefaultHospitalCodeBox.Text =
            defaults.IDBENHVIEN;

        DefaultHospitalNameBox.Text =
            defaults.BENHVIEN;

        DefaultDrugCombo.SelectedValue =
            string.IsNullOrWhiteSpace(defaults.MATUY)
                ? "0"
                : defaults.MATUY;

        DefaultDoctorBox.Text =
            defaults.BACSYKETLUAN;

        DefaultConclusionCombo.SelectedValue =
            string.IsNullOrWhiteSpace(defaults.KETLUAN)
                ? "A0-1"
                : defaults.KETLUAN;

        DefaultStateCombo.SelectedValue =
            string.IsNullOrWhiteSpace(defaults.STATE)
                ? "ADD"
                : defaults.STATE;
    }

    private ApiSettings ReadApiSettings()
    {
        return new ApiSettings
        {
            Url =
                ApiUrlBox.Text.Trim(),

            Username =
                ApiUsernameBox.Text.Trim(),

            Password =
                ApiPasswordBox.Password
        };
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
_config.SaveApi(
            ReadApiSettings());

        string signingServerUrl = SigningServerUrlBox.Text.Trim();
        string signingApiKey = SigningApiKeyBox.Password;

        if (LanSigningRadio.IsChecked == true)
        {
            try
            {
                signingServerUrl = LanSigningClient.NormalizeServerUrl(signingServerUrl);
                signingApiKey = LanSigningClient.NormalizeApiKey(signingApiKey);
            }
            catch (LanSigningServerException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Cấu hình ký số chưa hợp lệ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _config.SaveSigning(
            new SigningSettings
            {
                Mode = LanSigningRadio.IsChecked == true ? "LAN" : "DIRECT",
                ServerUrl = signingServerUrl,
                ApiKey = signingApiKey
            });

        _config.SaveDriverDefaults(
            new DriverKskDefaults
            {
                IDBENHVIEN =
                    DefaultHospitalCodeBox.Text.Trim(),

                BENHVIEN =
                    DefaultHospitalNameBox.Text.Trim(),

                MATUY =
                    DefaultDrugCombo.SelectedValue?.ToString()
                    ?? "0",

                BACSYKETLUAN =
                    DefaultDoctorBox.Text.Trim(),

                KETLUAN =
                    DefaultConclusionCombo.SelectedValue?.ToString()
                    ?? "A0-1",

                STATE =
                    DefaultStateCombo.SelectedValue?.ToString()
                    ?? "ADD"
            });

        MessageBox.Show(
            "Đã lưu cấu hình.",
            "GKSK Lái xe",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }

    private void SigningMode_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSigningControls();
    }

    private void UpdateSigningControls()
    {
        if (SigningServerUrlBox is null || SigningApiKeyBox is null || TestSigningButton is null)
            return;

        bool enabled = LanSigningRadio?.IsChecked == true;
        SigningServerUrlBox.IsEnabled = enabled;
        SigningApiKeyBox.IsEnabled = enabled;
        TestSigningButton.IsEnabled = enabled;
    }

    private async void TestSigning_Click(object sender, RoutedEventArgs e)
    {
        SigningTestStatus.Text = "Đang kiểm tra...";

        try
        {
            using var client = new LanSigningClient(
                SigningServerUrlBox.Text.Trim(),
                SigningApiKeyBox.Password);

            var status = await client.GetStatusAsync();
            SigningServerUrlBox.Text = LanSigningClient.NormalizeServerUrl(SigningServerUrlBox.Text);
            SigningApiKeyBox.Password = LanSigningClient.NormalizeApiKey(SigningApiKeyBox.Password);
            SigningTestStatus.Text = status.Success
                ? $"Kết nối thành công. Chứng thư: {status.Certificate?.Subject ?? "Không xác định"}"
                : "Server phản hồi nhưng chưa sẵn sàng.";
        }
        catch (Exception ex)
        {
            SigningTestStatus.Text = "Không kết nối được: " + ex.Message;
        }
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
