using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using GKSKLaiXe.Models;

namespace GKSKLaiXe.Windows;

public partial class PackageSelectionWindow : Window
{
    public ObservableCollection<PackageOption> Packages { get; }

    private readonly ICollectionView _packageView;

    public IReadOnlyList<string> SelectedPackages =>
        Packages
            .Where(x => x.IsSelected)
            .Select(x => x.Name)
            .ToList();

    public PackageSelectionWindow(
        IEnumerable<string> packageNames,
        IEnumerable<string> selectedPackages,
        string mode)
    {
        InitializeComponent();

        Title =
            string.Equals(
                mode,
                "DRIVER",
                StringComparison.OrdinalIgnoreCase)
                ? "Chọn gói - KSK LÁI XE"
                : "Chọn gói - KSK";

        var selected =
            new HashSet<string>(
                selectedPackages,
                StringComparer.OrdinalIgnoreCase);

        Packages =
            new ObservableCollection<PackageOption>(
                packageNames
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Where(
                        name =>
                            string.Equals(
                                mode,
                                "DRIVER",
                                StringComparison.OrdinalIgnoreCase)
                                ? name.Contains(
                                    "KSK LÁI XE",
                                    StringComparison.CurrentCultureIgnoreCase)
                                : !name.Contains(
                                    "LÁI XE",
                                    StringComparison.CurrentCultureIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .Select(
                        name =>
                            new PackageOption
                            {
                                Name = name,
                                IsSelected = selected.Contains(name)
                            }));

        foreach (var item in Packages)
            item.PropertyChanged += Package_PropertyChanged;

        _packageView =
            CollectionViewSource.GetDefaultView(Packages);

        _packageView.Filter =
            FilterPackage;

        PackageList.ItemsSource =
            _packageView;

        UpdateSelectedCount();
    }

    private bool FilterPackage(object obj)
    {
        if (obj is not PackageOption item)
            return false;

        var keyword =
            (PackageSearchBox.Text ?? "")
            .Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return true;

        return item.Name.Contains(
            keyword,
            StringComparison.CurrentCultureIgnoreCase);
    }

    private void PackageSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _packageView.Refresh();
    }

    private void SelectAll_Click(
        object sender,
        RoutedEventArgs e)
    {
        foreach (var item in _packageView.Cast<PackageOption>())
            item.IsSelected = true;

        UpdateSelectedCount();
    }

    private void ClearAll_Click(
        object sender,
        RoutedEventArgs e)
    {
        foreach (var item in _packageView.Cast<PackageOption>())
            item.IsSelected = false;

        UpdateSelectedCount();
    }

    private void Package_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageOption.IsSelected))
            UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedCountText.Text =
            $"Đã chọn: {Packages.Count(x => x.IsSelected)}";
    }

    private void Apply_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!Packages.Any(x => x.IsSelected))
        {
            MessageBox.Show(
                "Vui lòng chọn ít nhất một gói.",
                "Chọn gói",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
