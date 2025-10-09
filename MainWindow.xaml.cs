using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ParkToggleWpf.Monitoring;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;
using DrawingIcon = System.Drawing.Icon;
using DiagnosticsProcess = System.Diagnostics.Process;
using Brush = System.Windows.Media.Brush;
using InputCursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;

using Style = System.Windows.Style;

namespace ParkToggleWpf;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly PowerPlanService _service = new();
    private CancellationTokenSource? _refreshCts;
    private bool _isPopulatingPlans;

    private NotifyIcon? _trayIcon;
    private bool _isExitRequested;
    private bool _hasShownTrayTip;

    private byte[]? _iconDefaultBytes;
    private byte[]? _iconAlwaysOnBytes;
    private byte[]? _iconCoolIdleBytes;

    private ToolStripMenuItem? _trayCurrentModeItem;
    private ToolStripMenuItem? _trayToggleItem;
    private ToolStripMenuItem? _trayShowWindowItem;
    private ToolStripMenuItem? _trayShowLogItem;

    private CpuTemperatureService? _cpuTemperatureService;
    private DispatcherTimer? _cpuTimer;

    private MonitoringOptions? _monitoringOptions;
    private MonitoringRepository? _monitoringRepository;
    private HardwareMonitorService? _hardwareMonitorService;
    private MonitoringManager? _monitoringManager;
    private readonly ObservableCollection<SensorSelectionViewModel> _monitoringSensors = new();
    private ICollectionView? _monitoringSensorsView;
    private readonly Dictionary<string, SensorSelectionViewModel> _sensorLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SensorPreferenceState> _storedSensorPreferences = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Point? _dragStartPoint;
    private SensorSelectionViewModel? _draggedSensor;
    private bool _suppressPreferencePersistence;
    private bool _monitoringInitialized;

    private static readonly TimeSpan BringToFrontDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan CpuPollingInterval = TimeSpan.FromMilliseconds(2000);

    public ObservableCollection<SensorSelectionViewModel> MonitoringSensors => _monitoringSensors;

    public ICollectionView MonitoringSensorsView => _monitoringSensorsView ??= CreateMonitoringSensorsView();


    public event PropertyChangedEventHandler? PropertyChanged;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _monitoringSensors.CollectionChanged += MonitoringSensorsOnCollectionChanged;
        InitializeTrayIcon();

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        StateChanged += OnWindowStateChanged;
        Closing += OnWindowClosing;

        try
        {
            _cpuTemperatureService = new CpuTemperatureService();
            _cpuTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = CpuPollingInterval
            };
            _cpuTimer.Tick += (_, _) => UpdateCpuTemperatures();
        }
        catch
        {
            _cpuTemperatureService = null;
            _cpuTimer = null;
            if (PackageTempText != null)
            {
                PackageTempText.Text = "Unavailable";
            }
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_cpuTemperatureService is not null)
        {
            UpdateCpuTemperatures();
            _cpuTimer?.Start();
        }

        await RefreshAsync().ConfigureAwait(false);
        await InitializeMonitoringAsync().ConfigureAwait(false);
        await BringToFrontAsync().ConfigureAwait(false);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyMicaBackdrop();
    }

    private async Task BringToFrontAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            Topmost = true;
            Activate();
            Focus();
        }, DispatcherPriority.Loaded);

        await Task.Delay(BringToFrontDelay).ConfigureAwait(false);

        await Dispatcher.InvokeAsync(() => Topmost = false, DispatcherPriority.Loaded);
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon != null)
        {
            return;
        }

        EnsureTrayIconAssets();

        var notifyIcon = new NotifyIcon
        {
            Text = "Park Toggle",
            Visible = true
        };

        var initialIcon = CreateIconFromBytes(_iconDefaultBytes) ?? (DrawingIcon)System.Drawing.SystemIcons.Application.Clone();
        notifyIcon.Icon = initialIcon;

        notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleWindowVisibilityFromTray);

        notifyIcon.ContextMenuStrip = CreateTrayMenu();

        _trayIcon = notifyIcon;
    }

    private ContextMenuStrip CreateTrayMenu()
    {
        var menu = new ContextMenuStrip();

        _trayCurrentModeItem = new ToolStripMenuItem("Current Mode: --")
        {
            Enabled = false
        };
        menu.Items.Add(_trayCurrentModeItem);
        menu.Items.Add(new ToolStripSeparator());

        _trayToggleItem = new ToolStripMenuItem("Switch Mode");
        _trayToggleItem.Click += (_, _) => _ = Dispatcher.InvokeAsync(async () => await ToggleModeFromTrayAsync());
        menu.Items.Add(_trayToggleItem);

        menu.Items.Add(new ToolStripSeparator());

        _trayShowWindowItem = new ToolStripMenuItem("Show Window");
        _trayShowWindowItem.Click += (_, _) => Dispatcher.Invoke(ToggleWindowVisibilityFromTray);
        menu.Items.Add(_trayShowWindowItem);

        UpdateTrayWindowMenuText(IsVisible && WindowState != WindowState.Minimized);

        _trayShowLogItem = new ToolStripMenuItem("Open Log");
        _trayShowLogItem.Click += (_, _) => _ = Dispatcher.InvokeAsync(OpenLogFromTrayAsync);
        menu.Items.Add(_trayShowLogItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitFromTray);
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && !_isExitRequested)
        {
            Hide();
            ShowInTaskbar = false;
            UpdateTrayWindowMenuText(false);

            if (!_hasShownTrayTip)
            {
                _trayIcon?.ShowBalloonTip(1500, "Park Toggle", "Park Toggle is still running in the background.", ToolTipIcon.Info);
                _hasShownTrayTip = true;
            }
        }
        else if (WindowState == WindowState.Normal)
        {
            ShowInTaskbar = true;
            UpdateTrayWindowMenuText(true);
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExitRequested)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            UpdateTrayWindowMenuText(false);
            _trayIcon?.ShowBalloonTip(1000, "Park Toggle", "Double-click the tray icon to reopen.", ToolTipIcon.Info);
            return;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshCts?.Cancel();
        _trayIcon?.Dispose();
        _cpuTimer?.Stop();
        _cpuTemperatureService?.Dispose();
        _cpuTimer = null;
        _cpuTemperatureService = null;

        if (_monitoringManager is not null)
        {
            _monitoringManager.SampleCaptured -= OnMonitoringSampleCaptured;
            _monitoringManager.Dispose();
            _monitoringManager = null;
        }

        _hardwareMonitorService = null;
        _monitoringRepository = null;
        _monitoringOptions = null;
        _monitoringInitialized = false;
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
        UpdateTrayWindowMenuText(true);
    }

    private void ExitFromTray()
    {
        _isExitRequested = true;
        Close();
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlanCombo.SelectedItem is not PowerPlan plan)
        {
            return;
        }

        await ExecuteWithBusyAsync(async () =>
        {
            await _service.ToggleModeAsync(plan.Guid, plan.Name).ConfigureAwait(false);
            await RefreshAsync(manageBusy: false).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    private async void PlanCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingPlans || PlanCombo.SelectedItem is not PowerPlan plan)
        {
            return;
        }

        await ExecuteWithBusyAsync(async () =>
        {
            await _service.SetActivePlanAsync(plan.Guid, plan.Name).ConfigureAwait(false);
            await RefreshAsync(manageBusy: false).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task ToggleModeFromTrayAsync()
    {
        if (PlanCombo.SelectedItem is not PowerPlan plan)
        {
            return;
        }

        await ExecuteWithBusyAsync(async () =>
        {
            await _service.ToggleModeAsync(plan.Guid, plan.Name).ConfigureAwait(false);
            await RefreshAsync(manageBusy: false).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private Task OpenLogFromTrayAsync()
    {
        try
        {
            var logPath = _service.LogPath;
            if (!File.Exists(logPath))
            {
                File.WriteAllText(logPath, string.Empty);
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            };

            DiagnosticsProcess.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Unable to open the log file.{Environment.NewLine}{ex.Message}",
                "Park Toggle",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private void ToggleWindowVisibilityFromTray()
    {
        if (_trayShowWindowItem is null)
        {
            return;
        }

        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
            UpdateTrayWindowMenuText(false);
        }
        else
        {
            RestoreFromTray();
        }
    }

    private void UpdateTrayWindowMenuText(bool isVisible)
    {
        if (_trayShowWindowItem is null)
        {
            return;
        }

        _trayShowWindowItem.Text = isVisible ? "Hide Window" : "Show Window";
    }

    private void UpdateCpuTemperatures()
    {
        if (_cpuTemperatureService is null)
        {
            PackageTempText.Text = "N/A";
            return;
        }

        CpuTemperatureSnapshot snapshot;
        try
        {
            snapshot = _cpuTemperatureService.GetSnapshot();
        }
        catch
        {
            snapshot = CpuTemperatureSnapshot.Empty;
        }

        PackageTempText.Text = FormatTemperature(snapshot.PackageCelsius);
    }

    private static string FormatTemperature(double? value)
    {
        return value.HasValue
            ? $"{value.Value.ToString("F1", CultureInfo.InvariantCulture)} \u00B0C"
            : "N/A";
    }

    private async Task ExecuteWithBusyAsync(Func<Task> action)
    {
        await Dispatcher.InvokeAsync(() => SetBusyState(true), DispatcherPriority.Normal);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ShowError("Operation failed.", ex), DispatcherPriority.Normal);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => SetBusyState(false), DispatcherPriority.Normal);
        }
    }

    private async Task RefreshAsync(bool manageBusy = true, CancellationToken token = default)
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _refreshCts = linkedCts;

        if (manageBusy)
        {
            await Dispatcher.InvokeAsync(() => SetBusyState(true), DispatcherPriority.Normal);
        }

        try
        {
            var plans = await _service.GetPlansAsync(linkedCts.Token).ConfigureAwait(false);
            var planList = plans.ToList();
            var active = planList.FirstOrDefault(p => p.IsActive) ?? await _service.GetActivePlanAsync(linkedCts.Token).ConfigureAwait(false);

            if (linkedCts.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() => PopulatePlanCombo(planList, active.Guid), DispatcherPriority.Normal);

            var snapshot = await _service.GetModeSnapshotAsync(active.Guid, linkedCts.Token).ConfigureAwait(false);
            if (linkedCts.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                UpdateModeUi(snapshot);
                UpdateToggleUi(snapshot.Mode);
                UpdateDetails(snapshot);
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ShowError("Unable to refresh power settings.", ex), DispatcherPriority.Normal);
        }
        finally
        {
            if (manageBusy)
            {
                await Dispatcher.InvokeAsync(() => SetBusyState(false), DispatcherPriority.Normal);
            }

            if (_refreshCts == linkedCts)
            {
                _refreshCts = null;
            }

            linkedCts.Dispose();
        }
    }

    private void PopulatePlanCombo(IReadOnlyList<PowerPlan> plans, string activeGuid)
    {
        _isPopulatingPlans = true;
        try
        {
            var list = plans.ToList();
            PlanCombo.ItemsSource = list;
            PlanCombo.DisplayMemberPath = nameof(PowerPlan.Name);
            PlanCombo.SelectedValuePath = nameof(PowerPlan.Guid);

            PlanCombo.SelectedValue = activeGuid;
            var selected = list.FirstOrDefault(p => string.Equals(p.Guid, activeGuid, StringComparison.OrdinalIgnoreCase))
                           ?? list.FirstOrDefault();
            PlanCombo.SelectedItem = selected;
        }
        finally
        {
            _isPopulatingPlans = false;
        }
    }

    private void UpdateModeUi(ModeSnapshot snapshot)
    {
        ModeValueText.Text = PowerPlanService.ModeToDisplay(snapshot.Mode);
        Brush accentBrush = (Brush)FindResource("AccentBrush");
        Brush alwaysOnBrush = (Brush)FindResource("AlwaysOnBrush");
        Brush defaultBrush = (Brush)FindResource("ForegroundBrush");

        ModeValueText.Foreground = snapshot.Mode switch
        {
            ParkMode.AlwaysOn => alwaysOnBrush,
            ParkMode.CoolIdle => accentBrush,
            _ => defaultBrush
        };
    }

    private void UpdateToggleUi(ParkMode mode)
    {
        ToggleButton.Style = mode == ParkMode.CoolIdle
            ? (Style)FindResource("WarningButtonStyle")
            : (Style)FindResource("PrimaryButtonStyle");

        ToggleButton.Content = mode == ParkMode.CoolIdle
            ? "Switch to Always-On"
            : "Switch to Cool Idle";

        UpdateTrayMenuForMode(mode);
        UpdateTrayIcon(mode);
    }

    private void UpdateDetails(ModeSnapshot snapshot)
    {
        CoreLabel.Text = $"Core Parking Min Cores (AC/DC): {snapshot.Core.Ac}/{snapshot.Core.Dc}";
        IdleLabel.Text = $"Idle State Max (AC/DC): {snapshot.Idle.Ac}/{snapshot.Idle.Dc}";
    }

    private void SetBusyState(bool isBusy)
    {
        ToggleButton.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        PlanCombo.IsEnabled = !isBusy && !_isPopulatingPlans;
        Mouse.OverrideCursor = isBusy ? InputCursors.AppStarting : null;
    }

    private void ShowError(string message, Exception exception)
    {
        MessageBox.Show(
            this,
            $"{message}{Environment.NewLine}{exception.Message}",
            "Park Toggle",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void UpdateTrayMenuForMode(ParkMode mode)
    {
        if (_trayCurrentModeItem is not null)
        {
            _trayCurrentModeItem.Text = $"Current Mode: {PowerPlanService.ModeToDisplay(mode)}";
        }

        if (_trayToggleItem is not null)
        {
            _trayToggleItem.Text = mode == ParkMode.CoolIdle
                ? "Switch to Always-On"
                : "Switch to Cool Idle";
        }
    }

    private void UpdateTrayIcon(ParkMode mode)
    {
        if (_trayIcon is null)
        {
            return;
        }

        EnsureTrayIconAssets();

        DrawingIcon? nextIcon = mode switch
        {
            ParkMode.AlwaysOn => CreateIconFromBytes(_iconAlwaysOnBytes),
            ParkMode.CoolIdle => CreateIconFromBytes(_iconCoolIdleBytes),
            _ => CreateIconFromBytes(_iconDefaultBytes)
        };

        nextIcon ??= CreateIconFromBytes(_iconDefaultBytes) ?? (DrawingIcon)System.Drawing.SystemIcons.Application.Clone();

        var previous = _trayIcon.Icon;
        _trayIcon.Icon = nextIcon;
        previous?.Dispose();
    }

    private void EnsureTrayIconAssets()
    {
        _iconDefaultBytes ??= LoadIconBytes("main.ico");
        _iconAlwaysOnBytes ??= LoadIconBytes("lightningbolt.ico");
        _iconCoolIdleBytes ??= LoadIconBytes("snowflake.ico");
    }

    private static byte[]? LoadIconBytes(string fileName)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Resources/Icons/{fileName}", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo?.Stream is Stream stream)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch
        {
        }

        var filePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons", fileName);
        if (File.Exists(filePath))
        {
            return File.ReadAllBytes(filePath);
        }

        return null;
    }

    private static DrawingIcon? CreateIconFromBytes(byte[]? data)
    {
        if (data is null || data.Length == 0)
        {
            return null;
        }

        using var ms = new MemoryStream(data);
        return new DrawingIcon(ms);
    }

    private async Task InitializeMonitoringAsync()
    {
        if (_monitoringInitialized)
        {
            return;
        }

        try
        {
            _monitoringOptions = MonitoringOptions.CreateDefault();
            _monitoringRepository = new MonitoringRepository(_monitoringOptions);
            await LoadSensorSelectionPreferencesAsync().ConfigureAwait(false);
            _hardwareMonitorService = new HardwareMonitorService();
            _monitoringManager = new MonitoringManager(_hardwareMonitorService, _monitoringRepository, _monitoringOptions);
            _monitoringManager.SampleCaptured += OnMonitoringSampleCaptured;
            _monitoringManager.Start();
            _monitoringInitialized = true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Monitoring initialization failed: {ex}");
            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(this, "Monitoring is unavailable. Please check hardware monitor permissions.", "Monitoring", MessageBoxButton.OK, MessageBoxImage.Warning);
            }, DispatcherPriority.Background);
        }
    }

    private void OnMonitoringSampleCaptured(object? sender, MonitoringSample sample)
    {
        _ = Dispatcher.InvokeAsync(() => HandleMonitoringSample(sample), DispatcherPriority.Background);
    }

    private void HandleMonitoringSample(MonitoringSample sample)
    {
        if (!_monitoringInitialized)
        {
            return;
        }

        foreach (var sensor in sample.Samples)
        {
            var selection = EnsureSensorSelection(sensor);
            selection.UpdateReading(sensor.Value);
        }
    }

    private void PopulateSensorsSnapshot()
    {
        if (_hardwareMonitorService is null)
        {
            return;
        }

        try
        {
            var samples = _hardwareMonitorService.GetSamples();
            Trace.WriteLine($"Settings snapshot found {samples.Count} sensors.");
            foreach (var sample in samples)
            {
                EnsureSensorSelection(sample);
            }

            RefreshMonitoringSensorsView();
            Trace.WriteLine($"Monitoring sensor count: {_monitoringSensors.Count}.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to populate sensors snapshot: {ex}");
        }
    }

    private void ApplyStoredSelections()
    {
        if (_monitoringSensors.Count == 0)
        {
            return;
        }

        _suppressPreferencePersistence = true;
        try
        {
            foreach (var sensor in _monitoringSensors)
            {
                if (_storedSensorPreferences.TryGetValue(sensor.SensorId, out var preference))
                {
                    sensor.SetSortOrder(preference.SortOrder);
                    sensor.IsSelected = preference.IsSelected;
                }
                else
                {
                    sensor.IsSelected = false;
                }
            }
        }
        finally
        {
            _suppressPreferencePersistence = false;
        }

        RefreshMonitoringSensorsView();
    }

    private async Task LoadSensorSelectionPreferencesAsync()
    {
        _storedSensorPreferences.Clear();

        if (_monitoringRepository is null)
        {
            return;
        }

        try
        {
            var preferences = await _monitoringRepository
                .GetSensorPreferencesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            foreach (var preference in preferences)
            {
                var category = string.IsNullOrWhiteSpace(preference.Category)
                    ? string.Empty
                    : preference.Category;

                _storedSensorPreferences[preference.SensorId] = new SensorPreferenceState(
                    preference.SensorId,
                    preference.IsSelected,
                    category,
                    preference.SortOrder);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load sensor preferences: {ex}");
        }
    }
    private SensorSelectionViewModel EnsureSensorSelection(SensorSample sample)
    {
        if (_sensorLookup.TryGetValue(sample.SensorId, out var existing))
        {
            return existing;
        }

        var displayName = BuildDisplayName(sample);
        var segments = SensorDisplayNameFormatter.BuildSegments(sample);
        var categoryInfo = SensorDisplayNameFormatter.GetHardwareCategoryInfo(sample.HardwareType);

        _storedSensorPreferences.TryGetValue(sample.SensorId, out var storedPreference);

        var viewModel = new SensorSelectionViewModel(
            sample.SensorId,
            displayName,
            sample.Unit,
            categoryInfo.Category,
            categoryInfo.Order,
            sample.HardwareId,
            sample.HardwareName ?? string.Empty,
            sample.HardwareType,
            sample.SensorType,
            segments.Hardware,
            segments.Group,
            segments.Sensor);

        if (storedPreference is not null)
        {
            viewModel.SetSortOrder(storedPreference.SortOrder);
            if (storedPreference.IsSelected)
            {
                viewModel.IsSelected = true;
            }
        }
        else
        {
            viewModel.SetSortOrder(GetNextSortOrder(categoryInfo.Category));
        }

        viewModel.PropertyChanged += SensorSelectionOnPropertyChanged;
        _sensorLookup[sample.SensorId] = viewModel;
        _monitoringSensors.Add(viewModel);

        RefreshMonitoringSensorsView();

        return viewModel;
    }

    private int GetNextSortOrder(string category)
    {
        var maxOrder = _monitoringSensors
            .Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.SortOrder)
            .Where(order => order != SensorSelectionViewModel.UnsetSortOrder)
            .DefaultIfEmpty(-1)
            .Max();

        return maxOrder + 1;
    }

    private List<SensorPreferenceState> BuildCurrentPreferenceSnapshot()
    {
        var snapshot = new List<SensorPreferenceState>();

        foreach (var group in _monitoringSensors
            .Where(s => s.IsSelected)
            .GroupBy(s => s.Category, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderBy(s => s.SortOrder == SensorSelectionViewModel.UnsetSortOrder ? int.MaxValue : s.SortOrder)
                .ThenBy(s => s.SensorDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var sensor = ordered[i];
                if (sensor.SortOrder != i)
                {
                    sensor.SetSortOrder(i);
                }

                snapshot.Add(new SensorPreferenceState(sensor.SensorId, true, sensor.Category, sensor.SortOrder));
            }
        }

        return snapshot;
    }

    private async Task PersistSensorPreferencesAsync()
    {
        var snapshot = BuildCurrentPreferenceSnapshot();

        _storedSensorPreferences.Clear();
        foreach (var preference in snapshot)
        {
            _storedSensorPreferences[preference.SensorId] = preference;
        }

        if (_monitoringRepository is null)
        {
            return;
        }

        try
        {
            await _monitoringRepository
                .SaveSensorPreferencesAsync(
                    snapshot.Select(p => (p.SensorId, p.IsSelected, p.Category, p.SortOrder)),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to save sensor preferences: {ex}");
        }
    }

    private sealed record SensorPreferenceState(string SensorId, bool IsSelected, string Category, int SortOrder);

    private static string BuildDisplayName(SensorSample sample)
    {
        return SensorDisplayNameFormatter.BuildDisplayName(sample);
    }

    private async void OpenMonitoringSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_monitoringInitialized)
        {
            await InitializeMonitoringAsync();
        }

        PopulateSensorsSnapshot();
        ApplyStoredSelections();

        var window = new MonitoringSettingsWindow(_monitoringSensors)
        {
            Owner = this
        };

        window.ShowDialog();

        await PersistSensorPreferencesAsync().ConfigureAwait(false);
        RefreshMonitoringSensorsView();
    }

    private void MonitoringSensorsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(RefreshMonitoringSensorsView, DispatcherPriority.Background);
    }

    private ICollectionView CreateMonitoringSensorsView()
    {
        var view = new ListCollectionView(_monitoringSensors);
        view.Filter = static item => item is SensorSelectionViewModel vm && vm.IsSelected;
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorSelectionViewModel.Category)));
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(nameof(SensorSelectionViewModel.CategoryOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(SensorSelectionViewModel.Category), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(SensorSelectionViewModel.SortOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(SensorSelectionViewModel.SensorDisplayName), ListSortDirection.Ascending));
        return view;
    }

    private void RefreshMonitoringSensorsView()
    {
        if (_monitoringSensorsView is null)
        {
            return;
        }

        _monitoringSensorsView.Refresh();
        OnPropertyChanged(nameof(MonitoringSensorsView));
    }

    private void ActiveSensorsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListView listView)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            _dragStartPoint = null;
            _draggedSensor = null;
            return;
        }

        var container = FindAncestor<System.Windows.Controls.ListViewItem>(source);
        if (container?.DataContext is SensorSelectionViewModel sensor)
        {
            _dragStartPoint = e.GetPosition(listView);
            _draggedSensor = sensor;
        }
        else
        {
            _dragStartPoint = null;
            _draggedSensor = null;
        }
    }

    private void ActiveSensorsList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragStartPoint is null || _draggedSensor is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStartPoint = null;
            _draggedSensor = null;
            return;
        }

        if (sender is not System.Windows.Controls.ListView listView)
        {
            return;
        }

        var currentPosition = e.GetPosition(listView);
        if (Math.Abs(currentPosition.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new System.Windows.DataObject(typeof(SensorSelectionViewModel), _draggedSensor);
        System.Windows.DragDrop.DoDragDrop(listView, data, System.Windows.DragDropEffects.Move);

        _dragStartPoint = null;
        _draggedSensor = null;
    }

    private void ActiveSensorsList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(SensorSelectionViewModel)))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (sender is not System.Windows.Controls.ListView || e.OriginalSource is not DependencyObject source)
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var container = FindAncestor<System.Windows.Controls.ListViewItem>(source);
        if (container?.DataContext is not SensorSelectionViewModel target)
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var dragged = (SensorSelectionViewModel)e.Data.GetData(typeof(SensorSelectionViewModel))!;
        if (ReferenceEquals(dragged, target) ||
            !string.Equals(dragged.Category, target.Category, StringComparison.OrdinalIgnoreCase))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
    }

    private void ActiveSensorsList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(SensorSelectionViewModel)))
        {
            return;
        }

        if (sender is not System.Windows.Controls.ListView || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var container = FindAncestor<System.Windows.Controls.ListViewItem>(source);
        if (container?.DataContext is not SensorSelectionViewModel target)
        {
            return;
        }

        var dragged = (SensorSelectionViewModel)e.Data.GetData(typeof(SensorSelectionViewModel))!;
        if (ReferenceEquals(dragged, target) ||
            !string.Equals(dragged.Category, target.Category, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var dropPosition = e.GetPosition(container);
        var insertAfter = dropPosition.Y > container.ActualHeight / 2;

        ReorderSensorsWithinCategory(dragged, target, insertAfter);

        _dragStartPoint = null;
        _draggedSensor = null;
        e.Handled = true;
    }

    private void ReorderSensorsWithinCategory(SensorSelectionViewModel source, SensorSelectionViewModel target, bool insertAfter)
    {
        if (ReferenceEquals(source, target))
        {
            return;
        }

        var category = source.Category;
        var categoryItems = _monitoringSensors
            .Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.SensorDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!categoryItems.Remove(source))
        {
            return;
        }

        var targetIndex = categoryItems.IndexOf(target);
        if (targetIndex < 0)
        {
            return;
        }

        if (insertAfter)
        {
            targetIndex += 1;
        }

        categoryItems.Insert(targetIndex, source);

        for (var i = 0; i < categoryItems.Count; i++)
        {
            categoryItems[i].SetSortOrder(i);
        }

        RefreshMonitoringSensorsView();
        _ = PersistSensorPreferencesAsync();
    }

    private void ActiveSensorsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var listScrollViewer = FindDescendant<ScrollViewer>(source);
        if (listScrollViewer is null)
        {
            return;
        }

        var offset = listScrollViewer.VerticalOffset;
        var atTop = offset <= 0;
        var atBottom = offset >= listScrollViewer.ScrollableHeight;
        var scrollUp = e.Delta > 0;

        if ((scrollUp && !atTop) || (!scrollUp && !atBottom))
        {
            e.Handled = true;
            listScrollViewer.ScrollToVerticalOffset(offset - e.Delta);
            return;
        }

        var parentScrollViewer = FindAncestor<ScrollViewer>(source);
        if (parentScrollViewer is null || ReferenceEquals(parentScrollViewer, listScrollViewer))
        {
            return;
        }

        e.Handled = true;
        parentScrollViewer.ScrollToVerticalOffset(parentScrollViewer.VerticalOffset - e.Delta);
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            var parent = VisualTreeHelper.GetParent(current);
            if (parent is null)
            {
                return null;
            }

            if (parent is T typed)
            {
                return typed;
            }

            current = parent;
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject current) where T : DependencyObject
    {
        if (current is null)
        {
            return null;
        }

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(current);

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            var childCount = VisualTreeHelper.GetChildrenCount(item);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(item, i);
                if (child is T typed)
                {
                    return typed;
                }

                queue.Enqueue(child);
            }
        }

        return null;
    }

    private void SensorSelectionOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SensorSelectionViewModel.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        if (sender is SensorSelectionViewModel viewModel)
        {
            if (viewModel.IsSelected)
            {
                _storedSensorPreferences[viewModel.SensorId] = new SensorPreferenceState(
                    viewModel.SensorId,
                    true,
                    viewModel.Category,
                    viewModel.SortOrder);
            }
            else
            {
                _storedSensorPreferences.Remove(viewModel.SensorId);
            }

            if (!_suppressPreferencePersistence)
            {
                _ = PersistSensorPreferencesAsync();
            }
        }

        Dispatcher.InvokeAsync(RefreshMonitoringSensorsView, DispatcherPriority.Background);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void ApplyMicaBackdrop()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int dark = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));

        int corner = (int)DwmWindowCornerPreference.Round;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        int backdrop = (int)DwmSystemBackdropType.Mica;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    private enum DwmSystemBackdropType
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        Acrylic = 3,
        Tabbed = 4
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}















