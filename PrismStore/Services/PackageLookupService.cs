using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Management.Deployment;
using PrismStore.Models;

namespace PrismStore.Services
{
    public enum AppInstallState
    {
        NotInstalled,
        UpdateAvailable,
        UpToDate,
        InstalledNewer
    }

    public class InstalledAppStatus
    {
        public AppInstallState State { get; set; }
        public Package InstalledPackage { get; set; }
        public Version InstalledVersion { get; set; }
        public Version CatalogVersion { get; set; }

        public string InstalledVersionText =>
            InstalledVersion != null
                ? $"{InstalledVersion.Major}.{InstalledVersion.Minor}.{InstalledVersion.Build}.{InstalledVersion.Revision}"
                : string.Empty;
    }

    internal class PackageCache
    {
        public Dictionary<string, Package> ByPackageId { get; } =
            new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<Package>> ByTitle { get; } =
            new Dictionary<string, List<Package>>(StringComparer.OrdinalIgnoreCase);
    }

    public static class PackageLookupService
    {
        private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        private static PackageCache _cache;
        private static DateTime _lastCacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Clears the cached package index. Call this right after installing, updating, or uninstalling an app.
        /// </summary>
        public static void InvalidateCache()
        {
            _cache = null;
            _lastCacheTime = DateTime.MinValue;
        }

        /// <summary>
        /// Pre-warms the installed package index in the background so UI lookups are instantaneous.
        /// </summary>
        public static Task WarmCacheAsync()
        {
            return EnsureCacheAsync(forceRefresh: false);
        }

        /// <summary>
        /// Primary overload accepting an AppItem directly (uses app.packageId first, then title fallback).
        /// </summary>
        public static Task<InstalledAppStatus> GetAppStatusAsync(AppItem app)
        {
            if (app == null)
            {
                return Task.FromResult(new InstalledAppStatus { State = AppInstallState.NotInstalled });
            }

            return GetAppStatusAsync(app.packageId, app.Title, app.Version, app.Publisher);
        }

        /// <summary>
        /// Checks installed package state using packageId first (exact Identity Name or FamilyName),
        /// falling back to normalized title matching only if packageId is not supplied.
        /// </summary>
        public static async Task<InstalledAppStatus> GetAppStatusAsync(
            string packageId,
            string appTitle,
            string catalogVersionRaw,
            string publisher = null)
        {
            Package pkg = null;

            if (!string.IsNullOrWhiteSpace(packageId))
            {
                pkg = await FindInstalledByPackageIdAsync(packageId);
            }
            else
            {
                pkg = await FindInstalledByTitleAsync(appTitle, publisher);
            }

            if (pkg == null)
            {
                return new InstalledAppStatus
                {
                    State = AppInstallState.NotInstalled
                };
            }

            var pv = pkg.Id.Version;
            Version installedVer = new Version(pv.Major, pv.Minor, pv.Build, pv.Revision);
            Version catalogVer;

            if (!TryParseNormalizedVersion(catalogVersionRaw, out catalogVer))
            {
                return new InstalledAppStatus
                {
                    State = AppInstallState.UpToDate,
                    InstalledPackage = pkg,
                    InstalledVersion = installedVer
                };
            }

            int comparison = catalogVer.CompareTo(installedVer);
            AppInstallState state =
                comparison > 0 ? AppInstallState.UpdateAvailable :
                comparison == 0 ? AppInstallState.UpToDate :
                AppInstallState.InstalledNewer;

            return new InstalledAppStatus
            {
                State = state,
                InstalledPackage = pkg,
                InstalledVersion = installedVer,
                CatalogVersion = catalogVer
            };
        }

        public static async Task<Package> FindInstalledByPackageIdAsync(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            var cache = await EnsureCacheAsync(forceRefresh: false);

            Package pkg;
            if (cache.ByPackageId.TryGetValue(packageId.Trim(), out pkg))
            {
                return pkg;
            }

            return null;
        }

        public static async Task<Package> FindInstalledByTitleAsync(string appTitle, string publisher = null)
        {
            string targetTitle = NormalizeTitle(appTitle);
            if (string.IsNullOrEmpty(targetTitle))
                return null;

            var cache = await EnsureCacheAsync(forceRefresh: false);

            List<Package> matches;
            if (!cache.ByTitle.TryGetValue(targetTitle, out matches) || matches == null || matches.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(publisher))
            {
                string trimmedPub = publisher.Trim();
                var pubMatch = matches.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.PublisherDisplayName) &&
                    p.PublisherDisplayName.IndexOf(trimmedPub, StringComparison.OrdinalIgnoreCase) >= 0);

                if (pubMatch != null)
                    return pubMatch;
            }

            return matches[0];
        }

        private static async Task<PackageCache> EnsureCacheAsync(bool forceRefresh)
        {
            if (!forceRefresh && _cache != null && (DateTime.UtcNow - _lastCacheTime) < CacheTtl)
            {
                return _cache;
            }

            await _cacheLock.WaitAsync();
            try
            {
                if (!forceRefresh && _cache != null && (DateTime.UtcNow - _lastCacheTime) < CacheTtl)
                {
                    return _cache;
                }

                // Instantiate PackageManager on the calling thread (NOT inside Task.Run)
                // to prevent COM 0x800700EA activation errors on worker threads.
                PackageManager packageManager = null;
                try
                {
                    packageManager = new PackageManager();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[PackageLookupService] PackageManager unavailable: " + ex.Message);
                    _cache = new PackageCache();
                    _lastCacheTime = DateTime.UtcNow;
                    return _cache;
                }

                _cache = await Task.Run(() => BuildPackageCache(packageManager));
                _lastCacheTime = DateTime.UtcNow;
                return _cache;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private static PackageCache BuildPackageCache(PackageManager packageManager)
        {
            var cache = new PackageCache();
            if (packageManager == null)
                return cache;

            IEnumerable<Package> packages;
            try
            {
                packages = packageManager.FindPackagesForUser(string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PackageLookupService] FindPackagesForUser failed: " + ex.Message);
                return cache;
            }

            bool hasPackageDisplayName = ApiInformation.IsPropertyPresent(
                "Windows.ApplicationModel.Package",
                "DisplayName");
            bool hasIsResourcePackage = ApiInformation.IsPropertyPresent(
                "Windows.ApplicationModel.Package",
                "IsResourcePackage");
            bool hasIsBundle = ApiInformation.IsPropertyPresent(
                "Windows.ApplicationModel.Package",
                "IsBundle");

            foreach (var pkg in packages)
            {
                try
                {
                    if (pkg.IsFramework)
                        continue;
                    if (hasIsResourcePackage && pkg.IsResourcePackage)
                        continue;
                    if (hasIsBundle && pkg.IsBundle)
                        continue;

                    string pkgName = pkg.Id?.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(pkgName) ||
                        pkgName.StartsWith("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase) ||
                        pkgName.StartsWith("Microsoft.NET.", StringComparison.OrdinalIgnoreCase) ||
                        pkgName.StartsWith("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 1. Index by exact Package.Id.Name (Identity Name) and Package.Id.FamilyName
                    cache.ByPackageId[pkgName] = pkg;

                    string familyName = pkg.Id?.FamilyName;
                    if (!string.IsNullOrEmpty(familyName))
                    {
                        cache.ByPackageId[familyName] = pkg;
                    }

                    // 2. Also index by DisplayName for catalog items without a packageId
                    if (hasPackageDisplayName)
                    {
                        string displayName = NormalizeTitle(pkg.DisplayName);
                        if (!string.IsNullOrEmpty(displayName))
                        {
                            AddToTitleIndex(cache.ByTitle, displayName, pkg);
                        }
                    }
                }
                catch
                {
                    // Skip individual restricted/inaccessible system packages
                }
            }

            return cache;
        }

        private static void AddToTitleIndex(Dictionary<string, List<Package>> index, string key, Package pkg)
        {
            List<Package> list;
            if (!index.TryGetValue(key, out list))
            {
                list = new List<Package>();
                index[key] = list;
            }

            if (!list.Contains(pkg))
            {
                list.Add(pkg);
            }
        }

        public static bool TryParseNormalizedVersion(string rawVersion, out Version normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(rawVersion))
                return false;

            string cleaned = rawVersion.Trim().TrimStart('v', 'V');
            int dashIndex = cleaned.IndexOf('-');
            if (dashIndex > 0)
                cleaned = cleaned.Substring(0, dashIndex);

            string[] parts = cleaned.Split('.');
            if (parts.Length == 0 || parts.Length > 4)
                return false;

            int[] nums = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                int val;
                if (!int.TryParse(parts[i], out val) || val < 0)
                    return false;
                nums[i] = val;
            }

            normalized = new Version(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }

        private static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            return Regex.Replace(title, @"\s*\([^)]*\)", string.Empty).Trim();
        }

        public static async Task<bool> LaunchPackageAsync(Package pkg)
        {
            if (pkg == null)
                return false;

            var entries = await pkg.GetAppListEntriesAsync();
            var firstEntry = entries?.FirstOrDefault();
            return firstEntry != null && await firstEntry.LaunchAsync();
        }

        public static async Task<bool> UninstallPackageAsync(Package pkg)
        {
            if (pkg == null)
                return false;

            var packageManager = new PackageManager();
            var op = packageManager.RemovePackageAsync(pkg.Id.FullName);
            var tcs = new TaskCompletionSource<bool>();

            op.Completed = (info, status) =>
            {
                tcs.TrySetResult(status == AsyncStatus.Completed);
            };

            bool removed = await tcs.Task;
            if (removed)
            {
                InvalidateCache();
            }
            return removed;
        }
    }
}