using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Management.Deployment;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PrismStore.Models;
using PrismStore.Services;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.ViewManagement;

namespace PrismStore
{
    public sealed partial class SelectedApp : Page
    {
        private DataTransferManager _dataTransferManager;
        private AppItem _currentApp;

        // Pagination State (4 items per page)
        private const int PageSize = 4;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private List<Review> _allReviews = new List<Review>();
        private ObservableCollection<Review> _pagedReviews = new ObservableCollection<Review>();
        private InstalledAppStatus _appStatus;

        private int _reviewAnimationIndex = 0;
        private Review _myExistingReview = null;
        private int _selectedRating = 5;
        private const string AcrylicModeSettingKey = "PrismStore_AcrylicMode";
        private readonly UISettings _uiSettings = new UISettings();
        private bool _isInitializingAcrylicCombo = false;
        public SelectedApp()
        {
            this.InitializeComponent();
            ApplyAcrylicBackgroundsIfSupported();
            SetupCustomTitleBar();
            ReviewsGrid.ItemsSource = _pagedReviews;
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
            if (TopBarGrid != null)
            {
                double rightInset = coreTitleBar.SystemOverlayRightInset > 0
                    ? coreTitleBar.SystemOverlayRightInset
                    : 138;
                TopBarGrid.Padding = new Thickness(0, 0, rightInset, 0);
            }
        }

        // ==========================================
        // PAGE LIFECYCLE
        // ==========================================
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            ApplyAcrylicBackgroundsIfSupported();
            if (e.Parameter is AppItem app)
            {
                _currentApp = app;
                this.DataContext = _currentApp;
            }

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
            navManager.BackRequested += NavManager_BackRequested;

            _dataTransferManager = DataTransferManager.GetForCurrentView();
            _dataTransferManager.DataRequested += DataRequested;

            PlayFadeInAnimation();
            await RefreshInstallStateAsync();

            await AuthService.LogoutIfExpired();
            UpdateReviewFormState();

            LoadReviews();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= NavManager_BackRequested;

            if (_dataTransferManager != null)
            {
                _dataTransferManager.DataRequested -= DataRequested;
            }
            _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;

            base.OnNavigatedFrom(e);
        }

        private async void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ApplyAcrylicBackgroundsIfSupported();
            });
        }

        private void ApplyAcrylicBackgroundsIfSupported()
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush"))
                return;

            string mode = ApplicationData.Current.LocalSettings.Values[AcrylicModeSettingKey] as string ?? "Full";

            bool isLightTheme = ActualTheme == ElementTheme.Light;
            if (ActualTheme == ElementTheme.Default)
            {
                var bgColor = _uiSettings.GetColorValue(UIColorType.Background);
                isLightTheme = bgColor.R > 128 && bgColor.G > 128 && bgColor.B > 128;
            }

            var defaultPageBrush = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
            var defaultChromeBrush = Application.Current.Resources["SystemControlBackgroundAltHighBrush"] as Brush;

            if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
            {
                if (RootPageGrid != null) RootPageGrid.Background = defaultPageBrush;
                if (TopBarGrid != null) TopBarGrid.Background = defaultChromeBrush;
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

            bool isSemi = string.Equals(mode, "Semi", StringComparison.OrdinalIgnoreCase);

            // Full acrylic applies HostBackdrop to RootPageGrid; Semi keeps the solid page background
            if (RootPageGrid != null)
            {
                RootPageGrid.Background = isSemi ? defaultPageBrush : mainWindowBrush;
            }

            // TopBarGrid gets acrylic in both Full and Semi modes
            if (TopBarGrid != null)
            {
                TopBarGrid.Background = chromeBrush;
            }
        }

        private void NavManager_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (!e.Handled && Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        // ==========================================
        // SIMPLIFIED OPACITY-ONLY ANIMATIONS
        // ==========================================
        private void PlayFadeInAnimation()
        {
            var sb = new Storyboard();
            AddFadeIn(sb, HeroSection, 0);
            AddFadeIn(sb, DetailsSection, 50);
            AddFadeIn(sb, MetadataSection, 100);
            AddFadeIn(sb, ReviewsSection, 150);
            sb.Begin();
        }

        private void AddFadeIn(Storyboard sb, UIElement element, double delayMs)
        {
            if (element == null) return;

            var fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };
            Storyboard.SetTarget(fadeIn, element);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sb.Children.Add(fadeIn);
        }

        private void ReviewCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement card)
            {
                double delayMs = Math.Min(_reviewAnimationIndex * 20, 200);
                _reviewAnimationIndex++;

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
        // INSTALL STATE & PACKAGE MANAGEMENT
        // ==========================================
        private async Task RefreshInstallStateAsync()
        {
            if (_currentApp == null)
                return;

            _appStatus = await PackageLookupService.GetAppStatusAsync(_currentApp);

            switch (_appStatus.State)
            {
                case AppInstallState.UpdateAvailable:
                    DownloadButton.Content = "Update";
                    DownloadProgressPanel.Visibility = Visibility.Visible;
                    DownloadProgressBar.Visibility = Visibility.Collapsed;
                    DownloadStatusText.Text = $"Update available (Installed: v{_appStatus.InstalledVersionText} → Store: v{_currentApp.Version})";
                    ForceUpdateMenuItem.Text = "Force Reinstall";
                    ForceUpdateMenuItem.Visibility = Visibility.Visible;
                    UninstallMenuItem.Visibility = Visibility.Visible;
                    InstalledActionsSeparator.Visibility = Visibility.Visible;
                    break;

                case AppInstallState.UpToDate:
                    DownloadButton.Content = "Open";
                    DownloadProgressPanel.Visibility = Visibility.Visible;
                    DownloadProgressBar.Visibility = Visibility.Collapsed;
                    DownloadStatusText.Text = $"Installed (v{_appStatus.InstalledVersionText})";
                    ForceUpdateMenuItem.Text = "Force Update / Reinstall";
                    ForceUpdateMenuItem.Visibility = Visibility.Visible;
                    UninstallMenuItem.Visibility = Visibility.Visible;
                    InstalledActionsSeparator.Visibility = Visibility.Visible;
                    break;

                case AppInstallState.InstalledNewer:
                    DownloadButton.Content = "Open";
                    DownloadProgressPanel.Visibility = Visibility.Visible;
                    DownloadProgressBar.Visibility = Visibility.Collapsed;
                    DownloadStatusText.Text = $"Installed v{_appStatus.InstalledVersionText} (Newer than Store v{_currentApp.Version})";
                    ForceUpdateMenuItem.Text = "Force Downgrade to Store Version";
                    ForceUpdateMenuItem.Visibility = Visibility.Visible;
                    UninstallMenuItem.Visibility = Visibility.Visible;
                    InstalledActionsSeparator.Visibility = Visibility.Visible;
                    break;

                case AppInstallState.NotInstalled:
                default:
                    DownloadButton.Content = "Get / Download";
                    DownloadProgressBar.Visibility = Visibility.Visible;
                    DownloadProgressPanel.Visibility = Visibility.Collapsed;
                    ForceUpdateMenuItem.Visibility = Visibility.Collapsed;
                    UninstallMenuItem.Visibility = Visibility.Collapsed;
                    InstalledActionsSeparator.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private async Task<bool> SideloadPackageNativelyAsync(StorageFile packageFile, AppItem app, bool isForceUpdate, bool allowDowngrade)
        {
            if (packageFile == null) return false;

            if (!CertificateCheckHelper.IsWindowsAppPackage(packageFile))
            {
                await ShowDialogAsync(
                    "Unsupported Package Type",
                    $"'{packageFile.Name}' is not a Windows app package (.appx, .appxbundle, .msix, or .msixbundle).");
                return false;
            }

            DownloadProgressBar.IsIndeterminate = true;
            DownloadStatusText.Text = isForceUpdate
                ? $"Updating {app?.Title ?? packageFile.Name}..."
                : $"Installing {app?.Title ?? packageFile.Name}...";

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
                    else if (_appStatus?.InstalledPackage != null)
                    {
                        DownloadStatusText.Text = "Removing newer installed version...";
                        await PackageLookupService.UninstallPackageAsync(_appStatus.InstalledPackage);
                    }
                }

                var deployOperation = packageManager.AddPackageAsync(
                    new Uri(packageFile.Path),
                    null,
                    options);

                var tcs = new TaskCompletionSource<bool>();
                string deployError = null;
                int deployHr = 0;

                deployOperation.Progress = (info, progress) =>
                {
                    var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        DownloadProgressBar.IsIndeterminate = false;
                        DownloadProgressBar.Value = progress.percentage;
                        DownloadStatusText.Text = isForceUpdate
                            ? $"Updating... {progress.percentage}%"
                            : $"Installing... {progress.percentage}%";
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
                DownloadProgressBar.IsIndeterminate = false;

                if (installed)
                {
                    DownloadProgressBar.Value = 100;
                    DownloadStatusText.Text = isForceUpdate ? "App updated successfully" : "App installed successfully";
                    return true;
                }
                if (CertificateCheckHelper.IsCertificateDeploymentError(deployHr, deployError))
                {
                    string publisher = await CertificateCheckHelper.ExtractPackagePublisherAsync(packageFile);
                    await CertificateCheckHelper.ShowInstallCertificateDialogAsync(packageFile, publisher);
                    return false;
                }

                DownloadStatusText.Text = $"Install failed: {deployError}";
                await ShowDialogAsync(
                    "Deployment Failed",
                    $"Could not deploy \"{app?.Title ?? packageFile.Name}\".\n\nReason: {deployError}");
                return false;
            }
            catch (Exception ex)
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadStatusText.Text = $"Install error: {ex.Message}";
                await ShowDialogAsync("Installation Error", ex.Message);
                return false;
            }
        }

        // ==========================================
        // TOKEN & AUTH STATE CHECK
        // ==========================================
        private void UpdateReviewFormState()
        {
            bool isTokenValid = AuthService.IsLoggedIn && !AuthService.IsTokenExpired();
            ReviewFormPanel.Visibility = isTokenValid ? Visibility.Visible : Visibility.Collapsed;
            SignInToReviewPanel.Visibility = isTokenValid ? Visibility.Collapsed : Visibility.Visible;

            if (!isTokenValid)
                return;

            if (_myExistingReview != null)
            {
                if (WriteReviewTitle != null) WriteReviewTitle.Text = "Your review";
                ReviewCommentBox.Text = _myExistingReview.comment ?? string.Empty;
                _selectedRating = Math.Max(1, Math.Min(5, _myExistingReview.rating));
                UpdateInputStarDisplay(_selectedRating);

                SubmitReviewBtn.Visibility = Visibility.Collapsed;
                ExistingReviewActionsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                if (WriteReviewTitle != null) WriteReviewTitle.Text = "Write a review";
                _selectedRating = 5;
                UpdateInputStarDisplay(_selectedRating);

                SubmitReviewBtn.Visibility = Visibility.Visible;
                ExistingReviewActionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void RatingStar_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is TextBlock clickedStar && clickedStar.Tag != null &&
                int.TryParse(clickedStar.Tag.ToString(), out int rating))
            {
                _selectedRating = rating;
                UpdateInputStarDisplay(_selectedRating);
            }
        }

        private void UpdateInputStarDisplay(int rating)
        {
            var stars = new[] { InputStar1, InputStar2, InputStar3, InputStar4, InputStar5 };
            var activeBrush = Application.Current.Resources["SystemControlForegroundAccentBrush"] as Brush
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));
            var inactiveBrush = Application.Current.Resources["SystemControlForegroundBaseMediumBrush"] as Brush
                ?? new SolidColorBrush(Windows.UI.Colors.Gray);

            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null) continue;

                if (i < rating)
                {
                    stars[i].Text = "\uE1CF"; // Filled star
                    stars[i].Foreground = activeBrush;
                    stars[i].Opacity = 1.0;
                }
                else
                {
                    stars[i].Text = "\uE1CE"; // Outline star
                    stars[i].Foreground = inactiveBrush;
                    stars[i].Opacity = 0.45;
                }
            }
        }

        // ==========================================
        // DOWNLOAD HANDLERS
        // ==========================================
        private async void InstantDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp == null)
                return;

            if (_appStatus != null &&
                (_appStatus.State == AppInstallState.UpToDate || _appStatus.State == AppInstallState.InstalledNewer) &&
                _appStatus.InstalledPackage != null)
            {
                await PackageLookupService.LaunchPackageAsync(_appStatus.InstalledPackage);
                return;
            }

            bool isUpdate = _appStatus != null && _appStatus.State == AppInstallState.UpdateAvailable;
            await DownloadAndInstallAsync(forceRedownload: isUpdate, isForceUpdate: isUpdate, allowDowngrade: false);
        }

        private async void ForceUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp == null)
                return;

            bool isDowngrade = _appStatus != null && _appStatus.State == AppInstallState.InstalledNewer;
            await DownloadAndInstallAsync(forceRedownload: true, isForceUpdate: true, allowDowngrade: isDowngrade);
        }

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_appStatus?.InstalledPackage == null)
                return;

            bool confirmed = await ShowConfirmDialogAsync(
                "Confirm Uninstall",
                $"Uninstall \"{_currentApp.Title}\" (v{_appStatus.InstalledVersionText})?",
                "Uninstall",
                "Cancel");

            if (!confirmed)
                return;

            DownloadButton.IsEnabled = false;
            DownloadProgressRing.Visibility = Visibility.Visible;
            DownloadProgressRing.IsActive = true;
            DownloadStatusText.Text = "Uninstalling...";

            bool removed = await PackageLookupService.UninstallPackageAsync(_appStatus.InstalledPackage);

            DownloadProgressRing.IsActive = false;
            DownloadProgressRing.Visibility = Visibility.Collapsed;
            DownloadButton.IsEnabled = true;

            if (removed)
            {
                ToastService.Show("App uninstalled", _currentApp.Title, _currentApp.ImagePath);
                await RefreshInstallStateAsync();
            }
            else
            {
                await ShowDialogAsync("Uninstall Error", "Failed to uninstall package.");
            }
        }

        private async Task DownloadAndInstallAsync(bool forceRedownload, bool isForceUpdate, bool allowDowngrade)
        {
            if (_currentApp == null || string.IsNullOrEmpty(_currentApp.DownloadUrl))
                return;

            DownloadButton.IsEnabled = false;
            DownloadProgressRing.Visibility = Visibility.Visible;
            DownloadProgressRing.IsActive = true;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            DownloadProgressBar.Visibility = Visibility.Visible;
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 0;
            DownloadStatusText.Text = "Checking local files...";

            try
            {
                Uri uri = new Uri(_currentApp.DownloadUrl);
                string fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName))
                    fileName = _currentApp.Title + ".bin";

                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                StorageFolder packagesFolder = await localFolder.CreateFolderAsync(
                    "Packages",
                    CreationCollisionOption.OpenIfExists);

                StorageFile file = null;
                IStorageItem existingItem = await packagesFolder.TryGetItemAsync(fileName);
                bool alreadyDownloaded = false;

                if (!forceRedownload && existingItem is StorageFile existingFile)
                {
                    var basicProps = await existingFile.GetBasicPropertiesAsync();
                    if (basicProps.Size > 0)
                    {
                        file = existingFile;
                        alreadyDownloaded = true;
                        DownloadProgressBar.Value = 100;
                        DownloadStatusText.Text = "Using existing download";
                    }
                }

                // 1. Download the package first
                if (!alreadyDownloaded)
                {
                    ToastService.Show(isForceUpdate ? "Downloading update" : "Download started", _currentApp.Title, _currentApp.ImagePath);
                    DownloadStatusText.Text = "Preparing download...";

                    file = await packagesFolder.CreateFileAsync(
                        fileName,
                        CreationCollisionOption.ReplaceExisting);

                    await DownloadFileAsync(uri, file);

                    ToastService.Show("Download complete", _currentApp.Title, _currentApp.ImagePath);
                }

                DownloadHistoryService.Instance.Add(new DownloadItem
                {
                    Title = _currentApp.Title,
                    FileName = file.Name,
                    DownloadUrl = _currentApp.DownloadUrl,
                    ImagePath = _currentApp.ImagePath,
                    DownloadedAt = DateTime.Now
                });

                // 2. If not a Windows app package (.appx/.appxbundle/.msix/.msixbundle), open directly
                if (!CertificateCheckHelper.IsWindowsAppPackage(file))
                {
                    DownloadStatusText.Text = "Opening downloaded file...";
                    await Launcher.LaunchFileAsync(file);
                    return;
                }

                // 3. Inspect downloaded .appx/.msix certificate before sideloading
                DownloadStatusText.Text = "Verifying package certificate...";

                // 4. Sideload natively
                bool installed = await SideloadPackageNativelyAsync(file, _currentApp, isForceUpdate, allowDowngrade);
                if (installed)
                {
                    PackageLookupService.InvalidateCache();
                    ToastService.Show(
                        isForceUpdate ? "App updated successfully" : "App installed successfully",
                        _currentApp.Title,
                        _currentApp.ImagePath);
                    await RefreshInstallStateAsync();
                }
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Download Failed", ex.Message);
            }
            finally
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressRing.IsActive = false;
                DownloadProgressRing.Visibility = Visibility.Collapsed;
                DownloadButton.IsEnabled = true;
            }
        }

        private async void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp == null || string.IsNullOrEmpty(_currentApp.DownloadUrl))
                return;

            try
            {
                Uri uri = new Uri(_currentApp.DownloadUrl);
                var picker = new Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads
                };

                string extension = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(extension))
                    extension = ".bin";

                picker.FileTypeChoices.Add("File", new List<string> { extension });
                picker.SuggestedFileName = Path.GetFileName(uri.LocalPath) ?? _currentApp.Title;

                StorageFile file = await picker.PickSaveFileAsync();
                if (file == null)
                    return;

                ToastService.Show("Download started", _currentApp.Title, _currentApp.ImagePath);

                DownloadProgressRing.Visibility = Visibility.Visible;
                DownloadProgressRing.IsActive = true;
                DownloadProgressPanel.Visibility = Visibility.Visible;
                DownloadProgressBar.Visibility = Visibility.Visible;
                DownloadProgressBar.Value = 0;

                await DownloadFileAsync(uri, file);

                ToastService.Show("Download complete", _currentApp.Title, _currentApp.ImagePath);

                bool openFile = await ShowConfirmDialogAsync(
                    "Download Complete",
                    $"\"{file.Name}\" downloaded successfully.",
                    "Open",
                    "Close");

                if (openFile)
                {
                    await Launcher.LaunchFileAsync(file);
                }
            }
            catch (Exception ex)
            {
                ToastService.Show("Download Failed", ex.Message);
                await ShowDialogAsync("Error", ex.Message);
            }
            finally
            {
                DownloadProgressRing.IsActive = false;
                DownloadProgressRing.Visibility = Visibility.Collapsed;
            }
        }

        private async Task DownloadFileAsync(Uri uri, StorageFile file)
        {
            using (HttpClient client = new HttpClient())
            {
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
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
                            DownloadProgressBar.Value = percent;
                            DownloadStatusText.Text = $"{downloadedBytes / 1024d / 1024d:F1} MB / {totalBytes / 1024d / 1024d:F1} MB ({percent:F0}%)";
                        }
                    }

                    await writer.FlushAsync();
                    DownloadProgressBar.Value = 100;
                }
            }
        }

        // ==========================================
        // PAGINATED REVIEWS & RATINGS
        // ==========================================
        private async void LoadReviews()
        {
            if (_currentApp == null || string.IsNullOrEmpty(_currentApp.Id))
                return;

            try
            {
                string json = await ReviewService.GetReviews(_currentApp.Id);
                var arr = JArray.Parse(json);
                _allReviews.Clear();
                _myExistingReview = null;

                var currentUserId = ApplicationData.Current.LocalSettings.Values["userId"] as string;
                var currentUsername = AuthService.CurrentUsername;

                foreach (var item in arr)
                {
                    string rawAvatar = item["avatar"]?.ToString();
                    var r = new Review
                    {
                        _id = item["_id"]?.ToString(),
                        userId = item["userId"]?.ToString(),
                        username = item["username"]?.ToString(),
                        avatar = string.IsNullOrWhiteSpace(rawAvatar) ? null : rawAvatar,
                        rating = int.Parse(item["rating"]?.ToString() ?? "0"),
                        comment = item["comment"]?.ToString(),
                        appId = item["appId"]?.ToString(),
                        createdAt = DateTime.TryParse(item["createdAt"]?.ToString(), out var dtCreate) ? dtCreate : DateTime.MinValue,
                        updatedAt = DateTime.TryParse(item["updatedAt"]?.ToString(), out var dtUpdate) ? dtUpdate : DateTime.MinValue
                    };

                    if ((!string.IsNullOrEmpty(currentUserId) && string.Equals(r.userId, currentUserId, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(currentUsername) && string.Equals(r.username, currentUsername, StringComparison.OrdinalIgnoreCase)))
                    {
                        _myExistingReview = r;
                    }

                    _allReviews.Add(r);
                }

                UpdateReviewStatistics(_allReviews);
                UpdateReviewFormState();

                _currentPage = 1;
                RenderCurrentPage();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[LOAD REVIEWS ERROR] " + ex.Message);
            }
        }

        private void RenderCurrentPage()
        {
            _reviewAnimationIndex = 0;
            _pagedReviews.Clear();

            if (_allReviews.Count == 0)
            {
                PaginationPanel.Visibility = Visibility.Collapsed;
                EmptyStateText.Visibility = Visibility.Visible;
                return;
            }

            EmptyStateText.Visibility = Visibility.Collapsed;
            _totalPages = (int)Math.Ceiling((double)_allReviews.Count / PageSize);
            _currentPage = Math.Max(1, Math.Min(_currentPage, _totalPages));

            var pageItems = _allReviews.Skip((_currentPage - 1) * PageSize).Take(PageSize);
            foreach (var r in pageItems)
            {
                _pagedReviews.Add(r);
            }

            PageNumberText.Text = $"Page {_currentPage} of {_totalPages}";
            PrevPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < _totalPages;
            PaginationPanel.Visibility = _totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                RenderCurrentPage();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                RenderCurrentPage();
            }
        }

        private void UpdateReviewStatistics(IEnumerable<Review> reviews)
        {
            var list = reviews?.ToList() ?? new List<Review>();
            int count = list.Count;
            ReviewCountText.Text = $"{count} {(count == 1 ? "review" : "reviews")}";

            if (count == 0)
            {
                AverageRatingText.Text = "0.0";
                UpdateOverallStarDisplay(0);
                ResetRatingBars();
                return;
            }

            var parsedRatings = new List<double>();
            foreach (var rev in list)
            {
                double val = rev.rating;
                if (val > 5.0 && val <= 10.0) val /= 2.0;
                else if (val > 10.0) val = (val / 100.0) * 5.0;
                parsedRatings.Add(val);
            }

            double average = parsedRatings.Average();
            AverageRatingText.Text = average.ToString("0.0");
            UpdateOverallStarDisplay(average);

            int count5 = parsedRatings.Count(r => r >= 4.5);
            int count4 = parsedRatings.Count(r => r >= 3.5 && r < 4.5);
            int count3 = parsedRatings.Count(r => r >= 2.5 && r < 3.5);
            int count2 = parsedRatings.Count(r => r >= 1.5 && r < 2.5);
            int count1 = parsedRatings.Count(r => r < 1.5);

            Count5Text.Text = count5.ToString();
            Count4Text.Text = count4.ToString();
            Count3Text.Text = count3.ToString();
            Count2Text.Text = count2.ToString();
            Count1Text.Text = count1.ToString();

            const double MaxBarWidth = 140.0;
            Bar5.Width = Math.Max(0, ((double)count5 / count) * MaxBarWidth);
            Bar4.Width = Math.Max(0, ((double)count4 / count) * MaxBarWidth);
            Bar3.Width = Math.Max(0, ((double)count3 / count) * MaxBarWidth);
            Bar2.Width = Math.Max(0, ((double)count2 / count) * MaxBarWidth);
            Bar1.Width = Math.Max(0, ((double)count1 / count) * MaxBarWidth);
        }

        private void ResetRatingBars()
        {
            Count5Text.Text = Count4Text.Text = Count3Text.Text = Count2Text.Text = Count1Text.Text = "0";
            Bar5.Width = Bar4.Width = Bar3.Width = Bar2.Width = Bar1.Width = 0;
        }

        private void UpdateOverallStarDisplay(double rating)
        {
            int filledStars = (int)Math.Round(rating);
            var stars = new[] { Star1, Star2, Star3, Star4, Star5 };
            var activeBrush = Application.Current.Resources["SystemControlForegroundAccentBrush"] as Brush
                ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));
            var inactiveBrush = Application.Current.Resources["SystemControlForegroundBaseMediumBrush"] as Brush
                ?? new SolidColorBrush(Windows.UI.Colors.Gray);

            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null) continue;

                if (i < filledStars)
                {
                    stars[i].Text = "\uE1CF";
                    stars[i].Foreground = activeBrush;
                    stars[i].Opacity = 1.0;
                }
                else
                {
                    stars[i].Text = "\uE1CE";
                    stars[i].Foreground = inactiveBrush;
                    stars[i].Opacity = 0.45;
                }
            }
        }

        private async void SubmitReview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (await AuthService.LogoutIfExpired() || !AuthService.IsLoggedIn)
                {
                    UpdateReviewFormState();
                    await ShowDialogAsync("Sign In Required", "Your session has expired. Please sign in again.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(ReviewCommentBox.Text))
                    return;

                bool isUpdating = _myExistingReview != null;

                var bodyObj = new
                {
                    appId = _currentApp.Id,
                    rating = _selectedRating,
                    comment = ReviewCommentBox.Text.Trim()
                };

                string json = JsonConvert.SerializeObject(bodyObj);
                await ReviewService.SubmitReview(AuthService.CurrentToken, json);

                LoadReviews();

                await ShowDialogAsync(
                    isUpdating ? "Review Updated" : "Review Submitted",
                    isUpdating ? "Your review has been updated!" : "Thank you for your feedback!");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Submission Error", ex.Message);
            }
        }

        private async void DeleteReview_Click(object sender, RoutedEventArgs e)
        {
            if (_myExistingReview == null)
                return;

            if (await AuthService.LogoutIfExpired() || !AuthService.IsLoggedIn)
            {
                UpdateReviewFormState();
                await ShowDialogAsync("Sign In Required", "Your session has expired. Please sign in again.");
                return;
            }

            bool confirmed = await ShowConfirmDialogAsync(
                "Delete Review",
                "Are you sure you want to delete your review?",
                "Delete",
                "Cancel");

            if (!confirmed)
                return;

            string targetId = !string.IsNullOrWhiteSpace(_myExistingReview.appId)
                ? _myExistingReview.appId
                : _currentApp.Id;

            bool deleted = await ReviewService.DeleteReview(AuthService.CurrentToken, targetId);
            if (deleted)
            {
                ReviewCommentBox.Text = string.Empty;
                _selectedRating = 5;
                _myExistingReview = null;
                LoadReviews();
                await ShowDialogAsync("Review Deleted", "Your review has been removed.");
            }
            else
            {
                await ShowDialogAsync("Delete Failed", "Could not delete your review. Please try again.");
            }
        }

        private async void ReviewsGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (!(e.ClickedItem is Review review))
                return;

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avatarGrid = new Grid
            {
                Width = 48,
                Height = 48,
                Background = Application.Current.Resources["SystemControlBackgroundAccentBrush"] as Brush,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            avatarGrid.Children.Add(new FontIcon
            {
                Glyph = "\uE77B",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (!string.IsNullOrWhiteSpace(review.avatar) &&
                Uri.TryCreate(review.avatar, UriKind.Absolute, out Uri avatarUri))
            {
                avatarGrid.Children.Add(new Image
                {
                    Source = new BitmapImage(avatarUri),
                    Stretch = Stretch.UniformToFill,
                    Width = 48,
                    Height = 48
                });
            }

            var metaStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            metaStack.Children.Add(new TextBlock
            {
                Text = review.username ?? "Anonymous",
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                FontSize = 16
            });
            metaStack.Children.Add(new TextBlock
            {
                Text = $"★ {review.rating}  •  {review.updatedAt:MMM d, yyyy}",
                Foreground = Application.Current.Resources["SystemControlForegroundAccentBrush"] as Brush,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            });

            Grid.SetColumn(avatarGrid, 0);
            Grid.SetColumn(metaStack, 1);
            headerGrid.Children.Add(avatarGrid);
            headerGrid.Children.Add(metaStack);

            var bodyScroll = new ScrollViewer
            {
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = review.comment ?? string.Empty,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            var contentStack = new StackPanel();
            contentStack.Children.Add(headerGrid);
            contentStack.Children.Add(bodyScroll);

            var dialog = new ContentDialog
            {
                Title = "Review Details",
                Content = contentStack
            };

            if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.ContentDialog", "CloseButtonText"))
                dialog.CloseButtonText = "Close";
            else
                dialog.PrimaryButtonText = "Close";

            await dialog.ShowAsync();
        }

        // ==========================================
        // SHARE & LAUNCH
        // ==========================================
        private void Share_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager.ShowShareUI();
        }

        private void DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (_currentApp == null)
                return;

            var request = args.Request;
            request.Data.Properties.Title = _currentApp.Title;
            request.Data.Properties.Description = _currentApp.Subtitle ?? _currentApp.Description;
            request.Data.SetText($"{_currentApp.Title}\n{_currentApp.Description}\n\nDownload: {_currentApp.DownloadUrl}");

            if (!string.IsNullOrEmpty(_currentApp.DownloadUrl))
            {
                request.Data.SetWebLink(new Uri(_currentApp.DownloadUrl));
            }
        }

        private async void OpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp != null && !string.IsNullOrWhiteSpace(_currentApp.DownloadUrl))
            {
                await Launcher.LaunchUriAsync(new Uri(_currentApp.DownloadUrl));
            }
        }
    }
}