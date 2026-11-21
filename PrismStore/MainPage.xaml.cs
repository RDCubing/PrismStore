using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Management.Deployment;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;
using Windows.Web.Http;
using Newtonsoft.Json.Linq;
using PrismStore.Models;
using PrismStore.Services;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.System.Profile;
using Windows.Networking;
using Windows.Networking.Connectivity;
using System.Runtime.InteropServices;
using Windows.System.Diagnostics;
using Windows.UI.ViewManagement;

namespace PrismStore
{
    public class AppUpdateItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public AppItem App { get; set; }
        public string Title => App?.Title;
        public string Publisher => App?.Publisher;
        public string ImagePath => App?.ImagePath;
        private string _installedVersion;
        public string InstalledVersion
        {
            get => _installedVersion;
            set
            {
                if (_installedVersion != value)
                {
                    _installedVersion = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VersionText));
                }
            }
        }
        public string StoreVersion => App?.Version;
        public string VersionText => $"Installed: v{InstalledVersion}  →  Store: v{StoreVersion}";

        private bool _isQueued;
        public bool IsQueued
        {
            get => _isQueued;
            set
            {
                if (_isQueued != value)
                {
                    _isQueued = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressVisibility));
                    OnPropertyChanged(nameof(ProgressBarVisibility));
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(IsActionEnabled));
                }
            }
        }

        private bool _isUpdating;
        public bool IsUpdating
        {
            get => _isUpdating;
            set
            {
                if (_isUpdating != value)
                {
                    _isUpdating = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressVisibility));
                    OnPropertyChanged(nameof(ProgressBarVisibility));
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(IsActionEnabled));
                }
            }
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                if (Math.Abs(_progressValue - value) > 0.01)
                {
                    _progressValue = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isIndeterminate;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set
            {
                if (_isIndeterminate != value)
                {
                    _isIndeterminate = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressVisibility));
                }
            }
        }

        private bool _isUpdated;
        public bool IsUpdated
        {
            get => _isUpdated;
            set
            {
                if (_isUpdated != value)
                {
                    _isUpdated = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressVisibility));
                    OnPropertyChanged(nameof(ProgressBarVisibility));
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(IsActionEnabled));
                }
            }
        }

        public Visibility ProgressVisibility =>
            (IsUpdating || IsQueued || IsUpdated || !string.IsNullOrEmpty(StatusText))
                ? Visibility.Visible
                : Visibility.Collapsed;

        public Visibility ProgressBarVisibility =>
            IsUpdating ? Visibility.Visible : Visibility.Collapsed;

        public string ActionButtonText =>
            IsUpdating ? "Updating..." :
            IsQueued ? "Queued" :
            IsUpdated ? "Updated" :
            "Update";
        public bool IsActionEnabled => !IsUpdating && !IsQueued && !IsUpdated;
    }

    public sealed partial class MainPage : Page
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void GetNativeSystemInfo(out SYSTEM_INFO lpSystemInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_INFO
        {
            public ushort wProcessorArchitecture;
            public ushort wReserved;
            public uint dwPageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort wProcessorLevel;
            public ushort wProcessorRevision;
        }
        private ObservableCollection<AppItem> _allMergedApps = new ObservableCollection<AppItem>();
        private ObservableCollection<AppUpdateItem> _availableUpdates = new ObservableCollection<AppUpdateItem>();

        private int _tileAnimationIndex = 0;
        private DispatcherTimer _sessionTimer;

        // Spotlight Carousel State
        private DispatcherTimer _spotlightTimer;
        private List<AppItem> _spotlightApps = new List<AppItem>();
        private int _currentSpotlightIndex = 0;

        private const string LoginTaskName = "PrismStoreSilentLoginCheck";

        // Filter & Sort State for All Apps
        private string _selectedSort = "Featured";
        private string _selectedCategory = "All";
        private string _selectedPublisher = "All";
        private string _selectedPlatform = "All";
        private string _currentSearchQuery = string.Empty;
        private bool _isInitializingFilters = false;
        private readonly Queue<AppUpdateItem> _updateQueue = new Queue<AppUpdateItem>();
        private bool _isProcessingUpdateQueue = false;
        private const string AcrylicModeSettingKey = "PrismStore_AcrylicMode";
        private readonly UISettings _uiSettings = new UISettings();
        private bool _isInitializingAcrylicCombo = false;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;
            ApplyAcrylicBackgroundsIfSupported();
            SetupCustomTitleBar();
            InitializeAcrylicSettings();
            AppUpdatesListView.ItemsSource = _availableUpdates;
            AccountAvatarImage.ImageFailed += (s, e) =>
            {
                AccountAvatarImage.Source = null;
                AccountAvatarImage.Visibility = Visibility.Collapsed;
            };
        }

        private void LoadDeviceSpecifications()
        {
            try
            {
                var deviceInfo = new EasClientDeviceInformation();

                if (DeviceNameText != null)
                {
                    string hostName = NetworkInformation.GetHostNames()
                        ?.FirstOrDefault(h => h.Type == HostNameType.DomainName)
                        ?.DisplayName;

                    DeviceNameText.Text = !string.IsNullOrWhiteSpace(deviceInfo.FriendlyName)
                        ? deviceInfo.FriendlyName
                        : (!string.IsNullOrWhiteSpace(hostName) ? hostName : "Windows PC");
                }

                if (DeviceManufacturerText != null)
                    DeviceManufacturerText.Text = !string.IsNullOrWhiteSpace(deviceInfo.SystemManufacturer) ? deviceInfo.SystemManufacturer : "Unknown";

                if (DeviceModelText != null)
                    DeviceModelText.Text = !string.IsNullOrWhiteSpace(deviceInfo.SystemProductName) ? deviceInfo.SystemProductName : "Unknown";

                // Query true hardware architecture via GetNativeSystemInfo (bypasses WOW64 x86 reporting)
                string nativeArch = "x64";
                bool is64BitOs = true;
                try
                {
                    GetNativeSystemInfo(out SYSTEM_INFO sysInfo);
                    switch (sysInfo.wProcessorArchitecture)
                    {
                        case 9:  // PROCESSOR_ARCHITECTURE_AMD64
                            nativeArch = "x64";
                            is64BitOs = true;
                            break;
                        case 12: // PROCESSOR_ARCHITECTURE_ARM64
                            nativeArch = "ARM64";
                            is64BitOs = true;
                            break;
                        case 5:  // PROCESSOR_ARCHITECTURE_ARM
                            nativeArch = "ARM";
                            is64BitOs = false;
                            break;
                        case 0:  // PROCESSOR_ARCHITECTURE_INTEL
                        default:
                            nativeArch = "x86";
                            is64BitOs = false;
                            break;
                    }
                }
                catch
                {
                    nativeArch = Package.Current.Id.Architecture.ToString();
                    is64BitOs = nativeArch.IndexOf("64", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                int logicalCores = Environment.ProcessorCount;
                string procIdent = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                if (ProcessorInfoText != null)
                {
                    ProcessorInfoText.Text = !string.IsNullOrWhiteSpace(procIdent)
                        ? $"{procIdent} ({logicalCores} logical processors)"
                        : $"{nativeArch} ({logicalCores} logical processors)";
                }

                if (InstalledRamText != null)
                {
                    ulong totalBytes = 0;

                    if (ApiInformation.IsTypePresent("Windows.System.Diagnostics.SystemDiagnosticInfo"))
                    {
                        try
                        {
                            var sysDiag = SystemDiagnosticInfo.GetForCurrentSystem();
                            totalBytes = sysDiag?.MemoryUsage?.GetReport()?.TotalPhysicalSizeInBytes ?? 0;
                        }
                        catch
                        {
                            totalBytes = 0;
                        }
                    }

                    if (totalBytes > 0)
                    {
                        double totalGb = totalBytes / (1024.0 * 1024.0 * 1024.0);
                        // Match Windows Settings rounding (e.g., 11.9 GB hardware-reserved -> 12.0 GB)
                        double roundedGb = Math.Abs(totalGb - Math.Round(totalGb)) < 0.35
                            ? Math.Round(totalGb)
                            : Math.Round(totalGb, 1);
                        InstalledRamText.Text = $"{roundedGb:0.#} GB";
                    }
                    else
                    {
                        InstalledRamText.Text = "Unavailable on this OS build";
                    }
                }

                if (SystemTypeText != null)
                {
                    SystemTypeText.Text = is64BitOs
                        ? $"64-bit operating system, {nativeArch}-based processor"
                        : $"32-bit operating system, {nativeArch}-based processor";
                }

                if (WindowsVersionText != null)
                {
                    string versionFamily = AnalyticsInfo.VersionInfo?.DeviceFamily ?? "Windows.Desktop";
                    string rawVer = AnalyticsInfo.VersionInfo?.DeviceFamilyVersion;
                    string osBuildText = FormatDeviceFamilyVersion(rawVer);
                    string sku = !string.IsNullOrWhiteSpace(deviceInfo.OperatingSystem) ? deviceInfo.OperatingSystem : "Windows";

                    WindowsVersionText.Text = !string.IsNullOrEmpty(osBuildText)
                        ? $"{sku} (Build {osBuildText}) • {versionFamily}"
                        : $"{sku} • {versionFamily}";
                }
            }
            catch
            {
                // Ignore restricted device info queries on locked-down SKUs
            }
        }

        private string FormatDeviceFamilyVersion(string deviceFamilyVersion)
        {
            if (ulong.TryParse(deviceFamilyVersion, out ulong v))
            {
                ulong major = (v & 0xFFFF000000000000L) >> 48;
                ulong minor = (v & 0x0000FFFF00000000L) >> 32;
                ulong build = (v & 0x00000000FFFF0000L) >> 16;
                ulong revision = (v & 0x000000000000FFFFL);
                return $"{major}.{minor}.{build}.{revision}";
            }
            return string.Empty;
        }

        private async void CopyDeviceInfoButton_Click(object sender, RoutedEventArgs e)
        {
            string summary =
                $"Device name\t{DeviceNameText?.Text}\n" +
                $"Manufacturer\t{DeviceManufacturerText?.Text}\n" +
                $"Model\t{DeviceModelText?.Text}\n" +
                $"Processor\t{ProcessorInfoText?.Text}\n" +
                $"Installed RAM\t{InstalledRamText?.Text}\n" +
                $"System type\t{SystemTypeText?.Text}\n" +
                $"Windows edition & build\t{WindowsVersionText?.Text}";

            var dataPackage = new DataPackage();
            dataPackage.SetText(summary);
            Clipboard.SetContent(dataPackage);

            if (CopyDeviceInfoButton != null)
            {
                CopyDeviceInfoButton.Content = "Copied";
                await Task.Delay(1500);
                CopyDeviceInfoButton.Content = "Copy";
            }
        }

        // ==========================================
        // MODERN UWP CONTENTDIALOG HELPERS (1507+ SAFE)
        // ==========================================
        private async Task ShowDialogAsync(string title, string message, string closeText = "OK")
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            // Only touch IContentDialog2 (CloseButtonText) on Windows 10 1709+ (Build 16299+)
            if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.ContentDialog", "CloseButtonText"))
            {
                dialog.CloseButtonText = closeText;
            }
            else
            {
                dialog.PrimaryButtonText = closeText;
            }

            await dialog.ShowAsync();
        }

        private async Task<bool> ShowConfirmDialogAsync(string title, string message, string confirmText, string cancelText = "Cancel")
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = confirmText,
                SecondaryButtonText = cancelText
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        // ==========================================
        // FLUENT ACRYLIC & EXTENDED TITLEBAR
        // ==========================================

        public void RefreshAuthFromExternalLogin()
        {
            UpdateAccountUI();

            if (AuthService.IsLoggedIn && !AuthService.IsTokenExpired())
            {
                StartSessionCountdown();
            }

            if (UploaderSubmissionFormPanel != null && UploaderAuthRequiredPanel != null)
            {
                bool isAuthed = AuthService.IsLoggedIn && !AuthService.IsTokenExpired();
                UploaderSubmissionFormPanel.Visibility = isAuthed ? Visibility.Visible : Visibility.Collapsed;
                UploaderAuthRequiredPanel.Visibility = isAuthed ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void SetupCustomTitleBar()
        {
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            if (AppTitleBarDragRegion != null)
            {
                Window.Current.SetTitleBar(AppTitleBarDragRegion);
            }

            UpdateTitleBarPadding(coreTitleBar);
            coreTitleBar.LayoutMetricsChanged += (s, args) => UpdateTitleBarPadding(s);
        }

        private void UpdateTitleBarPadding(CoreApplicationViewTitleBar coreTitleBar)
        {
            if (TopHeaderGrid != null)
            {
                double rightInset = coreTitleBar.SystemOverlayRightInset > 0
                    ? coreTitleBar.SystemOverlayRightInset
                    : 138;
                TopHeaderGrid.Padding = new Thickness(0, 0, rightInset, 0);
            }
        }

        // ==========================================
        // PAGE LIFECYCLE
        // ==========================================
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DisableSystemBackButton();
            if (AppTitleBarDragRegion != null)
            {
                Window.Current.SetTitleBar(AppTitleBarDragRegion);
            }
            StartSpotlightRotation();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            StopSpotlightRotation();
        }

        private void DisableSystemBackButton()
        {
            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            DisableSystemBackButton();
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            LoadDeviceSpecifications();
            bool expired = await AuthService.LogoutIfExpired();
            UpdateAccountUI();

            if (AuthService.IsLoggedIn && !expired)
            {
                StartSessionCountdown();
            }

            var _ = PackageLookupService.WarmCacheAsync();

            if (PrismStoreService.PrismStore.Count == 0 && PrismStoreService.NeonStore.Count == 0)
            {
                LoadingRing.Visibility = Visibility.Visible;
                LoadingRing.IsActive = true;

                await PrismStoreService.LoadAsync();

                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }

            _tileAnimationIndex = 0;

            TopAppsGridView.ItemsSource = PrismStoreService.TopApps.Take(5).ToList();
            PrismAppsGridView.ItemsSource = PrismStoreService.PrismStore.Take(5).ToList();
            NeonAppsGridView.ItemsSource = PrismStoreService.NeonStore.Take(5).ToList();

            InitializeFilterSubmenus();
            ApplyFilterAndSort();
            AllAppsGridView.ItemsSource = _allMergedApps;

            SetupSpotlight();
            AnimateViewTransition(HomeScrollViewer);

            await ScanForAppUpdatesAsync();
            await RegisterSilentLoginBackgroundTaskAsync();
        }

        private async Task RegisterSilentLoginBackgroundTaskAsync()
        {
            // Single-process (in-process) background tasks require Windows 10 Build 14393 (1607)+
            if (!ApiInformation.IsMethodPresent("Windows.UI.Xaml.Application", "OnBackgroundActivated"))
                return;

            try
            {
                foreach (var existing in BackgroundTaskRegistration.AllTasks.Values)
                {
                    if (existing.Name == LoginTaskName)
                        return;
                }

                var status = await BackgroundExecutionManager.RequestAccessAsync();
                if (status == BackgroundAccessStatus.DeniedByUser ||
                    status == BackgroundAccessStatus.DeniedBySystemPolicy)
                {
                    return;
                }

                var builder = new BackgroundTaskBuilder
                {
                    Name = LoginTaskName
                };
                builder.SetTrigger(new SystemTrigger(SystemTriggerType.SessionConnected, false));
                builder.Register();
            }
            catch
            {
                // Ignore background registration failures on unsupported OS builds or restricted policies
            }
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            StopSessionCountdown();
            StopSpotlightRotation();
            _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
        }

        // ==========================================
        // FEATURED SPOTLIGHT BANNER ROTATION
        // ==========================================
        private void SetupSpotlight()
        {
            _spotlightApps.Clear();

            var featured = PrismStoreService.PrismStore
                .Where(a => !string.IsNullOrEmpty(a.Title))
                .Distinct()
                .Take(5)
                .ToList();

            if (featured.Count == 0)
            {
                featured = PrismStoreService.TopApps
                    .Where(a => !string.IsNullOrEmpty(a.Title))
                    .Distinct()
                    .Take(5)
                    .ToList();
            }

            if (featured.Count > 0)
            {
                _spotlightApps = featured;
                _currentSpotlightIndex = 0;
                BuildSpotlightDots();
                ShowSpotlightApp(_currentSpotlightIndex, animate: false);
                StartSpotlightRotation();
            }
        }

        private void InitializeAcrylicSettings()
        {
            bool isAcrylicSupported = ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush");

            if (PersonalizationSection != null)
            {
                PersonalizationSection.Visibility = isAcrylicSupported ? Visibility.Visible : Visibility.Collapsed;
            }

            if (!isAcrylicSupported)
                return;

            string savedMode = ApplicationData.Current.LocalSettings.Values[AcrylicModeSettingKey] as string ?? "Full";

            if (AcrylicModeComboBox != null)
            {
                _isInitializingAcrylicCombo = true;
                foreach (ComboBoxItem item in AcrylicModeComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), savedMode, StringComparison.OrdinalIgnoreCase))
                    {
                        AcrylicModeComboBox.SelectedItem = item;
                        break;
                    }
                }
                if (AcrylicModeComboBox.SelectedIndex < 0)
                    AcrylicModeComboBox.SelectedIndex = 0;

                _isInitializingAcrylicCombo = false;
            }

            ApplyAcrylicBackgroundsIfSupported();
        }

        private void AcrylicModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingAcrylicCombo)
                return;

            var selectedItem = AcrylicModeComboBox?.SelectedItem as ComboBoxItem;
            string mode = selectedItem?.Tag?.ToString() ?? "Full";

            ApplicationData.Current.LocalSettings.Values[AcrylicModeSettingKey] = mode;
            ApplyAcrylicBackgroundsIfSupported();
        }

        private async void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            // ColorValuesChanged fires on a background thread when switching Windows Light/Dark mode
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                ApplyAcrylicBackgroundsIfSupported();
            });
        }

        private void ApplyAcrylicBackgroundsIfSupported()
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush"))
                return;

            string mode = ApplicationData.Current.LocalSettings.Values[AcrylicModeSettingKey] as string ?? "Full";

            // Determine current effective theme (works even when RequestedTheme is Default)
            bool isLightTheme = ActualTheme == ElementTheme.Light;
            if (ActualTheme == ElementTheme.Default)
            {
                var bgColor = _uiSettings.GetColorValue(UIColorType.Background);
                isLightTheme = bgColor.R > 128 && bgColor.G > 128 && bgColor.B > 128;
            }

            var defaultPageBrush = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
            var defaultChromeBrush = Application.Current.Resources["SystemControlBackgroundAltHighBrush"] as Brush;
            var defaultPaneBrush = Application.Current.Resources["SystemControlBackgroundChromeMediumLowBrush"] as Brush;
            var defaultListLowBrush = Application.Current.Resources["SystemControlBackgroundListLowBrush"] as Brush;

            if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
            {
                if (RootPageGrid != null) RootPageGrid.Background = defaultPageBrush;
                if (TopHeaderGrid != null) TopHeaderGrid.Background = defaultChromeBrush;
                if (RootSplitView != null) RootSplitView.PaneBackground = defaultPaneBrush;
                if (NavPaneGrid != null) NavPaneGrid.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                if (ContentHostGrid != null) ContentHostGrid.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                if (UploaderAuthRequiredPanel != null) UploaderAuthRequiredPanel.Background = defaultListLowBrush;
                return;
            }

            Windows.UI.Color tintColor = isLightTheme
                ? Windows.UI.Color.FromArgb(255, 243, 243, 243)
                : Windows.UI.Color.FromArgb(255, 32, 32, 32);

            Windows.UI.Color fallbackColor = isLightTheme
                ? Windows.UI.Color.FromArgb(255, 238, 238, 238)
                : Windows.UI.Color.FromArgb(255, 28, 28, 28);

            var mainWindowBrush = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                TintColor = tintColor,
                TintOpacity = 0.72,
                FallbackColor = fallbackColor
            };

            var chromeBrush = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                TintColor = tintColor,
                TintOpacity = 0.65,
                FallbackColor = fallbackColor
            };

            var elementBrush = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                TintColor = tintColor,
                TintOpacity = 0.60,
                FallbackColor = fallbackColor
            };

            bool isSemi = string.Equals(mode, "Semi", StringComparison.OrdinalIgnoreCase);

            // Full acrylic applies HostBackdrop to RootPageGrid; Semi keeps the solid page background
            if (RootPageGrid != null)
            {
                RootPageGrid.Background = isSemi ? defaultPageBrush : mainWindowBrush;
            }

            // Title bar and SplitView pane get acrylic in both Full and Semi modes
            if (TopHeaderGrid != null) TopHeaderGrid.Background = chromeBrush;
            if (RootSplitView != null) RootSplitView.PaneBackground = chromeBrush;
            if (NavPaneGrid != null) NavPaneGrid.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);

            if (ContentHostGrid != null)
            {
                ContentHostGrid.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
            }

            if (UploaderAuthRequiredPanel != null)
            {
                UploaderAuthRequiredPanel.Background = elementBrush;
            }
        }

        private void StartSpotlightRotation()
        {
            StopSpotlightRotation();

            if (_spotlightApps.Count <= 1) return;

            _spotlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _spotlightTimer.Tick += (s, e) =>
            {
                if (_spotlightApps.Count == 0) return;
                _currentSpotlightIndex = (_currentSpotlightIndex + 1) % _spotlightApps.Count;
                ShowSpotlightApp(_currentSpotlightIndex, animate: true);
            };
            _spotlightTimer.Start();
        }

        private void StopSpotlightRotation()
        {
            if (_spotlightTimer != null)
            {
                _spotlightTimer.Stop();
                _spotlightTimer = null;
            }
        }

        private void ShowSpotlightApp(int index, bool animate)
        {
            if (index < 0 || index >= _spotlightApps.Count) return;

            var app = _spotlightApps[index];

            SpotlightTitleText.Text = app.Title ?? "Featured App";
            SpotlightSubtitleText.Text = !string.IsNullOrWhiteSpace(app.Subtitle)
                ? app.Subtitle
                : $"Available now for {app.Framework ?? "Windows 10"}";

            if (!string.IsNullOrEmpty(app.ImagePath))
            {
                try
                {
                    SpotlightHeroImage.Source = new BitmapImage(new Uri(app.DetailImagePath));
                    SpotlightIconImage.Source = new BitmapImage(new Uri(app.ImagePath));
                }
                catch
                {
                    SpotlightHeroImage.Source = null;
                }
            }

            UpdateSpotlightDots(index);

            if (animate)
            {
                var sb = new Storyboard();
                var fadeIn = new DoubleAnimation
                {
                    From = 0.3,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(220)
                };
                Storyboard.SetTarget(fadeIn, SpotlightTitleText);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                sb.Children.Add(fadeIn);
                sb.Begin();
            }
        }

        private void BuildSpotlightDots()
        {
            SpotlightDotsPanel.Children.Clear();

            var accentBrush = Application.Current.Resources["SystemControlForegroundAccentBrush"] as Brush
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));
            var inactiveBrush = new SolidColorBrush(Windows.UI.Colors.Gray);

            for (int i = 0; i < _spotlightApps.Count; i++)
            {
                int targetIndex = i;
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(4, 0, 4, 0),
                    Fill = (i == _currentSpotlightIndex) ? accentBrush : inactiveBrush
                };

                dot.Tapped += (s, e) =>
                {
                    if (_currentSpotlightIndex == targetIndex && _spotlightApps.Count > 0)
                    {
                        var selected = _spotlightApps[_currentSpotlightIndex];
                        Frame.Navigate(typeof(SelectedApp), selected);
                    }
                    else
                    {
                        _currentSpotlightIndex = targetIndex;
                        ShowSpotlightApp(_currentSpotlightIndex, animate: true);
                        StartSpotlightRotation();
                    }
                };

                SpotlightDotsPanel.Children.Add(dot);
            }
        }

        private void UpdateSpotlightDots(int activeIndex)
        {
            var accentBrush = Application.Current.Resources["SystemControlForegroundAccentBrush"] as Brush
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));
            var inactiveBrush = new SolidColorBrush(Windows.UI.Colors.Gray);

            for (int i = 0; i < SpotlightDotsPanel.Children.Count; i++)
            {
                if (SpotlightDotsPanel.Children[i] is Ellipse dot)
                {
                    dot.Fill = (i == activeIndex) ? accentBrush : inactiveBrush;
                }
            }
        }

        private void SpotlightBanner_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_spotlightApps.Count > 0 && _currentSpotlightIndex < _spotlightApps.Count)
            {
                var selected = _spotlightApps[_currentSpotlightIndex];
                Frame.Navigate(typeof(SelectedApp), selected);
            }
        }

        // ==========================================
        // SIMPLIFIED OPACITY ANIMATIONS
        // ==========================================
        private void AnimateViewTransition(FrameworkElement container)
        {
            if (container == null) return;

            container.Opacity = 0;

            var sb = new Storyboard();
            var fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180)
            };
            Storyboard.SetTarget(fadeIn, container);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sb.Children.Add(fadeIn);
            sb.Begin();
        }

        private void AppTile_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement card)
            {
                double delayMs = Math.Min(_tileAnimationIndex * 20, 240);
                _tileAnimationIndex++;

                var sb = new Storyboard { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
                var fadeIn = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(180)
                };
                Storyboard.SetTarget(fadeIn, card);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                sb.Children.Add(fadeIn);
                sb.Begin();
            }
        }

        // ==========================================
        // SIDELOADING & CERTIFICATE-FIRST VERIFICATION
        // ==========================================
        private async Task InstallDownloadedItemAsync(DownloadItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FileName))
                return;

            StorageFile file = await FindDownloadedFileAsync(item.FileName);

            if (file == null)
            {
                try
                {
                    string token = (item as dynamic)?.AccessToken as string;
                    if (!string.IsNullOrWhiteSpace(token) &&
                        StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
                    {
                        file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(token);
                    }
                }
                catch
                {
                    file = null;
                }
            }

            if (file == null)
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads,
                    ViewMode = Windows.Storage.Pickers.PickerViewMode.List
                };
                picker.FileTypeFilter.Add(".appx");
                picker.FileTypeFilter.Add(".appxbundle");
                picker.FileTypeFilter.Add(".msix");
                picker.FileTypeFilter.Add(".msixbundle");

                file = await picker.PickSingleFileAsync();
            }

            if (file == null)
            {
                bool openLink = await ShowConfirmDialogAsync(
                    "File Not Found",
                    $"\"{item.FileName}\" was not selected or could not be located. Would you like to open its download link in your browser?",
                    "Open Link",
                    "Cancel");

                if (openLink && !string.IsNullOrWhiteSpace(item.DownloadUrl))
                {
                    await Launcher.LaunchUriAsync(new Uri(item.DownloadUrl));
                }
                return;
            }

            if (!CertificateCheckHelper.IsWindowsAppPackage(file))
            {
                await Launcher.LaunchFileAsync(file);
                return;
            }

            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            try
            {
                var packageManager = new PackageManager();
                var deployOperation = packageManager.AddPackageAsync(
                    new Uri(file.Path),
                    null,
                    DeploymentOptions.None);

                var tcs = new TaskCompletionSource<bool>();
                int deployHr = 0;

                deployOperation.Completed = (info, status) =>
                {
                    if (status == AsyncStatus.Completed)
                    {
                        var result = info.GetResults();
                        if (result?.ExtendedErrorCode != null)
                        {
                            deployHr = result.ExtendedErrorCode.HResult;
                            tcs.TrySetResult(false);
                        }
                        else
                        {
                            tcs.TrySetResult(true);
                        }
                    }
                    else
                    {
                        deployHr = info.ErrorCode?.HResult ?? 0;
                        tcs.TrySetResult(false);
                    }
                };

                bool installed = await tcs.Task;
                if (installed)
                {
                    PackageLookupService.InvalidateCache();
                    ToastService.Show("App installed", item.Title ?? file.Name, item.ImagePath);
                    await ShowDialogAsync("Installation Complete", $"\"{item.Title ?? file.Name}\" was installed successfully.");
                }
                else if (deployHr == unchecked((int)0x800B0109) || deployHr == unchecked((int)0x800B010A))
                {
                    string publisher = await CertificateCheckHelper.ExtractPackagePublisherAsync(file);
                    await CertificateCheckHelper.ShowInstallCertificateDialogAsync(file, publisher);
                }
                else
                {
                    await Launcher.LaunchFileAsync(file);
                }
            }
            catch (UnauthorizedAccessException)
            {
                await Launcher.LaunchFileAsync(file);
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Installation Error", ex.Message);
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<StorageFile> FindDownloadedFileAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            try
            {
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                IStorageItem folderItem = await localFolder.TryGetItemAsync("Packages");
                if (folderItem is StorageFolder packagesFolder)
                {
                    IStorageItem fileItem = await packagesFolder.TryGetItemAsync(fileName);
                    return fileItem as StorageFile;
                }
            }
            catch
            {
                // Ignore missing folder/file errors
            }

            return null;
        }

        private async Task SideloadDownloadItemAsync(
    DownloadItem item,
    bool isForceUpdate = false,
    bool allowDowngrade = false,
    Package installedPackage = null)
        {
            StorageFile file = await FindDownloadedFileAsync(item.FileName);
            if (file == null)
            {
                await ShowDialogAsync(
                    "File Not Found",
                    $"Could not find '{item.FileName}' in local storage. Re-download the app from its store page.");
                return;
            }

            if (!CertificateCheckHelper.IsWindowsAppPackage(file))
            {
                await ShowDialogAsync(
                    "Unsupported Package Type",
                    $"'{file.Name}' is not a valid Windows app package (.appx, .appxbundle, .msix, or .msixbundle).");
                return;
            }

            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            try
            {
                var packageManager = new PackageManager();
                DeploymentOptions options = isForceUpdate
                    ? DeploymentOptions.ForceApplicationShutdown
                    : DeploymentOptions.None;

                if (allowDowngrade)
                {
                    if (ApiInformation.IsEnumNamedValuePresent("Windows.Management.Deployment.DeploymentOptions", "ForceUpdateFromAnyVersion"))
                    {
                        options |= (DeploymentOptions)0x40000;
                    }
                    else if (installedPackage != null)
                    {
                        await PackageLookupService.UninstallPackageAsync(installedPackage);
                    }
                }

                var deployOperation = packageManager.AddPackageAsync(
                    new Uri(file.Path),
                    null,
                    options);

                var tcs = new TaskCompletionSource<bool>();
                string deployError = null;
                int deployHr = 0;

                deployOperation.Completed = (info, status) =>
                {
                    if (status == AsyncStatus.Completed)
                    {
                        var result = info.GetResults();
                        if (result?.ExtendedErrorCode != null)
                        {
                            deployHr = result.ExtendedErrorCode.HResult;
                            deployError = !string.IsNullOrWhiteSpace(result.ErrorText)
                                ? result.ErrorText
                                : result.ExtendedErrorCode.Message;
                            tcs.TrySetResult(false);
                        }
                        else
                        {
                            tcs.TrySetResult(true);
                        }
                    }
                    else
                    {
                        deployHr = info.ErrorCode?.HResult ?? 0;
                        deployError = info.ErrorCode?.Message ?? "Deployment failed.";
                        tcs.TrySetResult(false);
                    }
                };

                bool installed = await tcs.Task;
                if (installed)
                {
                    PackageLookupService.InvalidateCache();
                    ToastService.Show(
                        isForceUpdate ? "App updated successfully" : "App installed successfully",
                        item.Title ?? file.Name,
                        item.ImagePath);
                    await ShowDialogAsync(
                        isForceUpdate ? "Update Complete" : "Installation Complete",
                        $"\"{item.Title ?? file.Name}\" was {(isForceUpdate ? "updated" : "installed")} successfully.");
                }
                else if (deployHr == unchecked((int)0x800B0109) || deployHr == unchecked((int)0x800B010A))
                {
                    string publisher = await CertificateCheckHelper.ExtractPackagePublisherAsync(file);
                    await CertificateCheckHelper.ShowInstallCertificateDialogAsync(file, publisher);
                }
                else
                {
                    await ShowDialogAsync(
                        "Deployment Failed",
                        $"Could not deploy \"{item.Title ?? file.Name}\".\n\nReason: {deployError}");
                }
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Installation Error", ex.Message);
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        // ==========================================
        // APP UPDATES SCAN & DIRECT INSTALL
        // ==========================================
        private async Task ScanForAppUpdatesAsync()
        {
            if (_isProcessingUpdateQueue)
                return;
            _availableUpdates.Clear();

            var allCatalogApps = PrismStoreService.PrismStore
                .Concat(PrismStoreService.NeonStore)
                .GroupBy(a => !string.IsNullOrWhiteSpace(a.packageId) ? a.packageId : a.Title, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (var app in allCatalogApps)
            {
                var status = await PackageLookupService.GetAppStatusAsync(app);
                if (status.State == AppInstallState.UpdateAvailable)
                {
                    _availableUpdates.Add(new AppUpdateItem
                    {
                        App = app,
                        InstalledVersion = status.InstalledVersionText
                    });
                }
            }

            bool hasUpdates = _availableUpdates.Count > 0;

            if (AppUpdatesSummaryText != null)
            {
                AppUpdatesSummaryText.Text = hasUpdates
                    ? $"{_availableUpdates.Count} update(s) available for your installed apps."
                    : "No pending app updates.";
            }

            if (UpdateAllButton != null)
                UpdateAllButton.Visibility = hasUpdates ? Visibility.Visible : Visibility.Collapsed;

            if (AppUpdatesListView != null)
                AppUpdatesListView.Visibility = hasUpdates ? Visibility.Visible : Visibility.Collapsed;

            if (EmptyUpdatesPanel != null)
                EmptyUpdatesPanel.Visibility = hasUpdates ? Visibility.Collapsed : Visibility.Visible;

            if (HomeUpdatesSection != null && HomeUpdatesGridView != null)
            {
                HomeUpdatesGridView.ItemsSource = hasUpdates
                    ? _availableUpdates.Select(u => u.App).ToList()
                    : null;
                HomeUpdatesSection.Visibility = hasUpdates ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void UpdateAppDirect_Click(object sender, RoutedEventArgs e)
        {
            var updateItem = (sender as FrameworkElement)?.Tag as AppUpdateItem;
            if (updateItem?.App == null || string.IsNullOrWhiteSpace(updateItem.App.DownloadUrl))
                return;

            EnqueueUpdateItem(updateItem);
            await ProcessUpdateQueueAsync();
        }

        private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdates.Count == 0)
                return;

            foreach (var item in _availableUpdates)
            {
                EnqueueUpdateItem(item);
            }

            await ProcessUpdateQueueAsync();
        }

        private async Task ProcessUpdateQueueAsync()
        {
            if (_isProcessingUpdateQueue)
                return;

            _isProcessingUpdateQueue = true;
            if (UpdateAllButton != null) UpdateAllButton.IsEnabled = false;
            if (RefreshUpdatesButton != null) RefreshUpdatesButton.IsEnabled = false;

            int completedCount = 0;
            int totalProcessed = 0;

            try
            {
                while (_updateQueue.Count > 0)
                {
                    var currentItem = _updateQueue.Dequeue();
                    if (currentItem?.App == null)
                        continue;

                    totalProcessed++;
                    currentItem.IsQueued = false;
                    currentItem.IsUpdating = true;
                    currentItem.ProgressValue = 0;
                    currentItem.IsIndeterminate = false;

                    bool ok = false;
                    try
                    {
                        ok = await UpdateSingleAppWithProgressAsync(currentItem);
                    }
                    catch (Exception ex)
                    {
                        currentItem.StatusText = $"Update failed: {ex.Message}";
                        await ShowDialogAsync("Update Error", ex.Message);
                    }
                    finally
                    {
                        currentItem.IsUpdating = false;
                        currentItem.IsIndeterminate = false;
                    }

                    if (ok)
                    {
                        completedCount++;
                        currentItem.IsUpdated = true;
                        currentItem.InstalledVersion = currentItem.StoreVersion;
                        currentItem.StatusText = "Updated successfully";
                        if (totalProcessed == 1 && _updateQueue.Count == 0)
                        {
                            ToastService.Show("App updated successfully", currentItem.Title, currentItem.ImagePath);
                        }
                    }
                }

                if (totalProcessed > 1)
                {
                    ToastService.Show("Batch update complete", $"{completedCount} of {totalProcessed} app(s) updated.");
                }
            }
            finally
            {
                _isProcessingUpdateQueue = false;
                if (UpdateAllButton != null) UpdateAllButton.IsEnabled = true;
                if (RefreshUpdatesButton != null) RefreshUpdatesButton.IsEnabled = true;
            }

            if (completedCount > 0)
            {
                PackageLookupService.InvalidateCache();
                await PackageLookupService.WarmCacheAsync();
                await ScanForAppUpdatesAsync();
            }
        }

        private async Task<bool> UpdateSingleAppWithProgressAsync(AppUpdateItem item)
        {
            var app = item.App;
            if (app == null || string.IsNullOrWhiteSpace(app.DownloadUrl))
                return false;

            item.IsIndeterminate = false;
            item.ProgressValue = 0;
            item.StatusText = "Preparing download...";

            Uri uri = new Uri(app.DownloadUrl);
            string fileName = System.IO.Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(fileName))
                fileName = app.Title + ".appx";

            StorageFolder packagesFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "Packages",
                CreationCollisionOption.OpenIfExists);

            StorageFile file = await packagesFolder.CreateFileAsync(
                fileName,
                CreationCollisionOption.ReplaceExisting);

            // 1. Download with per-item progress
            await DownloadPackageWithProgressAsync(uri, file, item);

            DownloadHistoryService.Instance.Add(new DownloadItem
            {
                Title = app.Title,
                FileName = file.Name,
                DownloadUrl = app.DownloadUrl,
                ImagePath = app.ImagePath,
                DownloadedAt = DateTime.Now
            });

            if (!CertificateCheckHelper.IsWindowsAppPackage(file))
            {
                item.StatusText = "Opening installer...";
                await Launcher.LaunchFileAsync(file);
                return true;
            }

            // 2. Deploy package with per-item progress
            bool installed = await DeployPackageWithProgressAsync(file, item);
            if (installed)
            {
                PackageLookupService.InvalidateCache();
            }

            return installed;
        }

        private void EnqueueUpdateItem(AppUpdateItem item)
        {
            if (item == null || item.IsUpdating || item.IsQueued || item.IsUpdated || _updateQueue.Contains(item))
                return;

            item.IsQueued = true;
            item.ProgressValue = 0;
            item.IsIndeterminate = false;
            item.StatusText = "Queued...";
            _updateQueue.Enqueue(item);
        }

        private async Task DownloadPackageWithProgressAsync(Uri uri, StorageFile file, AppUpdateItem item)
        {
            using (HttpClient client = new HttpClient())
            {
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                ulong totalBytes = response.Content.Headers.ContentLength ?? 0;

                using (var input = await response.Content.ReadAsInputStreamAsync())
                using (var output = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (var reader = new DataReader(input))
                using (var writer = new DataWriter(output))
                {
                    const uint BufferSize = 8192;
                    ulong downloadedBytes = 0;

                    while (true)
                    {
                        uint loaded = await reader.LoadAsync(BufferSize);
                        if (loaded == 0)
                            break;

                        byte[] buffer = new byte[loaded];
                        reader.ReadBytes(buffer);
                        writer.WriteBytes(buffer);
                        await writer.StoreAsync();

                        downloadedBytes += loaded;

                        if (totalBytes > 0)
                        {
                            double percent = (double)downloadedBytes / totalBytes * 100;
                            item.IsIndeterminate = false;
                            item.ProgressValue = percent;
                            item.StatusText =
                                $"Downloading: {downloadedBytes / 1024d / 1024d:F1} MB / {totalBytes / 1024d / 1024d:F1} MB ({percent:F0}%)";
                        }
                        else
                        {
                            item.IsIndeterminate = true;
                            item.StatusText = $"Downloading: {downloadedBytes / 1024d / 1024d:F1} MB...";
                        }
                    }

                    await writer.FlushAsync();
                    item.IsIndeterminate = false;
                    item.ProgressValue = 100;
                }
            }
        }

        private async Task<bool> DeployPackageWithProgressAsync(StorageFile packageFile, AppUpdateItem item)
        {
            if (!CertificateCheckHelper.IsWindowsAppPackage(packageFile))
            {
                await ShowDialogAsync(
                    "Unsupported Package Type",
                    $"'{packageFile.Name}' is not a valid Windows app package (.appx, .appxbundle, .msix, or .msixbundle).");
                return false;
            }

            item.IsIndeterminate = true;
            item.StatusText = "Installing update...";

            var packageManager = new PackageManager();
            var deployOperation = packageManager.AddPackageAsync(
                new Uri(packageFile.Path),
                null,
                DeploymentOptions.ForceApplicationShutdown);

            var tcs = new TaskCompletionSource<bool>();
            string deployError = null;
            int deployHr = 0;

            deployOperation.Progress = (info, progress) =>
            {
                var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    item.IsIndeterminate = false;
                    item.ProgressValue = progress.percentage;
                    item.StatusText = $"Installing... {progress.percentage}%";
                });
            };

            deployOperation.Completed = (info, status) =>
            {
                if (status == AsyncStatus.Completed)
                {
                    var result = info.GetResults();
                    if (result?.ExtendedErrorCode != null)
                    {
                        deployHr = result.ExtendedErrorCode.HResult;
                        deployError = !string.IsNullOrWhiteSpace(result.ErrorText)
                            ? result.ErrorText
                            : result.ExtendedErrorCode.Message;
                        tcs.TrySetResult(false);
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                }
                else
                {
                    deployHr = info.ErrorCode?.HResult ?? 0;
                    deployError = info.ErrorCode?.Message ?? "Deployment failed.";
                    tcs.TrySetResult(false);
                }
            };

            bool installed = await tcs.Task;
            item.IsIndeterminate = false;

            if (!installed)
            {
                if (CertificateCheckHelper.IsCertificateDeploymentError(deployHr, deployError))
                {
                    item.StatusText = "Certificate required";
                    string publisher = await CertificateCheckHelper.ExtractPackagePublisherAsync(packageFile);
                    await CertificateCheckHelper.ShowInstallCertificateDialogAsync(packageFile, publisher);
                    return false;
                }
                else if (!string.IsNullOrWhiteSpace(deployError))
                {
                    item.StatusText = $"Failed: {deployError}";
                    await ShowDialogAsync(
                        "Update Failed",
                        $"Could not update \"{item.Title}\".\n\nReason: {deployError}");
                }
            }

            return installed;
        }

        // ==========================================
        // HOVER SPLITVIEW BEHAVIOR
        // ==========================================
        private void NavPaneGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (RootSplitView.DisplayMode != SplitViewDisplayMode.Overlay)
            {
                RootSplitView.IsPaneOpen = true;
            }
        }

        private void NavPaneGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (RootSplitView.DisplayMode != SplitViewDisplayMode.Overlay)
            {
                RootSplitView.IsPaneOpen = false;
            }
        }

        private void HamburgerBtn_Click(object sender, RoutedEventArgs e)
        {
            RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;
        }

        // ==========================================
        // NAVIGATION & TAB SWITCHING
        // ==========================================
        private async void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavListBox.SelectedItem is ListBoxItem selected)
            {
                string tag = selected.Tag?.ToString();

                if (HomeScrollViewer == null || AllAppsContainer == null || SettingsContainer == null || UploaderContainer == null)
                    return;

                _tileAnimationIndex = 0;

                HomeScrollViewer.Visibility = Visibility.Collapsed;
                AllAppsContainer.Visibility = Visibility.Collapsed;
                AppUpdatesContainer.Visibility = Visibility.Collapsed;
                SettingsContainer.Visibility = Visibility.Collapsed;
                UploaderContainer.Visibility = Visibility.Collapsed;

                FrameworkElement activeView = null;

                switch (tag)
                {
                    case "Home":
                        HeaderTitleText.Text = "Store";

                        TopAppsGridView.ItemsSource = null;
                        TopAppsGridView.ItemsSource = PrismStoreService.TopApps.Take(5).ToList();

                        PrismAppsGridView.ItemsSource = null;
                        PrismAppsGridView.ItemsSource = PrismStoreService.PrismStore.Take(5).ToList();

                        NeonAppsGridView.ItemsSource = null;
                        NeonAppsGridView.ItemsSource = PrismStoreService.NeonStore.Take(5).ToList();

                        HomeScrollViewer.Visibility = Visibility.Visible;
                        activeView = HomeScrollViewer;
                        StartSpotlightRotation();
                        break;

                    case "AllApps":
                        HeaderTitleText.Text = "All Apps";
                        _currentSearchQuery = SearchFilterBox != null ? SearchFilterBox.Text : string.Empty;
                        ApplyFilterAndSort();

                        AllAppsGridView.ItemsSource = null;
                        AllAppsGridView.ItemsSource = _allMergedApps;

                        AllAppsContainer.Visibility = Visibility.Visible;
                        activeView = AllAppsContainer;
                        StopSpotlightRotation();
                        break;

                    case "Updates":
                        HeaderTitleText.Text = "App Updates";
                        if (!_isProcessingUpdateQueue)
                        {
                            await ScanForAppUpdatesAsync();
                        }
                        AppUpdatesContainer.Visibility = Visibility.Visible;
                        activeView = AppUpdatesContainer;
                        StopSpotlightRotation();
                        break;

                    case "Uploader":
                        HeaderTitleText.Text = "App Uploader";
                        bool isAuthed = AuthService.IsLoggedIn && !AuthService.IsTokenExpired();
                        UploaderSubmissionFormPanel.Visibility = isAuthed ? Visibility.Visible : Visibility.Collapsed;
                        UploaderAuthRequiredPanel.Visibility = isAuthed ? Visibility.Collapsed : Visibility.Visible;

                        UploaderContainer.Visibility = Visibility.Visible;
                        activeView = UploaderContainer;
                        StopSpotlightRotation();
                        break;

                    case "Settings":
                        HeaderTitleText.Text = "Settings";
                        UpdateAccountUI();
                        SettingsContainer.Visibility = Visibility.Visible;
                        activeView = SettingsContainer;
                        StopSpotlightRotation();
                        break;
                }

                if (activeView != null)
                {
                    AnimateViewTransition(activeView);
                }

                if (RootSplitView.DisplayMode == SplitViewDisplayMode.Overlay)
                {
                    RootSplitView.IsPaneOpen = false;
                }
            }
        }

        private void UploaderCancelSubmit_Click(object sender, RoutedEventArgs e)
        {
            UploaderSubmitConfirmFlyout.Hide();
        }

        private async void UploaderConfirmSubmit_Click(object sender, RoutedEventArgs e)
        {
            UploaderSubmitConfirmFlyout.Hide();

            if (AuthService.IsTokenExpired())
            {
                await AuthService.LogoutIfExpired();
                UploaderSubmissionFormPanel.Visibility = Visibility.Collapsed;
                UploaderAuthRequiredPanel.Visibility = Visibility.Visible;
                return;
            }

            if (string.IsNullOrWhiteSpace(UploaderTitleBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderCategoryBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderSubtitleBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderPublisherBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderIconUrlBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderScreenshotUrlBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderVersionBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderFrameworkBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderDescriptionBox.Text) ||
                string.IsNullOrWhiteSpace(UploaderDownloadUrlBox.Text))
            {
                await ShowDialogAsync("Missing Information", "Please fill out all fields before submitting.");
                return;
            }

            UploaderSubmitButton.IsEnabled = false;

            var model = new AppSubmissionModel
            {
                Title = UploaderTitleBox.Text.Trim(),
                Category = UploaderCategoryBox.Text.Trim(),
                Subtitle = UploaderSubtitleBox.Text.Trim(),
                Publisher = UploaderPublisherBox.Text.Trim(),
                ImagePath = UploaderIconUrlBox.Text.Trim(),
                DetailImagePath = UploaderScreenshotUrlBox.Text.Trim(),
                Version = UploaderVersionBox.Text.Trim(),
                Framework = UploaderFrameworkBox.Text.Trim(),
                Description = UploaderDescriptionBox.Text.Trim(),
                DownloadUrl = UploaderDownloadUrlBox.Text.Trim()
            };

            string responseJson = await AppSubmissionService.SubmitApp(model);
            UploaderSubmitButton.IsEnabled = true;

            try
            {
                JObject res = JObject.Parse(responseJson);

                if (res["success"] != null && (bool)res["success"])
                {
                    string msg = res["message"]?.ToString() ?? "Your application has been submitted and is pending review.";
                    await ShowDialogAsync("Submission Successful", msg);
                    ClearUploaderForm();
                }
                else
                {
                    string err = res["error"]?.ToString() ?? "An error occurred while submitting the application.";
                    await ShowDialogAsync("Submission Failed", err);
                }
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Error", "Failed to process response: " + ex.Message);
            }
        }

        private void ClearUploaderForm()
        {
            UploaderTitleBox.Text = string.Empty;
            UploaderCategoryBox.Text = string.Empty;
            UploaderSubtitleBox.Text = string.Empty;
            UploaderPublisherBox.Text = string.Empty;
            UploaderIconUrlBox.Text = string.Empty;
            UploaderScreenshotUrlBox.Text = string.Empty;
            UploaderVersionBox.Text = string.Empty;
            UploaderFrameworkBox.Text = string.Empty;
            UploaderDescriptionBox.Text = string.Empty;
            UploaderDownloadUrlBox.Text = string.Empty;
        }

        // ==========================================
        // ACCOUNT & SESSION MANAGEMENT
        // ==========================================
        private void UpdateAccountUI()
        {
            if (AuthService.IsLoggedIn)
            {
                AccountUsernameText.Text = $"Logged in as {AuthService.CurrentUsername}";

                if (AccountAvatarImage != null)
                {
                    if (!string.IsNullOrWhiteSpace(AuthService.CurrentAvatar) &&
                        Uri.TryCreate(AuthService.CurrentAvatar, UriKind.Absolute, out Uri avatarUri))
                    {
                        AccountAvatarImage.Source = new BitmapImage(avatarUri);
                        AccountAvatarImage.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        AccountAvatarImage.Source = null;
                        AccountAvatarImage.Visibility = Visibility.Collapsed;
                    }
                }

                LoggedInProfilePanel.Visibility = Visibility.Visible;
                LoggedOutAuthPanel.Visibility = Visibility.Collapsed;
                UpdateExpiryCountdown();
            }
            else
            {
                StopSessionCountdown();

                if (AccountAvatarImage != null)
                {
                    AccountAvatarImage.Source = null;
                    AccountAvatarImage.Visibility = Visibility.Collapsed;
                }

                LoggedInProfilePanel.Visibility = Visibility.Collapsed;
                LoggedOutAuthPanel.Visibility = Visibility.Visible;
            }
        }

        private async void DiscordLoginButton_Click(object sender, RoutedEventArgs e)
        {
            bool confirmed = await ShowConfirmDialogAsync(
                "Sign in with Discord",
                "PrismStore will open your web browser so you can sign in with Discord and return to the app automatically.",
                "Continue",
                "Cancel");

            if (!confirmed)
                return;

            if (sender is Button btn) btn.IsEnabled = false;

            try
            {
                bool launched = await AuthService.LoginWithDiscordAsync("prismstore");
                if (!launched)
                {
                    await ShowDialogAsync("Discord Sign-In", "Could not open your browser to complete Discord authentication.");
                }
            }
            finally
            {
                if (sender is Button button) button.IsEnabled = true;
            }
        }

        private void StartSessionCountdown()
        {
            StopSessionCountdown();
            UpdateExpiryCountdown();

            _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sessionTimer.Tick += SessionTimer_Tick;
            _sessionTimer.Start();
        }

        private void StopSessionCountdown()
        {
            if (_sessionTimer != null)
            {
                _sessionTimer.Stop();
                _sessionTimer = null;
            }
        }

        private async void SessionTimer_Tick(object sender, object e)
        {
            if (AuthService.IsTokenExpired())
            {
                StopSessionCountdown();
                await AuthService.LogoutIfExpired();
                UpdateAccountUI();
            }
            else
            {
                UpdateExpiryCountdown();
            }
        }

        private void UpdateExpiryCountdown()
        {
            SessionExpiryStatusText.Text = $"Session expires in: {AuthService.GetFormattedRemainingTokenTime()}";
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string result = await AuthService.Login(LoginUsernameBox.Text, LoginPasswordBox.Password);
                JObject obj = JObject.Parse(result);

                if (obj["token"] != null)
                {
                    string token = obj["token"].ToString();
                    string username = obj["username"]?.ToString() ?? LoginUsernameBox.Text;

                    ApplicationData.Current.LocalSettings.Values["token"] = token;
                    ApplicationData.Current.LocalSettings.Values["username"] = username;

                    UpdateAccountUI();
                    StartSessionCountdown();

                    LoginPasswordBox.Password = string.Empty;

                    await ShowDialogAsync("Login Successful", $"Welcome back, {username}!");
                }
                else
                {
                    await ShowDialogAsync("Login Failed", obj["error"]?.ToString() ?? "Invalid credentials.");
                }
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Login Error", ex.Message);
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (RegPasswordBox.Password != RegConfirmPasswordBox.Password)
            {
                await ShowDialogAsync("Registration Error", "Passwords do not match.");
                return;
            }

            try
            {
                string result = await AuthService.Register(RegEmailBox.Text, RegUsernameBox.Text, RegPasswordBox.Password);
                JObject obj = JObject.Parse(result);

                if (obj["success"] != null)
                {
                    LoginUsernameBox.Text = RegUsernameBox.Text;
                    RegPasswordBox.Password = string.Empty;
                    RegConfirmPasswordBox.Password = string.Empty;

                    await ShowDialogAsync("Registration Complete", "Account created successfully! Please sign in.");
                }
                else
                {
                    await ShowDialogAsync("Registration Error", obj["error"]?.ToString() ?? "Registration failed.");
                }
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Registration Error", ex.Message);
            }
        }

        private void SignOut_Click(object sender, RoutedEventArgs e)
        {
            AuthService.Logout();
            StopSessionCountdown();
            UpdateAccountUI();

            LoginUsernameBox.Text = string.Empty;
            LoginPasswordBox.Password = string.Empty;
        }

        // ==========================================
        // UPDATES & ABOUT HANDLERS
        // ==========================================
        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateProgressRing.Visibility = Visibility.Visible;
                UpdateProgressRing.IsActive = true;
                CheckUpdatesButton.IsEnabled = false;

                UpdateStatusText.Text = "Checking for updates...";
                await Task.Delay(600);

                var result = await AutoUpdateService.CheckAsync(showPromptIfAvailable: true);

                switch (result)
                {
                    case UpdateResult.Available:
                        UpdateStatusText.Text = "Update package available!";
                        break;
                    case UpdateResult.UpToDate:
                        UpdateStatusText.Text = "You're running the latest version.";
                        break;
                    case UpdateResult.Failed:
                        UpdateStatusText.Text = "Unable to reach update server.";
                        break;
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "Update check encountered an error.";
                await ShowDialogAsync("Update Error", ex.Message);
            }
            finally
            {
                UpdateProgressRing.IsActive = false;
                UpdateProgressRing.Visibility = Visibility.Collapsed;
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        private async void Discord_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri("https://discord.gg/YBsVhkcHT4"));
        }

        private async void Website_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri("https://rdcubing.github.io/"));
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            if (RefreshBtn != null) RefreshBtn.IsEnabled = false;
            if (RefreshUpdatesButton != null) RefreshUpdatesButton.IsEnabled = false;

            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;
            _tileAnimationIndex = 0;

            try
            {
                bool expired = await AuthService.LogoutIfExpired();
                UpdateAccountUI();
                if (AuthService.IsLoggedIn && !expired)
                {
                    StartSessionCountdown();
                }

                PackageLookupService.InvalidateCache();
                Task warmPackagesTask = PackageLookupService.WarmCacheAsync();

                await Task.WhenAll(
                    PrismStoreService.LoadAsync(),
                    DownloadHistoryService.Instance.LoadAsync(),
                    warmPackagesTask);

                TopAppsGridView.ItemsSource = null;
                TopAppsGridView.ItemsSource = PrismStoreService.TopApps.Take(5).ToList();

                PrismAppsGridView.ItemsSource = null;
                PrismAppsGridView.ItemsSource = PrismStoreService.PrismStore.Take(5).ToList();

                NeonAppsGridView.ItemsSource = null;
                NeonAppsGridView.ItemsSource = PrismStoreService.NeonStore.Take(5).ToList();

                InitializeFilterSubmenus();
                ApplyFilterAndSort();
                SetupSpotlight();

                await ScanForAppUpdatesAsync();
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                if (RefreshBtn != null) RefreshBtn.IsEnabled = true;
                if (RefreshUpdatesButton != null) RefreshUpdatesButton.IsEnabled = true;
            }
        }

        private void AppItem_Click(object sender, ItemClickEventArgs e)
        {
            AppItem targetApp = e.ClickedItem as AppItem ?? (e.ClickedItem as AppUpdateItem)?.App;
            if (targetApp != null)
            {
                Frame.Navigate(typeof(SelectedApp), targetApp);
            }
        }

        // ==========================================
        // ALL APPS FILTERING & SORTING PIPELINE
        // ==========================================
        private void SearchFilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _tileAnimationIndex = 0;
            _currentSearchQuery = sender.Text;
            ApplyFilterAndSort();
        }

        private void InitializeFilterSubmenus()
        {
            _isInitializingFilters = true;

            if (SortComboBox != null) SortComboBox.SelectionChanged -= FilterCriteria_Changed;
            if (CategoryComboBox != null) CategoryComboBox.SelectionChanged -= FilterCriteria_Changed;
            if (PublisherComboBox != null) PublisherComboBox.SelectionChanged -= FilterCriteria_Changed;
            if (PlatformComboBox != null) PlatformComboBox.SelectionChanged -= FilterCriteria_Changed;

            var allApps = PrismStoreService.PrismStore.Concat(PrismStoreService.NeonStore).ToList();

            if (SortComboBox != null)
                SortComboBox.SelectedIndex = 0;

            if (PlatformComboBox != null)
                PlatformComboBox.SelectedIndex = 0;

            if (CategoryComboBox != null)
            {
                CategoryComboBox.Items.Clear();
                CategoryComboBox.Items.Add(new ComboBoxItem { Content = "All Categories", Tag = "All" });

                var categories = allApps
                    .Select(a => a.Category?.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c);

                foreach (var cat in categories)
                {
                    CategoryComboBox.Items.Add(new ComboBoxItem { Content = cat, Tag = cat });
                }
                CategoryComboBox.SelectedIndex = 0;
            }

            if (PublisherComboBox != null)
            {
                PublisherComboBox.Items.Clear();
                PublisherComboBox.Items.Add(new ComboBoxItem { Content = "All Publishers", Tag = "All" });

                var publishers = allApps
                    .Select(a => a.Publisher?.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p);

                foreach (var pub in publishers)
                {
                    PublisherComboBox.Items.Add(new ComboBoxItem { Content = pub, Tag = pub });
                }
                PublisherComboBox.SelectedIndex = 0;
            }

            if (SortComboBox != null) SortComboBox.SelectionChanged += FilterCriteria_Changed;
            if (CategoryComboBox != null) CategoryComboBox.SelectionChanged += FilterCriteria_Changed;
            if (PublisherComboBox != null) PublisherComboBox.SelectionChanged += FilterCriteria_Changed;
            if (PlatformComboBox != null) PlatformComboBox.SelectionChanged += FilterCriteria_Changed;

            _isInitializingFilters = false;
        }

        private void FilterCriteria_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingFilters) return;

            _selectedSort = (SortComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Featured";
            _selectedCategory = (CategoryComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
            _selectedPublisher = (PublisherComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
            _selectedPlatform = (PlatformComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";

            _tileAnimationIndex = 0;
            ApplyFilterAndSort();
        }

        private void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            _isInitializingFilters = true;

            if (SortComboBox != null) SortComboBox.SelectedIndex = 0;
            if (CategoryComboBox != null) CategoryComboBox.SelectedIndex = 0;
            if (PublisherComboBox != null) PublisherComboBox.SelectedIndex = 0;
            if (PlatformComboBox != null) PlatformComboBox.SelectedIndex = 0;

            _selectedSort = "Featured";
            _selectedCategory = "All";
            _selectedPublisher = "All";
            _selectedPlatform = "All";

            _isInitializingFilters = false;

            _tileAnimationIndex = 0;
            ApplyFilterAndSort();
            FilterFlyout?.Hide();
        }

        private void ApplyFilterAndSort()
        {
            IEnumerable<AppItem> query;
            if (_selectedPlatform == "Win10")
                query = PrismStoreService.PrismStore;
            else if (_selectedPlatform == "Win81")
                query = PrismStoreService.NeonStore;
            else
                query = PrismStoreService.PrismStore.Concat(PrismStoreService.NeonStore);

            if (!string.IsNullOrWhiteSpace(_currentSearchQuery))
            {
                string q = _currentSearchQuery.Trim();
                query = query.Where(app =>
                    (app.Title != null && app.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (app.Subtitle != null && app.Subtitle.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (app.Description != null && app.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (app.Publisher != null && app.Publisher.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (app.Category != null && app.Category.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (_selectedCategory != "All")
            {
                query = query.Where(a => string.Equals(a.Category?.Trim(), _selectedCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (_selectedPublisher != "All")
            {
                query = query.Where(a => string.Equals(a.Publisher?.Trim(), _selectedPublisher, StringComparison.OrdinalIgnoreCase));
            }

            switch (_selectedSort)
            {
                case "AlphaAsc":
                    query = query.OrderBy(a => a.Title);
                    break;
                case "AlphaDesc":
                    query = query.OrderByDescending(a => a.Title);
                    break;
                case "TopApps":
                    query = query
                        .OrderByDescending(a => string.Equals(a.TopApp, "Yes", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(a => a.Title);
                    break;
                case "Featured":
                default:
                    query = query.OrderByDescending(a => string.Equals(a.TopApp, "Yes", StringComparison.OrdinalIgnoreCase));
                    break;
            }

            var results = query.ToList();
            _allMergedApps.Clear();
            foreach (var item in results)
            {
                _allMergedApps.Add(item);
            }

            if (AllAppsCountText != null)
            {
                int count = _allMergedApps.Count;
                AllAppsCountText.Text = $"{count} {(count == 1 ? "app" : "apps")}";
            }

            if (AllAppsEmptyPanel != null)
            {
                AllAppsEmptyPanel.Visibility = _allMergedApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}