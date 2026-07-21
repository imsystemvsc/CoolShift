using System.ComponentModel;
using System.Runtime.CompilerServices;
using LibreHardwareMonitor.Hardware;

namespace ParkToggleWpf.Monitoring;

public sealed class SensorSelectionViewModel : INotifyPropertyChanged
{
    public const int UnsetSortOrder = int.MaxValue;

    private bool _isSelected;
    private double? _currentValue;
    private double? _minValue;
    private double? _maxValue;
    private int _sortOrder = UnsetSortOrder;

    public SensorSelectionViewModel(
        string sensorId,
        string displayName,
        string? unit,
        string category,
        int categoryOrder,
        string hardwareId,
        string hardwareName,
        HardwareType hardwareType,
        SensorType sensorType,
        string hardwareDisplayName,
        string groupDisplayName,
        string sensorDisplayName)
    {
        SensorId = sensorId;
        DisplayName = displayName;
        Unit = unit;
        Category = category;
        CategoryOrder = categoryOrder;
        HardwareId = hardwareId;
        HardwareName = hardwareName;
        HardwareType = hardwareType;
        SensorType = sensorType;
        HardwareDisplayName = hardwareDisplayName;
        GroupDisplayName = groupDisplayName;
        SensorDisplayName = sensorDisplayName;
    }

    public string SensorId { get; }
    public string DisplayName { get; }
    public string? Unit { get; }
    public string Category { get; }
    public int CategoryOrder { get; }
    public string HardwareId { get; }
    public string HardwareName { get; }
    public HardwareType HardwareType { get; }
    public SensorType SensorType { get; }
    public string HardwareDisplayName { get; }
    public string GroupDisplayName { get; }
    public string SensorDisplayName { get; }

    public int SortOrder
    {
        get => _sortOrder;
        private set
        {
            if (_sortOrder == value)
            {
                return;
            }

            _sortOrder = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public double? CurrentValue
    {
        get => _currentValue;
        private set
        {
            if (_currentValue == value)
            {
                return;
            }

            _currentValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentDisplay));
        }
    }

    public double? MinValue
    {
        get => _minValue;
        private set
        {
            if (_minValue == value)
            {
                return;
            }

            _minValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MinDisplay));
        }
    }

    public double? MaxValue
    {
        get => _maxValue;
        private set
        {
            if (_maxValue == value)
            {
                return;
            }

            _maxValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaxDisplay));
        }
    }

    public string CurrentDisplay => FormatValue(CurrentValue);
    public string MinDisplay => FormatValue(MinValue);
    public string MaxDisplay => FormatValue(MaxValue);

    public bool HasProgress => SensorType == SensorType.Load || SensorType == SensorType.Temperature || SensorType == SensorType.Control || SensorType == SensorType.Level;

    public double ProgressPercent
    {
        get
        {
            if (!CurrentValue.HasValue) return 0;
            return Math.Min(100.0, Math.Max(0.0, CurrentValue.Value));
        }
    }

    public void SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
    }

    public void UpdateReading(double? value)
    {
        if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
        {
            value = null;
        }

        CurrentValue = value;
        OnPropertyChanged(nameof(ProgressPercent));

        if (!value.HasValue)
        {
            return;
        }

        if (!MinValue.HasValue || value.Value < MinValue.Value)
        {
            MinValue = value;
        }

        if (!MaxValue.HasValue || value.Value > MaxValue.Value)
        {
            MaxValue = value;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string FormatValue(double? value)
    {
        if (!value.HasValue)
        {
            return "--";
        }

        var formatted = value.Value.ToString("0.0");
        return string.IsNullOrWhiteSpace(Unit) ? formatted : $"{formatted} {Unit}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
