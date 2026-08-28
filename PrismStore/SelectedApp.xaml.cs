using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
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

namespace PrismStore
{
    public sealed partial class SelectedApp : Page
    {
        private DataTransferManager _dataTransferManager;
        private AppItem _currentApp;

        // 2-Column Pagination State (4 items per page)
        private const int PageSize = 4;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private List<Review> _allReviews = new List<Review>();
        private ObservableCollection<Review> _pagedReviews = new ObservableCollection<Review>();

        // Animation Stagger
        private int _reviewAnimationIndex = 0;

        public SelectedApp()
        {
            this.InitializeComponent();
            ReviewsGrid.ItemsSource = _pagedReviews;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is AppItem app)
            {
                _currentApp = app;
                this.DataContext = _currentApp;
            }

            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            navManager.BackRequested += NavManager_BackRequested;

            _dataTransferManager = DataTransferManager.GetForCurrentView();
            _dataTransferManager.DataRequested += DataRequested;

            // Trigger Section-by-Section Staggered Zoom Animations
            PlayStaggeredZoomInAnimation();

            await AuthService.LogoutIfExpired();
            UpdateReviewFormState();

            LoadReviews();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            var navManager = SystemNavigationManager.GetForCurrentView();
            navManager.BackRequested -= NavManager_BackRequested;

            if (_dataTransferManager != null)
            {
                _dataTransferManager.DataRequested -= DataRequested;
            }

            base.OnNavigatedFrom(e);
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
        // SECTION-BY-SECTION STAGGERED ZOOM ANIMATIONS
        // ==========================================
        private void PlayStaggeredZoomInAnimation()
        {
            Storyboard masterSb = new Storyboard();

            // 1. Hero Header (Delay 0ms)
            AddSectionZoomAnimation(masterSb, HeroSection, 0);

            // 2. Details & Screenshot (Delay 70ms)
            AddSectionZoomAnimation(masterSb, DetailsSection, 70);

            // 3. Metadata Sidebar (Delay 140ms)
            AddSectionZoomAnimation(masterSb, MetadataSection, 140);

            // 4. Ratings & Reviews Section (Delay 210ms)
            AddSectionZoomAnimation(masterSb, ReviewsSection, 210);

            masterSb.Begin();
        }

        private void AddSectionZoomAnimation(Storyboard sb, UIElement element, double delayMs)
        {
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(320),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, element);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sb.Children.Add(fadeIn);

            DoubleAnimation scaleX = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(350),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleX, element.RenderTransform);
            Storyboard.SetTargetProperty(scaleX, "(CompositeTransform.ScaleX)");
            sb.Children.Add(scaleX);

            DoubleAnimation scaleY = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(350),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleY, element.RenderTransform);
            Storyboard.SetTargetProperty(scaleY, "(CompositeTransform.ScaleY)");
            sb.Children.Add(scaleY);
        }

        private void ReviewCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement card)
            {
                double delayMs = Math.Min(_reviewAnimationIndex * 40, 240);
                _reviewAnimationIndex++;

                Storyboard sb = new Storyboard { BeginTime = TimeSpan.FromMilliseconds(delayMs) };

                DoubleAnimation fadeIn = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(300),
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
        // TOKEN & AUTH STATE CHECK
        // ==========================================
        private void UpdateReviewFormState()
        {
            bool isTokenValid = AuthService.IsLoggedIn && !AuthService.IsTokenExpired();

            ReviewFormPanel.Visibility = isTokenValid ? Visibility.Visible : Visibility.Collapsed;
            SignInToReviewPanel.Visibility = isTokenValid ? Visibility.Collapsed : Visibility.Visible;
        }

        // ==========================================
        // DOWNLOAD HANDLERS
        // ==========================================
        private async void InstantDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp == null || string.IsNullOrEmpty(_currentApp.DownloadUrl))
                return;

            // ✅ Notification: download started
            ToastService.Show("Download started", _currentApp.Title, _currentApp.ImagePath);

            DownloadProgressRing.Visibility = Visibility.Visible;
            DownloadProgressRing.IsActive = true;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            DownloadProgressBar.Value = 0;
            DownloadStatusText.Text = "Preparing download...";

            try
            {
                Uri uri = new Uri(_currentApp.DownloadUrl);
                string fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName))
                    fileName = _currentApp.Title + ".bin";

                StorageFile file = await DownloadsFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);

                DownloadHistoryService.Instance.Add(new DownloadItem
                {
                    Title = _currentApp.Title,
                    FileName = file.Name,
                    DownloadUrl = _currentApp.DownloadUrl,
                    ImagePath = _currentApp.ImagePath,
                    DownloadedAt = DateTime.Now
                });

                await DownloadFileAsync(uri, file);

                // ✅ Notification: download completed
                ToastService.Show("Download complete", _currentApp.Title, _currentApp.ImagePath);

                await Launcher.LaunchFileAsync(file);
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.Message, "Download Failed").ShowAsync();
            }
            finally
            {
                DownloadProgressRing.IsActive = false;
                DownloadProgressRing.Visibility = Visibility.Collapsed;
                DownloadStatusText.Text = "Ready";
            }
        }

        private async void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp == null || string.IsNullOrEmpty(_currentApp.DownloadUrl))
                return;

            // ✅ Notification: download started
            ToastService.Show("Download started", _currentApp.Title, _currentApp.ImagePath);

            // 1. Create the download record
            var downloadEntry = new DownloadItem
            {
                Title = _currentApp.Title,
                FileName = $"{_currentApp.Title}.appx",
                DownloadUrl = _currentApp.DownloadUrl,
                ImagePath = _currentApp.ImagePath,
                DownloadedAt = DateTime.Now
            };

            try
            {
                Uri uri = new Uri(_currentApp.DownloadUrl);
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;

                string extension = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(extension))
                    extension = ".bin";

                picker.FileTypeChoices.Add("File", new List<string> { extension });
                picker.SuggestedFileName = Path.GetFileName(uri.LocalPath) ?? _currentApp.Title;

                StorageFile file = await picker.PickSaveFileAsync();
                if (file == null)
                    return;

                DownloadProgressRing.Visibility = Visibility.Visible;
                DownloadProgressRing.IsActive = true;
                DownloadProgressPanel.Visibility = Visibility.Visible;

                DownloadHistoryService.Instance.Add(new DownloadItem
                {
                    Title = _currentApp.Title,
                    FileName = file.Name,
                    DownloadUrl = _currentApp.DownloadUrl,
                    ImagePath = _currentApp.ImagePath,
                    DownloadedAt = DateTime.Now
                });

                await DownloadFileAsync(uri, file);

                // ✅ Notification: download completed
                ToastService.Show("Download complete", _currentApp.Title, _currentApp.ImagePath);

                var dialog = new MessageDialog($"\"{file.Name}\" downloaded successfully.", "Download Complete");
                dialog.Commands.Add(new UICommand("Open", async _ => await Launcher.LaunchFileAsync(file)));
                dialog.Commands.Add(new UICommand("Close"));
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ToastService.Show("Download Failed", ex.Message);
                await new MessageDialog(ex.Message, "Error").ShowAsync();
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
        // 2-COLUMN PAGINATED REVIEWS & RATINGS
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

                foreach (var item in arr)
                {
                    _allReviews.Add(new Review
                    {
                        _id = item["_id"]?.ToString(),
                        userId = item["userId"]?.ToString(),
                        username = item["username"]?.ToString(),
                        rating = int.Parse(item["rating"]?.ToString() ?? "0"),
                        comment = item["comment"]?.ToString(),
                        appId = item["appId"]?.ToString(),
                        createdAt = DateTime.TryParse(item["createdAt"]?.ToString(), out var dtCreate) ? dtCreate : DateTime.MinValue,
                        updatedAt = DateTime.TryParse(item["updatedAt"]?.ToString(), out var dtUpdate) ? dtUpdate : DateTime.MinValue
                    });
                }

                UpdateReviewStatistics(_allReviews);
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
            int count = reviews.Count();
            ReviewCountText.Text = $"{count} reviews";

            if (count == 0)
            {
                AverageRatingText.Text = "0.0";
                return;
            }

            double average = reviews.Average(r => r.rating);
            AverageRatingText.Text = average.ToString("0.0");
        }

        private async void SubmitReview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (await AuthService.LogoutIfExpired() || !AuthService.IsLoggedIn)
                {
                    UpdateReviewFormState();
                    await new MessageDialog("Your session has expired. Please sign in again.", "Sign In Required").ShowAsync();
                    return;
                }

                if (string.IsNullOrWhiteSpace(ReviewCommentBox.Text))
                    return;

                int rating = int.Parse(((ComboBoxItem)ReviewRatingBox.SelectedItem).Content.ToString());

                var bodyObj = new
                {
                    appId = _currentApp.Id,
                    rating = rating,
                    comment = ReviewCommentBox.Text
                };

                string json = JsonConvert.SerializeObject(bodyObj);
                await ReviewService.SubmitReview(AuthService.CurrentToken, json);

                ReviewCommentBox.Text = string.Empty;
                LoadReviews();

                await new MessageDialog("Thank you for your feedback!", "Review Submitted").ShowAsync();
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.Message, "Submission Error").ShowAsync();
            }
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