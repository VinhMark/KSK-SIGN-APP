using System.Data;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using GKSKLaiXe.Services;
using Microsoft.Win32;

namespace GKSKLaiXe.Windows;

public partial class XmlDataView : UserControl
{
    private readonly ConfigService _config = new();
    private readonly XmlDataService _data = new();

    private DataTable _xml1 = new();
    private DataTable _xml2 = new();
    private DataTable _xml3 = new();

    public XmlDataView()
    {
        InitializeComponent();

        var today = DateTime.Today;

        DayPicker.SelectedDate = today;
        FromDatePicker.SelectedDate = today;
        ToDatePicker.SelectedDate = today;

        for (var month = 1; month <= 12; month++)
        {
            MonthCombo.Items.Add(month);
        }

        for (var year = today.Year - 10;
             year <= today.Year + 1;
             year++)
        {
            MonthYearCombo.Items.Add(year);
            QuarterYearCombo.Items.Add(year);
            YearCombo.Items.Add(year);
        }

        MonthCombo.SelectedItem = today.Month;
        MonthYearCombo.SelectedItem = today.Year;
        QuarterYearCombo.SelectedItem = today.Year;
        YearCombo.SelectedItem = today.Year;
        QuarterCombo.SelectedIndex = ((today.Month - 1) / 3);
    }

    private void FilterTypeCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        var tag =
            (FilterTypeCombo.SelectedItem as ComboBoxItem)
            ?.Tag?.ToString()
            ?? "RANGE";

        DayPanel.Visibility =
            tag == "DAY"
                ? Visibility.Visible
                : Visibility.Collapsed;

        RangePanel.Visibility =
            tag == "RANGE"
                ? Visibility.Visible
                : Visibility.Collapsed;

        MonthPanel.Visibility =
            tag == "MONTH"
                ? Visibility.Visible
                : Visibility.Collapsed;

        QuarterPanel.Visibility =
            tag == "QUARTER"
                ? Visibility.Visible
                : Visibility.Collapsed;

        YearPanel.Visibility =
            tag == "YEAR"
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private (DateTime From, DateTime To) ResolveRange()
    {
        var tag =
            (FilterTypeCombo.SelectedItem as ComboBoxItem)
            ?.Tag?.ToString()
            ?? "RANGE";

        if (tag == "DAY")
        {
            var d =
                DayPicker.SelectedDate
                ?? DateTime.Today;

            return (d.Date, d.Date);
        }


        if (tag == "MONTH")
        {
            var month =
                MonthCombo.SelectedItem is int m
                    ? m
                    : DateTime.Today.Month;

            var year =
                MonthYearCombo.SelectedItem is int y
                    ? y
                    : DateTime.Today.Year;

            var from =
                new DateTime(year, month, 1);

            return (
                from,
                from.AddMonths(1).AddDays(-1));
        }

        if (tag == "QUARTER")
        {
            var quarter =
                QuarterCombo.SelectedIndex + 1;

            var year =
                QuarterYearCombo.SelectedItem is int y
                    ? y
                    : DateTime.Today.Year;

            var from =
                new DateTime(
                    year,
                    ((quarter - 1) * 3) + 1,
                    1);

            return (
                from,
                from.AddMonths(3).AddDays(-1));
        }

        if (tag == "YEAR")
        {
            var year =
                YearCombo.SelectedItem is int y
                    ? y
                    : DateTime.Today.Year;

            return (
                new DateTime(year, 1, 1),
                new DateTime(year, 12, 31));
        }

        var fromDate =
            FromDatePicker.SelectedDate
            ?? DateTime.Today;

        var toDate =
            ToDatePicker.SelectedDate
            ?? fromDate;

        if (toDate < fromDate)
            throw new InvalidOperationException(
                "Đến ngày không được nhỏ hơn Từ ngày.");

        return (
            fromDate.Date,
            toDate.Date);
    }

    private async void LoadData_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadXmlButton.IsEnabled = false;

        try
        {
            var settings =
                _config.LoadSql();

            var range =
                ResolveRange();

            var maLk =
                string.IsNullOrWhiteSpace(MaLkBox.Text)
                    ? null
                    : MaLkBox.Text.Trim();

            XmlStatusText.Text =
                $"Đang tải dữ liệu {range.From:dd/MM/yyyy} - {range.To:dd/MM/yyyy}...";

            // Một kết nối + một SQL batch, trả về 3 ResultSet.
            // Tập XML1 được xác định một lần trong #XML1_SELECTED;
            // XML2/XML3 chỉ join theo ReportID của tập này.
            var result =
                await _data.LoadAllXmlAsync(
                    settings,
                    range.From,
                    range.To,
                    maLk);

            _xml1 = result.Xml1;
            _xml2 = result.Xml2;
            _xml3 = result.Xml3;

            Xml1Grid.ItemsSource =
                _xml1.DefaultView;

            Xml2Grid.ItemsSource =
                _xml2.DefaultView;

            Xml3Grid.ItemsSource =
                _xml3.DefaultView;

            UpdateSummary();

            XmlStatusText.Text =
                $"XML1: {_xml1.Rows.Count} | " +
                $"XML2: {_xml2.Rows.Count} | " +
                $"XML3: {_xml3.Rows.Count}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Lỗi dữ liệu XML",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            XmlStatusText.Text =
                "Tải dữ liệu thất bại.";
        }
        finally
        {
            LoadXmlButton.IsEnabled = true;
        }
    }

    private static readonly HashSet<string> HiddenXmlColumns =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "MA_HUYEN",
            "PatientReceiveID"
        };

    private static readonly HashSet<string> MoneyXmlColumns =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "T_TONGCHI_BV",
            "T_TONGCHI_BH",
            "T_BHTT",
            "TBNTT",
            "T_BNTT",
            "DON_GIA_BH",
            "DON_GIA_BV",
            "THANH_TIEN_BV"
        };

    private void XmlGrid_AutoGeneratingColumn(
        object sender,
        DataGridAutoGeneratingColumnEventArgs e)
    {
        var columnName =
            e.PropertyName ?? "";

        if (HiddenXmlColumns.Contains(columnName))
        {
            e.Cancel = true;
            return;
        }

        if (e.Column is DataGridBoundColumn boundColumn)
        {
            boundColumn.ElementStyle =
                (Style)FindResource("CenteredDataGridText");

            boundColumn.EditingElementStyle =
                (Style)FindResource("CenteredDataGridEditingText");

            if (MoneyXmlColumns.Contains(columnName) &&
                boundColumn.Binding is System.Windows.Data.Binding binding)
            {
                binding.StringFormat =
                    "#,###.00";
            }
        }

        e.Column.Header =
            columnName switch
            {
                "GIOI_TINH" => "Giới tính",
                "TEN_BAC_SI" => "Tên bác sĩ",
                "TEN_KHOA" => "Tên khoa",
                "THANG_QT" => "Tháng QT",
                _ => e.Column.Header
            };
    }


    private void UpdateSummary()
    {
        // Tổng hồ sơ tính theo XML1 để không bị nhân theo số dòng thuốc/DVKT.
        TotalRecordsText.Text =
            _xml1.Rows.Count.ToString("N0");

        decimal totalCost = SumColumn(_xml1, "T_TONGCHI_BV");
        decimal bhytPay = SumColumn(_xml1, "T_BHTT");
        decimal patientPay = SumColumn(_xml1, "TBNTT");

        TotalCostText.Text =
            totalCost.ToString("N0");

        BhytPayText.Text =
            bhytPay.ToString("N0");

        PatientPayText.Text =
            patientPay.ToString("N0");
    }

    private static decimal SumColumn(
        DataTable table,
        string columnName)
    {
        if (!table.Columns.Contains(columnName))
            return 0;

        decimal total = 0;

        foreach (DataRow row in table.Rows)
        {
            if (row[columnName] == DBNull.Value)
                continue;

            if (decimal.TryParse(
                    row[columnName].ToString(),
                    out var value))
            {
                total += value;
            }
        }

        return total;
    }

    private void ExportExcel_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_xml1.Rows.Count == 0 &&
            _xml2.Rows.Count == 0 &&
            _xml3.Rows.Count == 0)
        {
            MessageBox.Show(
                "Chưa có dữ liệu để xuất.");

            return;
        }

        var dlg =
            new SaveFileDialog
            {
                Filter =
                    "Excel Workbook (*.xlsx)|*.xlsx",
                FileName =
                    $"BHYT_XML_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

        if (dlg.ShowDialog() != true)
            return;

        using var wb =
            new XLWorkbook();

        wb.Worksheets.Add(_xml1, "XML1");
        wb.Worksheets.Add(_xml2, "XML2");
        wb.Worksheets.Add(_xml3, "XML3");

        foreach (var ws in wb.Worksheets)
        {
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(1, 80);

            // Excel width uses character units; cap around 300px.
            foreach (var column in ws.ColumnsUsed())
            {
                if (column.Width > 42)
                    column.Width = 42;
            }

            // Center all used cells vertically.
            var usedRange = ws.RangeUsed();
            if (usedRange is not null)
            {
                usedRange.Style.Alignment.Vertical =
                    XLAlignmentVerticalValues.Center;
            }

            // Định dạng các cột tiền theo #,###.00.
            foreach (var headerCell in ws.Row(1).CellsUsed())
            {
                var header =
                    headerCell.GetString();

                if (MoneyXmlColumns.Contains(header))
                {
                    ws.Column(
                        headerCell.Address.ColumnNumber)
                        .Style.NumberFormat.Format =
                        "#,###.00";
                }
            }
        }

        wb.SaveAs(dlg.FileName);

        XmlStatusText.Text =
            "Đã xuất Excel.";
    }
}
