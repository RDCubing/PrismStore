using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace PrismStore.Services
{
    public static class CertificateCheckHelper
    {
        private static readonly string[] PackageExtensions =
        {
            ".appx",
            ".appxbundle",
            ".msix",
            ".msixbundle"
        };

        public static bool IsWindowsAppPackage(string fileNameOrUrl)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrUrl))
                return false;

            string path = fileNameOrUrl;
            if (Uri.TryCreate(fileNameOrUrl, UriKind.Absolute, out Uri uri))
            {
                path = uri.LocalPath;
            }

            string ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext))
                return false;

            return PackageExtensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsWindowsAppPackage(StorageFile file)
        {
            return file != null && IsWindowsAppPackage(file.Name);
        }

        /// <summary>
        /// Checks whether a PackageManager deployment HRESULT or error message indicates
        /// a missing or untrusted signing certificate.
        /// </summary>
        public static bool IsCertificateDeploymentError(int hresult, string errorText = null)
        {
            // 0x800B0109 = CERT_E_UNTRUSTEDROOT
            // 0x800B010A = CERT_E_CHAINING
            // 0x800B010C = CERT_E_REVOKED
            // 0x800B0101 = CERT_E_EXPIRED
            if (hresult == unchecked((int)0x800B0109) ||
                hresult == unchecked((int)0x800B010A) ||
                hresult == unchecked((int)0x800B010C) ||
                hresult == unchecked((int)0x800B0101))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(errorText))
            {
                return errorText.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       errorText.IndexOf("0x800B0109", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       errorText.IndexOf("0x800B010A", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       errorText.IndexOf("untrusted", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        /// <summary>
        /// Reads AppxManifest.xml or AppxMetadata/AppxBundleManifest.xml inside the package ZIP
        /// to extract the Identity/@Publisher string (e.g., "CN=...").
        /// </summary>
        public static async Task<string> ExtractPackagePublisherAsync(StorageFile packageFile)
        {
            if (packageFile == null)
                return null;

            try
            {
                using (var stream = await packageFile.OpenStreamForReadAsync())
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var manifestEntry = archive.Entries.FirstOrDefault(e =>
                        e.FullName.Equals("AppxManifest.xml", StringComparison.OrdinalIgnoreCase) ||
                        e.FullName.Equals("AppxMetadata/AppxBundleManifest.xml", StringComparison.OrdinalIgnoreCase) ||
                        e.FullName.Equals("AppxMetadata\\AppxBundleManifest.xml", StringComparison.OrdinalIgnoreCase));

                    if (manifestEntry == null)
                        return null;

                    using (var entryStream = manifestEntry.Open())
                    {
                        var doc = XDocument.Load(entryStream);
                        var identity = doc.Descendants()
                            .FirstOrDefault(el => el.Name.LocalName.Equals("Identity", StringComparison.OrdinalIgnoreCase));

                        return identity?.Attribute("Publisher")?.Value;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Displays a Windows 10 ContentDialog explaining how to install the certificate,
        /// with a primary "Open Folder" button that opens the folder containing the downloaded package.
        /// </summary>
        public static async Task ShowInstallCertificateDialogAsync(
            StorageFile packageFile = null,
            string publisherSubject = null)
        {
            string fileName = packageFile != null ? $"\"{packageFile.Name}\"" : "the .appx / .msix file";
            string subjectLine = !string.IsNullOrWhiteSpace(publisherSubject)
                ? $"Package Certificate Subject: {publisherSubject}\n\n"
                : string.Empty;

            string instructions =
                $"This app package was downloaded, but its signing certificate is not installed on your PC.\n\n" +
                subjectLine +
                $"To install the certificate:\n" +
                $"1. Click \"Open Folder\" below, then right-click {fileName} and select Properties.\n" +
                $"2. Open the Digital Signatures tab, select the signature in the list, and click Details.\n" +
                $"3. Click View Certificate → Install Certificate...\n" +
                $"4. Select Local Machine → Next, choose \"Place all certificates in the following store\", click Browse, and pick Trusted People (or Trusted Root Certification Authorities).\n" +
                $"5. Click Next → Finish, then retry installing in PrismStore.";

            var dialog = new ContentDialog
            {
                Title = "Whoa there!",
                Content = new TextBlock
                {
                    Text = instructions,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Open Folder"
            };

            if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.ContentDialog", "CloseButtonText"))
                dialog.CloseButtonText = "Close";
            else
                dialog.SecondaryButtonText = "Close";

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                StorageFolder targetFolder = null;
                if (packageFile != null)
                {
                    targetFolder = await packageFile.GetParentAsync();
                }
                if (targetFolder == null)
                {
                    targetFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                        "Packages",
                        CreationCollisionOption.OpenIfExists);
                }

                await Launcher.LaunchFolderAsync(targetFolder);
            }
        }
    }
}