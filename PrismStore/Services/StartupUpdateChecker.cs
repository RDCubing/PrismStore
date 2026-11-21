using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using PrismStore.Models;

namespace PrismStore.Services
{
    public static class StartupUpdateChecker
    {
        private static readonly SemaphoreSlim _checkLock = new SemaphoreSlim(1, 1);
        private static bool _hasCheckedThisSession = false;

        private const string LastToastSignatureKey = "StartupChecker_LastSignature";
        private const string LastToastTimestampKey = "StartupChecker_LastUtcTicks";
        private static readonly TimeSpan NotificationCooldown = TimeSpan.FromHours(12);

        public static async Task CheckAndNotifyAsync(bool force = false)
        {
            if (_hasCheckedThisSession && !force)
                return;

            if (!await _checkLock.WaitAsync(0))
                return;

            try
            {
                _hasCheckedThisSession = true;

                // 1. Check if PrismStore client itself has a newer release (no modal popup)
                var clientResult = await AutoUpdateService.CheckAsync(showPromptIfAvailable: false);
                Debug.WriteLine("[STARTUP CHECK] Client update status: " + clientResult);

                if (clientResult == UpdateResult.Available)
                {
                    ToastService.Show(
                        "PrismStore update available",
                        "A new version of PrismStore is ready to install in Settings.",
                        "ms-appx:///Assets/Square150x150Logo.png");
                }

                // 2. Scan installed packages against the loaded catalog
                var allCatalogApps = PrismStoreService.PrismStore
                    .Concat(PrismStoreService.NeonStore)
                    .GroupBy(a => !string.IsNullOrWhiteSpace(a.packageId) ? a.packageId : a.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                Debug.WriteLine($"[STARTUP CHECK] Catalog apps loaded: {allCatalogApps.Count}");
                if (allCatalogApps.Count == 0)
                    return;

                // Refresh package cache so externally installed/updated apps are accurate
                PackageLookupService.InvalidateCache();
                await PackageLookupService.WarmCacheAsync();

                var updatableApps = new List<AppItem>();
                foreach (var app in allCatalogApps)
                {
                    var status = await PackageLookupService.GetAppStatusAsync(app);
                    if (status.State == AppInstallState.UpdateAvailable)
                    {
                        updatableApps.Add(app);
                    }
                }

                Debug.WriteLine($"[STARTUP CHECK] Updatable apps found: {updatableApps.Count}");
                if (updatableApps.Count == 0)
                {
                    ClearLastNotificationState();
                    return;
                }

                // 3. Avoid spamming identical toasts every 15-minute TimeTrigger wake
                string signature = string.Join("|", updatableApps
                    .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(a => $"{a.Title}:{a.Version}"));

                if (!force && IsDuplicateWithinCooldown(signature))
                {
                    Debug.WriteLine("[STARTUP CHECK] Skipping duplicate toast within cooldown.");
                    return;
                }

                // 4. Fire a Windows toast notification
                if (updatableApps.Count == 1)
                {
                    var single = updatableApps[0];
                    ToastService.Show(
                        $"Update available: {single.Title}",
                        $"Version {single.Version} is ready to install in PrismStore.",
                        !string.IsNullOrWhiteSpace(single.ImagePath) ? single.ImagePath : "ms-appx:///Assets/Square150x150Logo.png");
                }
                else
                {
                    string names = string.Join(", ", updatableApps.Take(3).Select(a => a.Title));
                    if (updatableApps.Count > 3)
                        names += $" +{updatableApps.Count - 3} more";

                    ToastService.Show(
                        $"{updatableApps.Count} app updates available",
                        names,
                        "ms-appx:///Assets/PrismStore.png");
                }

                SaveLastNotificationState(signature);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[STARTUP UPDATE CHECK ERROR] " + ex.Message);
            }
            finally
            {
                _checkLock.Release();
            }
        }

        private static bool IsDuplicateWithinCooldown(string currentSignature)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (!values.TryGetValue(LastToastSignatureKey, out object sigObj) ||
                !values.TryGetValue(LastToastTimestampKey, out object ticksObj))
            {
                return false;
            }

            if (sigObj is string lastSig &&
                ticksObj is long lastTicks &&
                string.Equals(lastSig, currentSignature, StringComparison.Ordinal))
            {
                var elapsed = DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc);
                return elapsed < NotificationCooldown;
            }

            return false;
        }

        private static void SaveLastNotificationState(string signature)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values[LastToastSignatureKey] = signature;
            values[LastToastTimestampKey] = DateTime.UtcNow.Ticks;
        }

        private static void ClearLastNotificationState()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values.Remove(LastToastSignatureKey);
            values.Remove(LastToastTimestampKey);
        }
    }
}