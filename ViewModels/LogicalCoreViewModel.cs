using CommunityToolkit.Mvvm.ComponentModel;

namespace ParkToggleWpf.ViewModels;

public partial class LogicalCoreViewModel : ObservableObject
{
    [ObservableProperty]
    private string _coreName;

    [ObservableProperty]
    private bool _isParked;

    public LogicalCoreViewModel(string name, bool isParked)
    {
        _coreName = name;
        _isParked = isParked;
    }
}
