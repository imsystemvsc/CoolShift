using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ParkToggleWpf.Monitoring;

namespace ParkToggleWpf;

public partial class MonitoringSettingsWindow : Window
{
    private string _searchText = string.Empty;

    public MonitoringSettingsWindow(ObservableCollection<SensorSelectionViewModel> sensors)
    {
        InitializeComponent();
        Sensors = sensors;
        DataContext = this;

        Sensors.CollectionChanged += SensorsOnCollectionChanged;
        Closed += OnClosed;
        SourceInitialized += OnSourceInitialized;

        RebuildTree();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        
        int corner = 2; // Round
        DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
    }

    public ObservableCollection<SensorSelectionViewModel> Sensors { get; }

    public ObservableCollection<MonitoringTreeNode> SensorTreeNodes { get; } = new();

    private void SensorsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(RebuildTree);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text.Trim();
        RebuildTree();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Sensors.CollectionChanged -= SensorsOnCollectionChanged;
    }

    private void RebuildTree()
    {
        var filtered = string.IsNullOrWhiteSpace(_searchText)
            ? Sensors
            : Sensors.Where(MatchesSearch);

        var nodes = MonitoringTreeBuilder.Build(filtered);

        SensorTreeNodes.Clear();
        foreach (var node in nodes)
        {
            SensorTreeNodes.Add(node);
        }

        EmptyTreeMessage.Visibility = SensorTreeNodes.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool MatchesSearch(SensorSelectionViewModel sensor)
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        return sensor.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || sensor.HardwareDisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || sensor.GroupDisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }
}
