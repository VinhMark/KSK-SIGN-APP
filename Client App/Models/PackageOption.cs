using System.ComponentModel;

namespace GKSKLaiXe.Models;

public sealed class PackageOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
