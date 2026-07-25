using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GKSKLaiXe.Models;
using GKSKLaiXe.Services;
using GKSKLaiXe.Services.LanSigning;
using GKSKLaiXe.Windows;
using Microsoft.Win32;

namespace GKSKLaiXe;

public partial class MainWindow : Window
{
    private ObservableCollection<GkskRecord> _records = [];
    private ICollectionView? _recordsView;
    private ObservableCollection<GkskRecord> _generalRecords = [];
    private ICollectionView? _generalRecordsView;

    private List<string> _availablePackages = [];
    private List<string> _selectedPackages = [];
    private string _currentKskMode = "DRIVER";

    private readonly ConfigService _config = new();
    private readonly SqlDataService _sql = new();
    private readonly ExcelService _excel = new();
    private readonly ValidationService _validator = new();
    private readonly ApiService _api = new();
    private readonly SignDataService _signData = new();
    private readonly SentHistoryService _sentHistory = new();

    public MainWindow()
    {
        InitializeComponent();

        DriverFromDatePicker.SelectedDate = DateTime.Today;
        DriverToDatePicker.SelectedDate = DateTime.Today;
        GeneralFromDatePicker.SelectedDate = DateTime.Today;
        GeneralToDatePicker.SelectedDate = DateTime.Today;
        InitializeDateFilters();

        _selectedPackages =
            _config.LoadSelectedPackages(
                _currentKskMode);

        _records.CollectionChanged += Records_CollectionChanged;

        RefreshView();
        UpdateNavigationState(0);
    }

    private void InitializeDateFilters()
    {
        var today = DateTime.Today;
        DriverDayPicker.SelectedDate = today;
        GeneralDayPicker.SelectedDate = today;
        var months = Enumerable.Range(1, 12).ToList();
        var quarters = Enumerable.Range(1, 4).ToList();
        var years = Enumerable.Range(today.Year - 10, 21).Reverse().ToList();
        DriverMonthCombo.ItemsSource = months; GeneralMonthCombo.ItemsSource = months;
        DriverQuarterCombo.ItemsSource = quarters; GeneralQuarterCombo.ItemsSource = quarters;
        DriverMonthYearCombo.ItemsSource = years; GeneralMonthYearCombo.ItemsSource = years;
        DriverQuarterYearCombo.ItemsSource = years; GeneralQuarterYearCombo.ItemsSource = years;
        DriverYearCombo.ItemsSource = years; GeneralYearCombo.ItemsSource = years;
        DriverMonthCombo.SelectedItem = today.Month; GeneralMonthCombo.SelectedItem = today.Month;
        DriverQuarterCombo.SelectedItem = ((today.Month - 1) / 3) + 1; GeneralQuarterCombo.SelectedItem = ((today.Month - 1) / 3) + 1;
        DriverMonthYearCombo.SelectedItem = today.Year; GeneralMonthYearCombo.SelectedItem = today.Year;
        DriverQuarterYearCombo.SelectedItem = today.Year; GeneralQuarterYearCombo.SelectedItem = today.Year;
        DriverYearCombo.SelectedItem = today.Year; GeneralYearCombo.SelectedItem = today.Year;
    }

    private static string GetFilterType(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "RANGE";

    private static void SetFilterPanels(string type, params StackPanel[] panels)
    {
        foreach (var p in panels) p.Visibility = Visibility.Collapsed;
        var index = type switch { "DAY" => 1, "MONTH" => 2, "QUARTER" => 3, "YEAR" => 4, _ => 0 };
        panels[index].Visibility = Visibility.Visible;
    }

    private void DriverFilterType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DriverRangePanel == null) return;
        SetFilterPanels(GetFilterType(DriverFilterTypeCombo), DriverRangePanel, DriverDayPanel, DriverMonthPanel, DriverQuarterPanel, DriverYearPanel);
    }

    private void GeneralFilterType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralRangePanel == null) return;
        SetFilterPanels(GetFilterType(GeneralFilterTypeCombo), GeneralRangePanel, GeneralDayPanel, GeneralMonthPanel, GeneralQuarterPanel, GeneralYearPanel);
    }

    private (DateTime From, DateTime To) GetDriverDateRange()
    {
        var today = DateTime.Today;
        return GetFilterType(DriverFilterTypeCombo) switch
        {
            "DAY" => ((DriverDayPicker.SelectedDate ?? today).Date, (DriverDayPicker.SelectedDate ?? today).Date),
            "MONTH" => MonthRange((int?)DriverMonthCombo.SelectedItem ?? today.Month, (int?)DriverMonthYearCombo.SelectedItem ?? today.Year),
            "QUARTER" => QuarterRange((int?)DriverQuarterCombo.SelectedItem ?? 1, (int?)DriverQuarterYearCombo.SelectedItem ?? today.Year),
            "YEAR" => YearRange((int?)DriverYearCombo.SelectedItem ?? today.Year),
            _ => ((DriverFromDatePicker.SelectedDate ?? today).Date, (DriverToDatePicker.SelectedDate ?? today).Date)
        };
    }

    private (DateTime From, DateTime To) GetGeneralDateRange()
    {
        var today = DateTime.Today;
        return GetFilterType(GeneralFilterTypeCombo) switch
        {
            "DAY" => ((GeneralDayPicker.SelectedDate ?? today).Date, (GeneralDayPicker.SelectedDate ?? today).Date),
            "MONTH" => MonthRange((int?)GeneralMonthCombo.SelectedItem ?? today.Month, (int?)GeneralMonthYearCombo.SelectedItem ?? today.Year),
            "QUARTER" => QuarterRange((int?)GeneralQuarterCombo.SelectedItem ?? 1, (int?)GeneralQuarterYearCombo.SelectedItem ?? today.Year),
            "YEAR" => YearRange((int?)GeneralYearCombo.SelectedItem ?? today.Year),
            _ => ((GeneralFromDatePicker.SelectedDate ?? today).Date, (GeneralToDatePicker.SelectedDate ?? today).Date)
        };
    }

    private static (DateTime From, DateTime To) MonthRange(int month, int year) =>
        (new DateTime(year, month, 1), new DateTime(year, month, DateTime.DaysInMonth(year, month)));
    private static (DateTime From, DateTime To) QuarterRange(int quarter, int year)
    {
        var from = new DateTime(year, ((quarter - 1) * 3) + 1, 1);
        return (from, from.AddMonths(3).AddDays(-1));
    }
    private static (DateTime From, DateTime To) YearRange(int year) => (new DateTime(year, 1, 1), new DateTime(year, 12, 31));

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var previousIndex = MainScreenTabs.SelectedIndex;
        UpdateNavigationState(3);

        var win = new SettingsWindow
        {
            Owner = this
        };

        win.ShowDialog();
        UpdateNavigationState(previousIndex);
    }


    private async void SelectPackages_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings = _config.LoadSql();

            if (_availablePackages.Count == 0)
            {
                StatusText.Text = "Đang tải danh sách gói khám...";

                _availablePackages =
                    (await _sql.LoadPackageNamesAsync(settings))
                    .ToList();

            }

            var win =
                new PackageSelectionWindow(
                    _availablePackages,
                    _selectedPackages,
                    _currentKskMode)
                {
                    Owner = this
                };

            if (win.ShowDialog() == true)
            {
                _selectedPackages =
                    win.SelectedPackages.ToList();

                _config.SaveSelectedPackages(
                    _selectedPackages,
                    _currentKskMode);

                StatusText.Text =
                    $"Đã chọn {_selectedPackages.Count} gói.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không tải được danh sách gói:\n" + ex.Message,
                "Lỗi",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void LoadSql_Click(object sender, RoutedEventArgs e)
    {
        LoadSqlButton.IsEnabled = false;

        try
        {
            if (_selectedPackages.Count == 0)
            {
                MessageBox.Show(
                    _currentKskMode == "DRIVER"
                        ? "Vui lòng chọn ít nhất một gói KSK LÁI XE trước khi lấy dữ liệu SQL."
                        : "Vui lòng chọn ít nhất một gói KSK không phải LÁI XE trước khi lấy dữ liệu SQL.",
                    "Chưa chọn gói",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            StatusText.Text = "Đang lấy dữ liệu SQL...";

            var settings = _config.LoadSql();
            var (fromDate, toDate) = GetDriverDateRange();

            if (toDate.Date < fromDate.Date)
            {
                MessageBox.Show("Đến ngày không được nhỏ hơn Từ ngày.", "Khoảng thời gian", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _records = await _sql.LoadAsync(
                settings,
                fromDate,
                toDate,
                OnlyPaidCheck.IsChecked == true,
                _selectedPackages);

            _sentHistory.ApplySentFlags(_records);

            SubscribeRecordSelectionChanges();

            SearchBox.Text = "";

            RefreshView();

            StatusText.Text = $"Đã tải {_records.Count} hồ sơ.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Lỗi lấy dữ liệu SQL.";

            MessageBox.Show(
                ex.Message,
                "Lỗi SQL",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            LoadSqlButton.IsEnabled = true;
        }
    }

    private void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            Title = "Chọn file dữ liệu GKSK"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var defaults =
                _config.LoadDriverDefaults();

            _records =
                _excel.Import(
                    dlg.FileName,
                    defaults);

            SubscribeRecordSelectionChanges();

            SearchBox.Text = "";

            RefreshView();

            StatusText.Text =
                $"Đã import {_records.Count} hồ sơ từ Excel.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Lỗi Import Excel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"KSK_LAI_XE_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            _excel.ExportDriverKsk(
                dlg.FileName,
                _records);

            StatusText.Text = "Đã export Excel.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Lỗi Export Excel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Validate_Click(
        object sender,
        RoutedEventArgs e)
    {
        CommitGridChanges();

        int ok = 0;
        int fail = 0;

        foreach (var r in _records)
        {
            var errors = _validator.Validate(r);

            r.ValidationStatus =
                errors.Count == 0
                    ? "Hợp lệ"
                    : "Lỗi";

            r.ErrorMessage =
                string.Join("; ", errors);

            if (errors.Count == 0)
                ok++;
            else
                fail++;
        }

        _recordsView?.Refresh();

        StatusText.Text =
            $"Kiểm tra xong: {ok} hợp lệ, {fail} lỗi.";
    }


    private async void ExportSignedXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!ExportSignedXmlButton.IsEnabled)
            return;

        ExportSignedXmlButton.IsEnabled = false;
        SendApiButton.IsEnabled = false;

        LanSigningClient? lanSigningClient = null;

        try
        {
            CommitGridChanges();

            var selected = _records
                .Where(x => x.IsSelected)
                .GroupBy(x => new
                {
                    SO = (x.SO ?? "").Trim().ToUpperInvariant(),
                    HANG = (x.HANGBANGLAI ?? "").Trim().ToUpperInvariant()
                })
                .Select(g => g.First())
                .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    "Chưa chọn hồ sơ để xuất XML ký số.",
                    "Xuất XML ký số",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var valid = new List<GkskRecord>();
            foreach (var record in selected)
            {
                // Cổng KSK lái xe yêu cầu nồng độ cồn luôn bằng 0.
                record.NONGDOCON = "0";

                var errors = _validator.Validate(record);
                record.ValidationStatus = errors.Count == 0 ? "Hợp lệ" : "Lỗi";
                record.ErrorMessage = string.Join("; ", errors);

                if (errors.Count == 0)
                    valid.Add(record);
            }

            _recordsView?.Refresh();

            if (valid.Count == 0)
            {
                MessageBox.Show(
                    "Không có hồ sơ hợp lệ để xuất XML ký số. Hãy bấm Kiểm tra và sửa dữ liệu trước.",
                    "Xuất XML ký số",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string outputDirectory;
            string? singleFilePath = null;

            if (valid.Count == 1)
            {
                var record = valid[0];
                var saveDialog = new SaveFileDialog
                {
                    Filter = "XML đã ký (*.xml)|*.xml",
                    FileName = BuildSignedXmlFileName(record),
                    Title = "Lưu XML đã ký để kiểm tra"
                };

                if (saveDialog.ShowDialog() != true)
                    return;

                singleFilePath = saveDialog.FileName;
                outputDirectory = Path.GetDirectoryName(singleFilePath)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
            else
            {
                var folderDialog = new OpenFolderDialog
                {
                    Title = $"Chọn thư mục lưu {valid.Count} XML đã ký",
                    Multiselect = false
                };

                if (folderDialog.ShowDialog() != true)
                    return;

                outputDirectory = folderDialog.FolderName;
            }

            var signingSettings = _config.LoadSigning();
            System.Security.Cryptography.X509Certificates.X509Certificate2? signingCertificate = null;

            if (signingSettings.UseLan)
            {
                if (string.IsNullOrWhiteSpace(signingSettings.ServerUrl) ||
                    string.IsNullOrWhiteSpace(signingSettings.ApiKey))
                {
                    MessageBox.Show(
                        "Chưa cấu hình địa chỉ Signing Server hoặc API Key. Vào Cấu hình → Ký số để thiết lập.",
                        "Thiếu cấu hình ký LAN",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    lanSigningClient = new LanSigningClient(
                        signingSettings.ServerUrl,
                        signingSettings.ApiKey);

                    var status = await lanSigningClient.GetStatusAsync();
                    if (!status.Success)
                        throw new InvalidOperationException("Signing Server chưa sẵn sàng.");
                }
                catch (Exception ex)
                {
                    ShowSigningServerConnectionError(
                        signingSettings.ServerUrl,
                        ex,
                        "Chưa tạo file XML ký số nào.");
                    return;
                }
            }
            else
            {
                signingCertificate = _signData.SelectCertificate();
                if (signingCertificate is null)
                {
                    MessageBox.Show(
                        "Chưa chọn chứng thư số. Hủy xuất XML.",
                        "Ký số trực tiếp",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            int successCount = 0;
            var errorsDuringExport = new List<string>();

            foreach (var record in valid)
            {
                try
                {
                    StatusText.Text = $"Đang ký XML {successCount + 1}/{valid.Count}: {record.SO}";

                    string signedXml;
                    if (signingSettings.UseLan)
                    {
                        var unsignedXml = _signData.CreateUnsignedXmlText(record);
                        var result = await lanSigningClient!.SignXmlAsync(
                            unsignedXml,
                            "KSK_LAI_XE",
                            record.SO,
                            Environment.UserName);

                        signedXml = result.SignedXml
                            ?? throw new InvalidOperationException("Signing Server không trả về XML đã ký.");
                    }
                    else
                    {
                        signedXml = _signData.CreateSignedXmlText(record, signingCertificate!);
                    }

                    record.SIGNDATA = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes(signedXml));

                    var filePath = singleFilePath
                        ?? Path.Combine(outputDirectory, BuildSignedXmlFileName(record));

                    await File.WriteAllTextAsync(
                        filePath,
                        signedXml,
                        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    successCount++;
                }
                catch (LanSigningServerException ex) when (ex.IsConnectionFailure)
                {
                    ShowSigningServerConnectionError(
                        signingSettings.ServerUrl,
                        ex,
                        $"Đã dừng xuất XML. Đã tạo thành công {successCount}/{valid.Count} file.");
                    break;
                }
                catch (Exception ex)
                {
                    errorsDuringExport.Add($"{record.SO}: {ex.Message}");
                }
            }

            StatusText.Text =
                $"Xuất XML ký số hoàn tất: {successCount}/{valid.Count} file. Không gửi cổng, không ghi lịch sử.";

            var message =
                $"Đã tạo {successCount}/{valid.Count} file XML ký số.\n" +
                $"Thư mục: {outputDirectory}\n\n" +
                "Chức năng này chỉ dùng kiểm tra chữ ký; hồ sơ chưa được gửi lên cổng giám định và chưa được đánh dấu Đã gửi.";

            if (errorsDuringExport.Count > 0)
                message += "\n\nLỗi:\n" + string.Join("\n", errorsDuringExport.Take(10));

            MessageBox.Show(
                message,
                "Kết quả xuất XML ký số",
                MessageBoxButton.OK,
                errorsDuringExport.Count > 0
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);
        }
        finally
        {
            lanSigningClient?.Dispose();
            ExportSignedXmlButton.IsEnabled = true;
            SendApiButton.IsEnabled = true;
        }
    }

    private static string BuildSignedXmlFileName(GkskRecord record)
    {
        var rawName = $"KSK_LAI_XE_{record.SO}_{record.HOTEN}_SIGNED.xml";
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            rawName = rawName.Replace(invalidChar, '_');

        return rawName.Replace(' ', '_');
    }

    private async void SendApi_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!SendApiButton.IsEnabled)
            return;

        SendApiButton.IsEnabled = false;

        SendProgressBar.Visibility = Visibility.Visible;
        SendProgressBar.Value = 0;
        SendProgressText.Text = "0/0";

        try
        {
            CommitGridChanges();

            var selected =
                _records
                    .Where(x => x.IsSelected)
                    .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    "Chưa chọn hồ sơ để đẩy cổng.",
                    "Đẩy cổng giám định",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Chặn trùng trong chính lượt gửi hiện tại theo SO + HANGBANGLAI.
            // Giữ hồ sơ đầu tiên của mỗi khóa.
            var uniqueSelected =
                selected
                    .GroupBy(
                        x => new
                        {
                            SO = (x.SO ?? "").Trim().ToUpperInvariant(),
                            HANG = (x.HANGBANGLAI ?? "").Trim().ToUpperInvariant()
                        })
                    .Select(g => g.First())
                    .ToList();

            var duplicateCount =
                selected.Count - uniqueSelected.Count;

            var valid =
                new List<GkskRecord>();

            foreach (var r in uniqueSelected)
            {
                // Cổng KSK Lái xe yêu cầu nồng độ cồn luôn bằng 0.
                r.NONGDOCON = "0";

                var errors =
                    _validator.Validate(r);

                r.ValidationStatus =
                    errors.Count == 0
                        ? "Hợp lệ"
                        : "Lỗi";

                r.ErrorMessage =
                    string.Join("; ", errors);

                if (errors.Count == 0)
                    valid.Add(r);
            }

            var invalidCount =
                uniqueSelected.Count - valid.Count;

            if (valid.Count == 0)
            {
                StatusText.Text =
                    "Không có hồ sơ hợp lệ để đẩy cổng.";

                _recordsView?.Refresh();
                return;
            }

            var apiSettings =
                _config.LoadApi();

            if (string.IsNullOrWhiteSpace(apiSettings.Username) ||
                string.IsNullOrWhiteSpace(apiSettings.Password))
            {
                MessageBox.Show(
                    "Chưa cấu hình Username/Password liên thông.",
                    "Thiếu cấu hình liên thông",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var signingSettings = _config.LoadSigning();
            System.Security.Cryptography.X509Certificates.X509Certificate2? signingCertificate = null;
            LanSigningClient? lanSigningClient = null;

            if (signingSettings.UseLan)
            {
                if (string.IsNullOrWhiteSpace(signingSettings.ServerUrl) ||
                    string.IsNullOrWhiteSpace(signingSettings.ApiKey))
                {
                    MessageBox.Show(
                        "Chưa cấu hình địa chỉ Signing Server hoặc API Key. Vào Cấu hình → Ký số để thiết lập.",
                        "Thiếu cấu hình ký LAN",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    lanSigningClient = new LanSigningClient(
                        signingSettings.ServerUrl,
                        signingSettings.ApiKey);

                    var status = await lanSigningClient.GetStatusAsync();
                    if (!status.Success)
                        throw new InvalidOperationException("Signing Server chưa sẵn sàng.");
                }
                catch (Exception ex)
                {
                    lanSigningClient?.Dispose();
                    ShowSigningServerConnectionError(
                        signingSettings.ServerUrl,
                        ex,
                        "Hồ sơ chưa được ký và chưa được gửi lên cổng BHXH.");
                    return;
                }
            }
            else
            {
                signingCertificate = _signData.SelectCertificate();

                if (signingCertificate is null)
                {
                    MessageBox.Show(
                        "Chưa chọn chứng thư số. Hủy đẩy cổng.",
                        "Ký số trực tiếp",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            var note =
                duplicateCount > 0
                    ? $"\nĐã tự loại {duplicateCount} hồ sơ trùng SO + Hạng GPLX trong lượt gửi."
                    : "";

            var confirm =
                MessageBox.Show(
                    $"Sẽ gửi {valid.Count} hồ sơ hợp lệ.{note}\n" +
                    (invalidCount > 0
                        ? $"Có {invalidCount} hồ sơ lỗi sẽ được bỏ qua.\n"
                        : "") +
                    "\nTiếp tục?",
                    "Xác nhận gửi hàng loạt",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            // Mỗi hồ sơ lưu lịch sử theo đúng ngày phát sinh, kể cả khi đang lọc nhiều ngày.
            var sourceDate = DateTime.Today;

            int successCount = 0;
            int failedCount = 0;
            int processedCount = 0;
            bool signingServerDisconnected = false;

            SendProgressBar.Minimum = 0;
            SendProgressBar.Maximum = valid.Count;
            SendProgressBar.Value = 0;
            SendProgressText.Text = $"0/{valid.Count}";

            foreach (var r in valid)
            {
                processedCount++;

                try
                {
                    r.SendStatus =
                        $"Đang gửi {processedCount}/{valid.Count}...";

                    StatusText.Text =
                        $"Đang gửi hồ sơ {processedCount}/{valid.Count}: {r.SO}";

                    _recordsView?.Refresh();

                    string signedRootXml;

                    if (signingSettings.UseLan)
                    {
                        var unsignedXml = _signData.CreateUnsignedXmlText(r);
                        var lanResult = await lanSigningClient!.SignXmlAsync(
                            unsignedXml,
                            "KSK_LAI_XE",
                            r.SO,
                            apiSettings.Username);

                        signedRootXml = lanResult.SignedXml
                            ?? throw new InvalidOperationException("Signing Server không trả về XML đã ký.");
                    }
                    else
                    {
                        signedRootXml = _signData.CreateSignedXmlText(
                            r,
                            signingCertificate!);
                    }

                    r.SIGNDATA =
                        Convert.ToBase64String(
                            System.Text.Encoding.UTF8.GetBytes(
                                signedRootXml));

                    var result =
                        await _api.SendAsync(
                            r,
                            apiSettings,
                            includeSignData: true);

                    r.ApiMessage =
                        result.Message;

                    r.UUID =
                        result.UUID;

                    if (result.Success)
                    {
                        r.SendStatus =
                            "Gửi thành công";

                        r.IsSent =
                            true;

                        _sentHistory.MarkSent(
                            r,
                            r.CreateDate?.Date ?? sourceDate.Date,
                            result.UUID,
                            "1",
                            result.Message);

                        successCount++;
                    }
                    else
                    {
                        r.SendStatus =
                            "Gửi thất bại";

                        failedCount++;
                    }
                }
                catch (LanSigningServerException ex) when (signingSettings.UseLan && ex.IsConnectionFailure)
                {
                    // Mất kết nối Signing Server: dừng ngay để không thử gửi tiếp
                    // và tuyệt đối không gọi API BHXH khi hồ sơ chưa ký thành công.
                    r.SendStatus = "Chưa gửi - mất kết nối ký số";
                    r.ApiMessage = ex.Message;
                    failedCount++;
                    signingServerDisconnected = true;

                    foreach (var pending in valid.Skip(processedCount))
                    {
                        pending.SendStatus = "Chưa gửi - Signing Server ngoại tuyến";
                        pending.ApiMessage = "Chưa xử lý vì mất kết nối Signing Server.";
                    }

                    ShowSigningServerConnectionError(
                        signingSettings.ServerUrl,
                        ex,
                        $"Đã dừng lô tại hồ sơ {processedCount}/{valid.Count}. " +
                        "Các hồ sơ còn lại chưa được ký và chưa được gửi lên cổng BHXH.");
                }
                catch (Exception ex)
                {
                    // Lỗi nghiệp vụ của một hồ sơ không dừng cả lô.
                    r.SendStatus =
                        "Lỗi gửi";

                    r.ApiMessage =
                        ex.Message;

                    failedCount++;
                }
                finally
                {
                    SendProgressBar.Value =
                        processedCount;

                    SendProgressText.Text =
                        $"{processedCount}/{valid.Count}";

                    StatusText.Text =
                        $"Đã xử lý {processedCount}/{valid.Count} | " +
                        $"Thành công: {successCount} | Lỗi: {failedCount}";

                    _recordsView?.Refresh();

                    // Cho UI kịp vẽ tiến trình.
                    await Task.Yield();
                }

                if (signingServerDisconnected)
                    break;
            }

            _recordsView?.Refresh();
            UpdateVisibleCount();
            UpdateHeaderCheckState();

            StatusText.Text = signingServerDisconnected
                ? $"Đã dừng gửi do mất kết nối Signing Server: {successCount} thành công, {failedCount} lỗi."
                : $"Gửi hàng loạt hoàn tất: {successCount} thành công, {failedCount} lỗi.";

            MessageBox.Show(
                $"Đã xử lý {processedCount}/{valid.Count} hồ sơ.\n" +
                $"Thành công: {successCount}\n" +
                $"Lỗi: {failedCount}" +
                (signingServerDisconnected
                    ? $"\nChưa xử lý: {valid.Count - processedCount} hồ sơ do Signing Server mất kết nối."
                    : "") +
                (duplicateCount > 0
                    ? $"\nĐã bỏ qua trùng: {duplicateCount}"
                    : ""),
                "Kết quả gửi hàng loạt",
                MessageBoxButton.OK,
                failedCount > 0
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);

            lanSigningClient?.Dispose();
        }
        finally
        {
            SendApiButton.IsEnabled = true;

            SendProgressBar.Visibility = Visibility.Collapsed;
            SendProgressBar.Value = 0;
            SendProgressText.Text = "";
        }
    }

    private static void ShowSigningServerConnectionError(
        string? serverUrl,
        Exception exception,
        string consequence)
    {
        var reason = exception switch
        {
            LanSigningServerException { Kind: LanSigningErrorKind.Timeout }
                => "Kết nối tới máy chủ ký số đã hết thời gian chờ.",
            LanSigningServerException { Kind: LanSigningErrorKind.InvalidConfiguration }
                => "Địa chỉ máy chủ ký số không hợp lệ.",
            LanSigningServerException { Kind: LanSigningErrorKind.ConnectionFailed }
                => "Máy chủ ký số không phản hồi hoặc cổng kết nối chưa mở.",
            LanSigningServerException { Kind: LanSigningErrorKind.NetworkError }
                => "Có lỗi mạng khi kết nối tới máy chủ ký số.",
            _ => exception.Message
        };

        MessageBox.Show(
            "KHÔNG THỂ KẾT NỐI MÁY CHỦ KÝ SỐ\n\n" +
            $"Địa chỉ: {serverUrl}\n" +
            $"Nguyên nhân: {reason}\n\n" +
            consequence + "\n\n" +
            "Vui lòng kiểm tra:\n" +
            "• Signing Server đã được mở trên máy chủ.\n" +
            "• Địa chỉ IP và cổng trong Cấu hình → Ký số.\n" +
            "• Hai máy đang cùng mạng LAN và Windows Firewall không chặn cổng.",
            "Signing Server ngoại tuyến",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private async void SelectGeneralPackages_Click(
        object sender,
        RoutedEventArgs e)
    {
        _currentKskMode = "GENERAL";

        try
        {
            var settings = _config.LoadSql();

            if (_availablePackages.Count == 0)
            {
                GeneralStatusText.Text =
                    "Đang tải danh sách gói khám...";

                _availablePackages =
                    (await _sql.LoadPackageNamesAsync(settings))
                    .ToList();
            }

            _selectedPackages =
                _config.LoadSelectedPackages("GENERAL");

            var win =
                new PackageSelectionWindow(
                    _availablePackages,
                    _selectedPackages,
                    "GENERAL")
                {
                    Owner = this
                };

            if (win.ShowDialog() == true)
            {
                _selectedPackages =
                    win.SelectedPackages.ToList();

                _config.SaveSelectedPackages(
                    _selectedPackages,
                    "GENERAL");

                GeneralStatusText.Text =
                    $"Đã chọn {_selectedPackages.Count} gói KSK.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không tải được danh sách gói:\n" + ex.Message,
                "Lỗi",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void LoadGeneralSql_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadGeneralSqlButton.IsEnabled = false;

        try
        {
            _currentKskMode = "GENERAL";

            _selectedPackages =
                _config.LoadSelectedPackages("GENERAL");

            if (_selectedPackages.Count == 0)
            {
                MessageBox.Show(
                    "Vui lòng chọn ít nhất một gói KSK trước khi lấy dữ liệu SQL.",
                    "Chưa chọn gói",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            GeneralStatusText.Text =
                "Đang lấy dữ liệu KSK...";

            var settings =
                _config.LoadSql();

            var (fromDate, toDate) = GetGeneralDateRange();

            if (toDate.Date < fromDate.Date)
            {
                MessageBox.Show("Đến ngày không được nhỏ hơn Từ ngày.", "Khoảng thời gian", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _generalRecords =
                await _sql.LoadAsync(
                    settings,
                    fromDate,
                    toDate,
                    GeneralOnlyPaidCheck.IsChecked == true,
                    _selectedPackages);

            foreach (var r in _generalRecords)
            {
                // SqlDataService đã tách PatientReceive.Reason theo dấu "-" cuối:
                // Ví dụ: 00000/GKSK-VHTP-1
                //   SO = 00000/GKSK-VHTP
                //   HANGBANGLAI = 1
                //
                // Với KSK thường, phần cuối được dùng làm Loại Sức khỏe.
                var healthTypeCode =
                    (r.HANGBANGLAI ?? "")
                    .Trim();

                r.GeneralHealthType =
                    healthTypeCode is "1" or "2" or "3" or "4" or "5"
                        ? $"LOẠI {healthTypeCode}"
                        : "";
            }

            RefreshGeneralView();

            GeneralStatusText.Text =
                $"Đã tải {_generalRecords.Count} hồ sơ KSK.";
        }
        catch (Exception ex)
        {
            GeneralStatusText.Text =
                "Lỗi lấy dữ liệu KSK.";

            MessageBox.Show(
                ex.Message,
                "Lỗi SQL",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            LoadGeneralSqlButton.IsEnabled = true;
        }
    }

    private void RefreshGeneralView()
    {
        _generalRecordsView =
            CollectionViewSource
                .GetDefaultView(_generalRecords);

        _generalRecordsView.Filter =
            FilterGeneralRecord;

        GeneralGrid.ItemsSource =
            _generalRecordsView;

        _generalRecordsView.Refresh();

        UpdateGeneralHeaderCheckState();
    }

    private bool FilterGeneralRecord(object obj)
    {
        if (obj is not GkskRecord r)
            return false;

        var keyword =
            (GeneralSearchBox?.Text ?? "")
            .Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return true;

        string[] values =
        [
            r.SO,
            r.IntroName,
            r.ServiceName,
            r.CreateDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
            r.GeneralHealthCategory,
            r.GeneralHealthType,
            r.HOTEN,
            r.NGAYSINH,
            r.GenderDisplay,
            r.PaidDisplay
        ];

        return values.Any(
            v =>
                (v ?? "")
                .Contains(
                    keyword,
                    StringComparison.CurrentCultureIgnoreCase));
    }

    private void GeneralSearch_Click(
        object sender,
        RoutedEventArgs e)
    {
        _generalRecordsView?.Refresh();
        UpdateGeneralHeaderCheckState();
    }

    private void GeneralSearchBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        _generalRecordsView?.Refresh();
        UpdateGeneralHeaderCheckState();
    }

    private void GeneralSelectAllHeaderCheck_Click(
        object sender,
        RoutedEventArgs e)
    {
        var visibleRecords =
            GeneralGrid.Items
                .OfType<GkskRecord>()
                .ToList();

        bool selectAll =
            GeneralSelectAllHeaderCheck.IsChecked == true;

        foreach (var item in visibleRecords)
            item.IsSelected = selectAll;

        GeneralGrid.Items.Refresh();
        UpdateGeneralHeaderCheckState();
    }

    private void UpdateGeneralHeaderCheckState()
    {
        if (GeneralSelectAllHeaderCheck is null)
            return;

        var visibleRecords =
            GeneralGrid.Items
                .OfType<GkskRecord>()
                .ToList();

        if (visibleRecords.Count == 0)
        {
            GeneralSelectAllHeaderCheck.IsChecked = false;
            return;
        }

        var selectedCount =
            visibleRecords.Count(x => x.IsSelected);

        GeneralSelectAllHeaderCheck.IsChecked =
            selectedCount == 0
                ? false
                : selectedCount == visibleRecords.Count
                    ? true
                    : null;
    }

    private void ExportGeneralExcel_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_generalRecords.Count == 0)
        {
            MessageBox.Show(
                "Chưa có dữ liệu KSK để xuất.");
            return;
        }

        var dlg =
            new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName =
                    $"KSK_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            _excel.ExportGeneralKsk(
                dlg.FileName,
                _generalRecords);

            GeneralStatusText.Text =
                "Đã xuất Excel KSK.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Lỗi Export Excel KSK",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshView()
    {
        _recordsView =
            CollectionViewSource
                .GetDefaultView(_records);

        _recordsView.Filter =
            FilterRecord;

        Grid.ItemsSource =
            _recordsView;

        _recordsView.Refresh();

        UpdateVisibleCount();
        UpdateHeaderCheckState();
    }

    private bool FilterRecord(object obj)
    {
        if (obj is not GkskRecord r)
            return false;

        var keyword =
            (SearchBox?.Text ?? "")
            .Trim();

        bool showSent =
            ShowSentCheck?.IsChecked == true;

        // Checkbox Đã gửi:
        // - Checked  -> chỉ hiển thị hồ sơ đã gửi.
        // - Unchecked -> chỉ hiển thị hồ sơ chưa gửi.
        if (showSent != r.IsSent)
            return false;

        if (string.IsNullOrWhiteSpace(keyword))
            return true;

        return MatchesGlobalSearch(
            r,
            keyword);
    }

    private static bool MatchesGlobalSearch(
        GkskRecord r,
        string keyword)
    {
        string[] values =
        [
            r.ServiceName,
            r.CreateDate?.ToString("dd/MM/yyyy HH:mm") ?? "",
            r.Paid?.ToString() ?? "",
            r.SO,
            r.HANGBANGLAI,
            r.HOTEN,
            r.GIOITINHVAL,
            r.NGAYSINH,
            r.SOCMND_PASSPORT,
            r.NGAYTHANGNAMCAPCMND,
            r.NOICAP,
            r.DIACHITHUONGTRU,
            r.MATINH_THUONGTRU,
            r.MAXA_THUONGTRU,
            r.IDBENHVIEN,
            r.BENHVIEN,
            r.MATUY,
            r.NGAYKETLUAN,
            r.BACSYKETLUAN,
            r.KETLUAN,
            r.ValidationStatus,
            r.ErrorMessage,
            r.SendStatus,
            r.ApiMessage,
            r.UUID
        ];

        return values.Any(
            v =>
                (v ?? "")
                .Contains(
                    keyword,
                    StringComparison.CurrentCultureIgnoreCase));
    }

    private void SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        // Không lọc ngay khi gõ; chỉ lọc khi bấm nút Tìm kiếm hoặc nhấn Enter.
    }

    private void ShowSentCheck_Changed(
        object sender,
        RoutedEventArgs e)
    {
        _recordsView?.Refresh();
        UpdateVisibleCount();
        UpdateHeaderCheckState();
    }

    private void Search_Click(
        object sender,
        RoutedEventArgs e)
    {
        _recordsView?.Refresh();

        UpdateVisibleCount();
        UpdateHeaderCheckState();
    }

    private void SearchBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        _recordsView?.Refresh();

        UpdateVisibleCount();
        UpdateHeaderCheckState();
    }

    private void ClearSearch_Click(
        object sender,
        RoutedEventArgs e)
    {
        SearchBox.Text = "";

        _recordsView?.Refresh();

        UpdateVisibleCount();
        UpdateHeaderCheckState();
    }

    private void SelectAllHeaderCheck_Click(
        object sender,
        RoutedEventArgs e)
    {
        CommitGridChanges();

        var visibleRecords =
            Grid.Items
                .OfType<GkskRecord>()
                .ToList();

        bool selectAll =
            SelectAllHeaderCheck.IsChecked == true;

        foreach (var item in visibleRecords)
            item.IsSelected = selectAll;

        Grid.Items.Refresh();

        UpdateHeaderCheckState();

        StatusText.Text =
            selectAll
                ? $"Đã chọn {visibleRecords.Count} hồ sơ đang hiển thị."
                : $"Đã bỏ chọn {visibleRecords.Count} hồ sơ đang hiển thị.";
    }

    private void Records_CollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SubscribeRecordSelectionChanges();
        UpdateHeaderCheckState();
    }

    private void SubscribeRecordSelectionChanges()
    {
        foreach (var record in _records)
        {
            record.PropertyChanged -= Record_PropertyChanged;
            record.PropertyChanged += Record_PropertyChanged;
        }
    }

    private void Record_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GkskRecord.IsSelected))
            UpdateHeaderCheckState();
    }

    private void UpdateHeaderCheckState()
    {
        if (SelectAllHeaderCheck is null)
            return;

        var visibleRecords =
            Grid.Items
                .OfType<GkskRecord>()
                .ToList();

        if (visibleRecords.Count == 0)
        {
            SelectAllHeaderCheck.IsChecked = false;
            return;
        }

        int selectedCount =
            visibleRecords
                .Count(x => x.IsSelected);

        if (selectedCount == 0)
        {
            SelectAllHeaderCheck.IsChecked =
                false;
        }
        else if (selectedCount == visibleRecords.Count)
        {
            SelectAllHeaderCheck.IsChecked =
                true;
        }
        else
        {
            SelectAllHeaderCheck.IsChecked =
                null;
        }
    }

    private void UpdateVisibleCount()
    {
        var visible =
            Grid.Items
                .OfType<GkskRecord>()
                .Count();

        StatusText.Text =
            $"Đang hiển thị {visible} / {_records.Count} hồ sơ.";
    }

    private void CommitGridChanges()
    {
        Grid.CommitEdit(
            DataGridEditingUnit.Cell,
            true);

        Grid.CommitEdit(
            DataGridEditingUnit.Row,
            true);
    }

    private void ShowDriverKskScreen_Click(
        object sender,
        RoutedEventArgs e)
    {
        _currentKskMode = "DRIVER";

        _selectedPackages =
            _config.LoadSelectedPackages(
                _currentKskMode);

        MainScreenTabs.SelectedIndex = 0;
        UpdateNavigationState(0);
    }

    private void ShowGeneralKskScreen_Click(
        object sender,
        RoutedEventArgs e)
    {
        _currentKskMode = "GENERAL";

        _selectedPackages =
            _config.LoadSelectedPackages(
                _currentKskMode);

        MainScreenTabs.SelectedIndex = 1;
        UpdateNavigationState(1);
    }

    private void ShowXmlScreen_Click(
        object sender,
        RoutedEventArgs e)
    {
        MainScreenTabs.SelectedIndex = 2;
        UpdateNavigationState(2);
    }

    private void UpdateNavigationState(int screenIndex)
    {
        if (NavKskButton is null ||
            NavXmlButton is null ||
            NavSettingsButton is null ||
            FeatureTitleText is null ||
            FeatureSubtitleText is null)
        {
            return;
        }

        NavKskButton.Style =
            (Style)FindResource(
                screenIndex is 0 or 1
                    ? "SidebarButtonActive"
                    : "SidebarButton");

        NavXmlButton.Style =
            (Style)FindResource(
                screenIndex == 2
                    ? "SidebarButtonActive"
                    : "SidebarButton");

        NavSettingsButton.Style =
            (Style)FindResource(
                screenIndex == 3
                    ? "SidebarButtonActive"
                    : "SidebarButton");

        DriverModeButton.Style =
            (Style)FindResource(
                screenIndex == 0
                    ? "ModeButtonActive"
                    : "ModeButton");

        GeneralModeButton.Style =
            (Style)FindResource(
                screenIndex == 1
                    ? "ModeButtonActive"
                    : "ModeButton");

        if (screenIndex is 0 or 1)
        {
            FeatureTitleText.Text =
                "LIÊN THÔNG KHÁM SỨC KHỎE";

            FeatureSubtitleText.Text =
                screenIndex == 0
                    ? "Khám sức khỏe lái xe - lấy dữ liệu, kiểm tra và đẩy cổng giám định"
                    : "Khám sức khỏe - quản lý và xuất dữ liệu hồ sơ";
        }
        else if (screenIndex == 2)
        {
            FeatureTitleText.Text =
                "DỮ LIỆU XML";

            FeatureSubtitleText.Text =
                "Tra cứu, kiểm tra và xuất dữ liệu XML";
        }
        else
        {
            FeatureTitleText.Text =
                "CẤU HÌNH HỆ THỐNG";

            FeatureSubtitleText.Text =
                "Thiết lập kết nối, tài khoản và giá trị mặc định";
        }
    }

    private void DownloadImportSample_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var sourcePath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Mau_Import_GKSK_Lai_Xe.xlsx");

            if (!File.Exists(sourcePath))
            {
                MessageBox.Show(
                    "Không tìm thấy file import mẫu đi kèm ứng dụng.",
                    "Tải file mẫu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog =
                new SaveFileDialog
                {
                    Filter = "Excel (*.xlsx)|*.xlsx",
                    FileName = "Mau_Import_GKSK_Lai_Xe.xlsx"
                };

            if (dialog.ShowDialog() != true)
                return;

            File.Copy(
                sourcePath,
                dialog.FileName,
                overwrite: true);

            StatusText.Text =
                "Đã tải file import mẫu.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Lỗi tải file mẫu",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


}
