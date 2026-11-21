using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using PrismStore.Services;
using Windows.UI;
using Windows.ApplicationModel.Core;
using System.Reflection;

namespace PrismStore
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        /// private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new Windows.UI.ViewManagement.UISettings();
        private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new Windows.UI.ViewManagement.UISettings();
        private Windows.UI.Core.CoreDispatcher _mainDispatcher;
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    // Restore saved session state if needed
                }

                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }

                // Activate the window for normal interactive user launches
                Window.Current.Activate();
                ExtendAcrylicIntoTitleBar();

                // Load catalog, update live tiles, and run startup checks
                await PrismStoreService.LoadAsync();
                var allApps = PrismStoreService.TopApps
                    .Concat(PrismStoreService.OtherApps)
                    .ToList();

                TileService.UpdateRandomAppsQueue();
                await AuthService.LogoutIfExpired();
                await DownloadHistoryService.Instance.LoadAsync();
                await AutoUpdateService.CheckAsync(showPromptIfAvailable: true);
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        protected override async void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);

            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            if (args.Kind == ActivationKind.ToastNotification)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage));
                }

                Window.Current.Activate();
            }
            else if (args.Kind == ActivationKind.Protocol && args is ProtocolActivatedEventArgs protocolArgs)
            {
                await HandleProtocolAuthCallbackAsync(protocolArgs.Uri);

                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage));
                }
                else if (rootFrame.Content is MainPage mainPage)
                {
                    mainPage.RefreshAuthFromExternalLogin();
                }

                Window.Current.Activate();
            }
        }

        private async System.Threading.Tasks.Task HandleProtocolAuthCallbackAsync(Uri callbackUri)
        {
            if (callbackUri == null || string.IsNullOrWhiteSpace(callbackUri.Query))
                return;

            var decoder = new WwwFormUrlDecoder(callbackUri.Query);
            string token = null;
            string username = null;
            string avatar = null;
            string code = null;

            foreach (var entry in decoder)
            {
                if (entry.Name.Equals("token", StringComparison.OrdinalIgnoreCase))
                    token = entry.Value;
                else if (entry.Name.Equals("username", StringComparison.OrdinalIgnoreCase))
                    username = entry.Value;
                else if (entry.Name.Equals("avatar", StringComparison.OrdinalIgnoreCase))
                    avatar = entry.Value;
                else if (entry.Name.Equals("code", StringComparison.OrdinalIgnoreCase))
                    code = entry.Value;
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                AuthService.SaveSession(token, username, avatar);
                await AuthService.SyncProfileAsync();
            }
            else if (!string.IsNullOrWhiteSpace(code))
            {
                await AuthService.ExchangeDiscordTokenAsync(code);
                await AuthService.SyncProfileAsync();
            }
        }

        private void ExtendAcrylicIntoTitleBar()
        {
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            _mainDispatcher = Window.Current?.Dispatcher;
            _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;

            UpdateTitleBarButtonColors();
        }

        private async void UiSettings_ColorValuesChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            if (_mainDispatcher == null)
                return;

            await _mainDispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                UpdateTitleBarButtonColors();
            });
        }

        private void UpdateTitleBarButtonColors()
        {
            var appTitleBar = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().TitleBar;
            appTitleBar.ButtonBackgroundColor = Colors.Transparent;
            appTitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Detect live system theme (works even when App.RequestedTheme follows Windows Default)
            var bgColor = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            bool isLight = bgColor.R > 128 && bgColor.G > 128 && bgColor.B > 128;

            appTitleBar.ButtonForegroundColor = isLight ? Colors.Black : Colors.White;
            appTitleBar.ButtonInactiveForegroundColor = isLight
                ? Windows.UI.Color.FromArgb(140, 0, 0, 0)
                : Windows.UI.Color.FromArgb(140, 255, 255, 255);

            appTitleBar.ButtonHoverForegroundColor = isLight ? Colors.Black : Colors.White;
            appTitleBar.ButtonHoverBackgroundColor = isLight
                ? Windows.UI.Color.FromArgb(20, 0, 0, 0)
                : Windows.UI.Color.FromArgb(25, 255, 255, 255);

            appTitleBar.ButtonPressedForegroundColor = isLight ? Colors.Black : Colors.White;
            appTitleBar.ButtonPressedBackgroundColor = isLight
                ? Windows.UI.Color.FromArgb(40, 0, 0, 0)
                : Windows.UI.Color.FromArgb(45, 255, 255, 255);
        }

        private async System.Threading.Tasks.Task RunHeadlessStartupCheckAsync()
        {
            try
            {
                await PrismStoreService.LoadAsync();
                await StartupUpdateChecker.CheckAndNotifyAsync(force: true);
                await System.Threading.Tasks.Task.Delay(500);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Headless startup error: " + ex.Message);
            }
            finally
            {
                if (Window.Current == null || Window.Current.Content == null)
                {
                    try { Window.Current?.Close(); } catch { }
                    Application.Current.Exit();
                }
            }
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity
            deferral.Complete();
        }

        protected override async void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            base.OnBackgroundActivated(args);

            var deferral = args.TaskInstance.GetDeferral();
            try
            {
                await PrismStoreService.LoadAsync();
                await StartupUpdateChecker.CheckAndNotifyAsync(force: true);
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
