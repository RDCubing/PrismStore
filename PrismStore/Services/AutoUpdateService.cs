using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Popups;
using Windows.Web.Http;
using Newtonsoft.Json;
using PrismStore.Models;
using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace PrismStore.Services
{
    public enum UpdateResult
    {
        Available,
        UpToDate,
        Failed
    }

    public static class AutoUpdateService
    {
        private const string UpdateJsonUrl = "https://raw.githubusercontent.com/RDCubing/geekhubapi/main/updateprism.json";

        public static async Task<UpdateResult> CheckAsync(bool showPromptIfAvailable = true)
        {
            try
            {
                string url = $"{UpdateJsonUrl}?t={DateTime.UtcNow.Ticks}";

                using (HttpClient client = new HttpClient())
                {
                    string json = await client.GetStringAsync(new Uri(url));
                    UpdateInfo update = JsonConvert.DeserializeObject<UpdateInfo>(json);

                    if (update == null || string.IsNullOrWhiteSpace(update.Version))
                        return UpdateResult.Failed;

                    Version currentVersion = GetAppVersion();
                    Version latestVersion = new Version(update.Version);

                    if (latestVersion > currentVersion)
                    {
                        if (showPromptIfAvailable)
                        {
                            await ShowUpdateDialogAsync(update);
                        }
                        return UpdateResult.Available;
                    }

                    return UpdateResult.UpToDate;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[UPDATE ERROR] " + ex.Message);
                return UpdateResult.Failed;
            }
        }

        private static Version GetAppVersion()
        {
            var v = Package.Current.Id.Version;
            return new Version(v.Major, v.Minor, v.Build, v.Revision);
        }

        public static async Task ShowUpdateDialogAsync(UpdateInfo update)
        {
            if (Window.Current?.Content == null)
                return;

            string message = $"{update.Message}\n\nVersion: {update.Version}";
            if (!string.IsNullOrWhiteSpace(update.Changelog))
            {
                message += $"\n\nWhat's New:\n{update.Changelog}";
            }

            var dialog = new ContentDialog
            {
                Title = update.Name ?? "Update Available",
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Update"
            };

            if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.ContentDialog", "CloseButtonText"))
                dialog.CloseButtonText = "Later";
            else
                dialog.SecondaryButtonText = "Later";

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                await DownloadAndInstallUpdateAsync(update);
            }
        }

        private static async Task DownloadAndInstallUpdateAsync(UpdateInfo update)
        {
            if (string.IsNullOrWhiteSpace(update.DownloadUrl))
                return;

            try
            {
                Uri uri = new Uri(update.DownloadUrl);

                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.Downloads;

                string fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName))
                    fileName = $"{update.Name}_{update.Version}.appx";

                picker.SuggestedFileName = fileName;

                string extension = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(extension))
                    extension = ".appx";

                picker.FileTypeChoices.Add("Package", new List<string> { extension });

                StorageFile file = await picker.PickSaveFileAsync();
                if (file == null)
                    return;

                ToastService.Show("Download started", update.Name ?? "PrismStore Update", "ms-appx:///Assets/PrismStore.png");

                using (HttpClient client = new HttpClient())
                {
                    var buffer = await client.GetBufferAsync(uri);
                    await FileIO.WriteBufferAsync(file, buffer);
                }

                ToastService.Show("Download complete", update.Name ?? "PrismStore Update", "ms-appx:///Assets/PrismStore.png");

                if (Window.Current?.Content != null)
                {
                    var installDialog = new ContentDialog
                    {
                        Title = "Update Ready",
                        Content = new TextBlock
                        {
                            Text = $"\"{file.Name}\" has downloaded successfully. Launch package installer now?",
                            TextWrapping = TextWrapping.Wrap
                        },
                        PrimaryButtonText = "Install"
                    };

                    if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.ContentDialog", "CloseButtonText"))
                        installDialog.CloseButtonText = "Later";
                    else
                        installDialog.SecondaryButtonText = "Later";

                    var result = await installDialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        await Launcher.LaunchFileAsync(file);
                    }
                }
            }
            catch (Exception ex)
            {
                ToastService.Show("Download Failed", ex.Message);

                if (Window.Current?.Content != null)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Update Installation Error",
                        Content = new TextBlock
                        {
                            Text = ex.Message,
                            TextWrapping = TextWrapping.Wrap
                        }
                    };

                    if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.ContentDialog", "CloseButtonText"))
                        errorDialog.CloseButtonText = "OK";
                    else
                        errorDialog.PrimaryButtonText = "OK";

                    await errorDialog.ShowAsync();
                }
            }
        }
    }
}