using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;
using Newtonsoft.Json.Linq;
using PrismStore.Models;
using PrismStore.Services;

namespace PrismStore
{
    public class CategoryItem
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public string CountText => $"({Count})";
    }

    public sealed partial class MainPage : Page
    {
        private ObservableCollection<AppItem> _allMergedApps = new ObservableCollection<AppItem>();
        private ObservableCollection<AppItem> _categoryFilteredApps = new ObservableCollection<AppItem>();
        private ObservableCollection<CategoryItem> _categoriesList = new ObservableCollection<CategoryItem>();

        private int _tileAnimationIndex = 0;
        private DispatcherTimer _sessionTimer;

        // Spotlight Carousel State
        private DispatcherTimer _spotlightTimer;
        private List<AppItem> _spotlightApps = new List<AppItem>();
        private int _currentSpotlightIndex = 0;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;

            CategoryComboBox.ItemsSource = _categoriesList;
            CategoryAppsGridView.ItemsSource = _categoryFilteredApps;

            DownloadsHistoryListView.ItemsSource = DownloadHistoryService.Instance.Downloads;
            DownloadHistoryService.Instance.Downloads.CollectionChanged += (s, e) => UpdateDownloadsUI();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DisableSystemBackButton();
            UpdateDownloadsUI();
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

            bool expired = await AuthService.LogoutIfExpired();
            UpdateAccountUI();

            if (AuthService.IsLoggedIn && !expired)
            {
                StartSessionCountdown();
            }

            if (PrismStoreService.PrismStore.Count == 0 && PrismStoreService.NeonStore.Count == 0)
            {
                LoadingRing.Visibility = Visibility.Visible;
                LoadingRing.IsActive = true;

                await PrismStoreService.LoadAsync();

                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }

            await DownloadHistoryService.Instance.LoadAsync();
            UpdateDownloadsUI();

            _tileAnimationIndex = 0;

            // 1. Bind Storefront collections (limited to 5 items each)
            TopAppsGridView.ItemsSource = PrismStoreService.TopApps.Take(5).ToList();
            PrismAppsGridView.ItemsSource = PrismStoreService.PrismStore.Take(5).ToList();
            NeonAppsGridView.ItemsSource = PrismStoreService.NeonStore.Take(5).ToList();

            // 2. Bind All Apps master collection (shows all items)
            FilterAllApps();
            AllAppsGridView.ItemsSource = _allMergedApps;

            // 3. Populate Categories
            PopulateCategories();

            // 4. Initialize Rotating Spotlight with Windows 10 Apps
            SetupSpotlight();

            // 5. Animate initial home view
            AnimateViewTransition(HomeScrollViewer);
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            StopSessionCountdown();
            StopSpotlightRotation();
        }

        // ==========================================
        // FEATURED SPOTLIGHT BANNER ROTATION
        // ==========================================
        private void SetupSpotlight()
        {
            _spotlightApps.Clear();

            // Prioritize Windows 10 Apps (PrismStore) for spotlight
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
                }
                catch
                {
                    SpotlightHeroImage.Source = null;
                }
            }

            UpdateSpotlightDots(index);

            if (animate)
            {
                Storyboard sb = new Storyboard();
                DoubleAnimation fadeIn = new DoubleAnimation
                {
                    From = 0.3,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(450),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
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

            var accentBrush = (Brush)this.Resources["AppAccent"] ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));
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
            var accentBrush = (Brush)this.Resources["AppAccent"] ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));
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
        // UNIFIED VIEW ZOOM-IN ANIMATION
        // ==========================================
        private void AnimateViewTransition(FrameworkElement container)
        {
            if (container == null) return;

            container.Opacity = 0;
            if (container.RenderTransform is CompositeTransform ct)
            {
                ct.ScaleX = 0.94;
                ct.ScaleY = 0.94;
            }

            Storyboard sb = new Storyboard();

            DoubleAnimation fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, container);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sb.Children.Add(fadeIn);

            DoubleAnimation scaleX = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleX, container.RenderTransform);
            Storyboard.SetTargetProperty(scaleX, "(CompositeTransform.ScaleX)");
            sb.Children.Add(scaleX);

            DoubleAnimation scaleY = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleY, container.RenderTransform);
            Storyboard.SetTargetProperty(scaleY, "(CompositeTransform.ScaleY)");
            sb.Children.Add(scaleY);

            sb.Begin();
        }

        // ==========================================
        // STAGGERED ZOOM-IN STORYBOARDS FOR APP TILES
        // ==========================================
        private void AppTile_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement card)
            {
                double delayMs = Math.Min(_tileAnimationIndex * 35, 500);
                _tileAnimationIndex++;

                Storyboard sb = new Storyboard();
                sb.BeginTime = TimeSpan.FromMilliseconds(delayMs);

                DoubleAnimation fadeIn = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(320),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(fadeIn, card);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                sb.Children.Add(fadeIn);

                DoubleAnimation scaleX = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(350),
                    EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(scaleX, card.RenderTransform);
                Storyboard.SetTargetProperty(scaleX, "(CompositeTransform.ScaleX)");
                sb.Children.Add(scaleX);

                DoubleAnimation scaleY = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(350),
                    EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(scaleY, card.RenderTransform);
                Storyboard.SetTargetProperty(scaleY, "(CompositeTransform.ScaleY)");
                sb.Children.Add(scaleY);

                sb.Begin();
            }
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
        private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavListBox.SelectedItem is ListBoxItem selected)
            {
                string tag = selected.Tag?.ToString();

                if (HomeScrollViewer == null || AllAppsContainer == null || CategoriesContainer == null ||
                    DownloadsContainer == null || SettingsContainer == null || UploaderContainer == null)
                    return;

                _tileAnimationIndex = 0;

                // Reset view visibility
                HomeScrollViewer.Visibility = Visibility.Collapsed;
                AllAppsContainer.Visibility = Visibility.Collapsed;
                CategoriesContainer.Visibility = Visibility.Collapsed;
                DownloadsContainer.Visibility = Visibility.Collapsed;
                SettingsContainer.Visibility = Visibility.Collapsed;
                UploaderContainer.Visibility = Visibility.Collapsed; // Ensures uploader hides when changing tabs

                FrameworkElement activeView = null;

                switch (tag)
                {
                    case "Home":
                        HeaderTitleText.Text = "Store";
                        HomeScrollViewer.Visibility = Visibility.Visible;
                        activeView = HomeScrollViewer;
                        StartSpotlightRotation();
                        break;

                    case "AllApps":
                        HeaderTitleText.Text = "All Apps";
                        string query = SearchFilterBox != null ? SearchFilterBox.Text : string.Empty;
                        FilterAllApps(query);
                        AllAppsContainer.Visibility = Visibility.Visible;
                        activeView = AllAppsContainer;
                        StopSpotlightRotation();
                        break;

                    case "Categories":
                        HeaderTitleText.Text = "Categories";
                        if (CategoryComboBox.SelectedItem == null && _categoriesList.Count > 0)
                        {
                            CategoryComboBox.SelectedIndex = 0;
                        }
                        CategoriesContainer.Visibility = Visibility.Visible;
                        activeView = CategoriesContainer;
                        StopSpotlightRotation();
                        break;

                    case "Downloads":
                        HeaderTitleText.Text = "Downloads";
                        UpdateDownloadsUI();
                        DownloadsContainer.Visibility = Visibility.Visible;
                        activeView = DownloadsContainer;
                        StopSpotlightRotation();
                        break;

                    case "Uploader":
                        HeaderTitleText.Text = "App Uploader";

                        // Check authentication state for uploader form visibility
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
                await new MessageDialog("Please fill out all fields before submitting.", "Missing Information").ShowAsync();
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
                    await new MessageDialog(msg, "Submission Successful").ShowAsync();
                    ClearUploaderForm();
                }
                else
                {
                    string err = res["error"]?.ToString() ?? "An error occurred while submitting the application.";
                    await new MessageDialog(err, "Submission Failed").ShowAsync();
                }
            }
            catch (Exception ex)
            {
                await new MessageDialog("Failed to process response: " + ex.Message, "Error").ShowAsync();
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
        // DOWNLOADS MANAGEMENT
        // ==========================================
        private void UpdateDownloadsUI()
        {
            if (EmptyDownloadsPanel == null || DownloadsHistoryListView == null) return;

            var downloads = DownloadHistoryService.Instance.Downloads;
            DownloadsHistoryListView.ItemsSource = null;
            DownloadsHistoryListView.ItemsSource = downloads;

            bool hasDownloads = downloads != null && downloads.Count > 0;
            DownloadsHistoryListView.Visibility = hasDownloads ? Visibility.Visible : Visibility.Collapsed;
            EmptyDownloadsPanel.Visibility = hasDownloads ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ClearDownloadsHistory_Click(object sender, RoutedEventArgs e)
        {
            DownloadHistoryService.Instance.Clear();
            UpdateDownloadsUI();
        }

        // ==========================================
        // CATEGORIES FILTERING & GROUPING
        // ==========================================
        private void PopulateCategories()
        {
            _categoriesList.Clear();
            var allApps = PrismStoreService.PrismStore.Concat(PrismStoreService.NeonStore).ToList();

            if (allApps.Count == 0)
                return;

            _categoriesList.Add(new CategoryItem
            {
                Name = "All",
                Count = allApps.Count
            });

            var grouped = allApps
                .Where(a => !string.IsNullOrWhiteSpace(a.Category))
                .GroupBy(a => a.Category.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                _categoriesList.Add(new CategoryItem
                {
                    Name = group.Key,
                    Count = group.Count()
                });
            }

            if (_categoriesList.Count > 0)
            {
                CategoryComboBox.SelectedIndex = 0;
                FilterAppsByCategory(_categoriesList[0].Name);
            }
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryComboBox.SelectedItem is CategoryItem selectedCategory)
            {
                _tileAnimationIndex = 0;
                FilterAppsByCategory(selectedCategory.Name);

                if (CategorySubheaderText != null)
                {
                    CategorySubheaderText.Text = selectedCategory.Name == "All"
                        ? $"Showing all {selectedCategory.Count} packages"
                        : $"Showing {selectedCategory.Count} package(s) in {selectedCategory.Name}";
                }
            }
        }

        private void FilterAppsByCategory(string category)
        {
            _categoryFilteredApps.Clear();
            var allApps = PrismStoreService.PrismStore.Concat(PrismStoreService.NeonStore);

            var filtered = string.Equals(category, "All", StringComparison.OrdinalIgnoreCase)
                ? allApps
                : allApps.Where(a => string.Equals(a.Category?.Trim(), category, StringComparison.OrdinalIgnoreCase));

            foreach (var app in filtered)
            {
                _categoryFilteredApps.Add(app);
            }

            if (CategoryEmptyStateText != null)
            {
                CategoryEmptyStateText.Visibility = _categoryFilteredApps.Any() ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // ==========================================
        // ACCOUNT & SESSION MANAGEMENT
        // ==========================================
        private void UpdateAccountUI()
        {
            if (AuthService.IsLoggedIn)
            {
                AccountUsernameText.Text = $"Logged in as {AuthService.CurrentUsername}";
                LoggedInProfilePanel.Visibility = Visibility.Visible;
                LoggedOutAuthPanel.Visibility = Visibility.Collapsed;
                UpdateExpiryCountdown();
            }
            else
            {
                StopSessionCountdown();
                LoggedInProfilePanel.Visibility = Visibility.Collapsed;
                LoggedOutAuthPanel.Visibility = Visibility.Visible;
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

                    await new MessageDialog($"Welcome back, {username}!", "Login Successful").ShowAsync();
                }
                else
                {
                    await new MessageDialog(obj["error"]?.ToString() ?? "Invalid credentials.", "Login Failed").ShowAsync();
                }
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.Message, "Login Error").ShowAsync();
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (RegPasswordBox.Password != RegConfirmPasswordBox.Password)
            {
                await new MessageDialog("Passwords do not match.", "Registration Error").ShowAsync();
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

                    await new MessageDialog("Account created successfully! Please sign in.", "Registration Complete").ShowAsync();
                }
                else
                {
                    await new MessageDialog(obj["error"]?.ToString() ?? "Registration failed.", "Registration Error").ShowAsync();
                }
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.Message, "Registration Error").ShowAsync();
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
        // ALL APPS SEARCH FILTER
        // ==========================================
        private void SearchFilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _tileAnimationIndex = 0;
            FilterAllApps(sender.Text);
        }

        private void FilterAllApps(string query = "")
        {
            _allMergedApps.Clear();
            var merged = PrismStoreService.PrismStore.Concat(PrismStoreService.NeonStore);

            if (!string.IsNullOrWhiteSpace(query))
            {
                merged = merged.Where(a =>
                    (a.Title != null && a.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Subtitle != null && a.Subtitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Framework != null && a.Framework.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            foreach (var item in merged)
            {
                _allMergedApps.Add(item);
            }
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
                await new MessageDialog(ex.Message, "Update Error").ShowAsync();
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
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            _tileAnimationIndex = 0;
            await PrismStoreService.LoadAsync();
            FilterAllApps(SearchFilterBox?.Text ?? string.Empty);
            PopulateCategories();
            SetupSpotlight();

            if (CategoryComboBox.SelectedItem is CategoryItem currentCat)
            {
                FilterAppsByCategory(currentCat.Name);
            }

            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }

        private void AppItem_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AppItem app)
            {
                Frame.Navigate(typeof(SelectedApp), app);
            }
        }
    }
}