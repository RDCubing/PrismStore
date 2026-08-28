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
            string message = $"{update.Message}\n\nVersion: {update.Version}";
            if (!string.IsNullOrWhiteSpace(update.Changelog))
            {
                message += $"\n\nWhat's New:\n{update.Changelog}";
            }

            MessageDialog dialog = new MessageDialog(message, update.Name ?? "Update Available");
            dialog.Commands.Add(new UICommand("Update"));
            dialog.Commands.Add(new UICommand("Later"));
            dialog.DefaultCommandIndex = 0;
            dialog.CancelCommandIndex = 1;

            var result = await dialog.ShowAsync();

            if (result != null && result.Label == "Update")
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

                // ✅ Notification: download started
                ToastService.Show("Download started", update.Name ?? "Prism Store Update", "ms-appx:///Assets/PrismStore.png");

                using (HttpClient client = new HttpClient())
                {
                    var buffer = await client.GetBufferAsync(uri);
                    await FileIO.WriteBufferAsync(file, buffer);
                }

                // ✅ Notification: download completed
                ToastService.Show("Download complete", update.Name ?? "Prism Store Update", "ms-appx:///Assets/PrismStore.png");

                MessageDialog installDialog = new MessageDialog(
                    $"\"{file.Name}\" has downloaded successfully. Launch package installer now?",
                    "Update Ready");

                installDialog.Commands.Add(new UICommand("Install", async _ =>
                {
                    await Launcher.LaunchFileAsync(file);
                }));
                installDialog.Commands.Add(new UICommand("Later"));

                await installDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ToastService.Show("Download Failed", ex.Message);
                await new MessageDialog(ex.Message, "Update Installation Error").ShowAsync();
            }
        }
    }
}