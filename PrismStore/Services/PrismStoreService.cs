using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.Web.Http;
using Newtonsoft.Json;
using PrismStore.Models;

namespace PrismStore.Services
{
    public static class PrismStoreService
    {
        // Store Catalogs
        public static ObservableCollection<AppItem> PrismStore { get; } = new ObservableCollection<AppItem>();
        public static ObservableCollection<AppItem> NeonStore { get; } = new ObservableCollection<AppItem>();

        // Featured & Filter Collections
        public static ObservableCollection<AppItem> TopApps { get; } = new ObservableCollection<AppItem>();
        public static ObservableCollection<AppItem> OtherApps { get; } = new ObservableCollection<AppItem>();
        public static ObservableCollection<string> Categories { get; } = new ObservableCollection<string>();

        public static async Task LoadAsync()
        {
            try
            {
                Debug.WriteLine("PrismStoreService: Loading...");

                using (var client = new HttpClient())
                {
                    // Cache-busting timestamp query
                    string url = "https://raw.githubusercontent.com/RDCubing/geekhubapi/main/projects.json?t=" + DateTime.UtcNow.Ticks;

                    string json = await client.GetStringAsync(new Uri(url));
                    RootObject data = JsonConvert.DeserializeObject<RootObject>(json);

                    if (data == null)
                    {
                        Debug.WriteLine("PrismStoreService: Data is NULL");
                        return;
                    }

                    // Safely dispatch to UI Thread only if a UI window exists
                    var coreWindow = CoreApplication.MainView?.CoreWindow;
                    if (coreWindow != null && coreWindow.Dispatcher != null)
                    {
                        await coreWindow.Dispatcher.RunAsync(
                            CoreDispatcherPriority.Normal,
                            () => PopulateCollections(data));
                    }
                    else
                    {
                        // Running headless during StartupTask activation (no UI window created)
                        PopulateCollections(data);
                    }

                    Debug.WriteLine("PrismStoreService: Load Completed Successfully ✔");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PrismStoreService ERROR: " + ex.Message);
            }
        }

        private static void PopulateCollections(RootObject data)
        {
            PrismStore.Clear();
            NeonStore.Clear();
            TopApps.Clear();
            OtherApps.Clear();
            Categories.Clear();

            // Load PrismStore Apps (Windows 10)
            if (data.PrismStore != null)
            {
                foreach (var app in data.PrismStore)
                {
                    PrismStore.Add(app);
                    ProcessAppMetadata(app);
                }
            }

            // Load NeonStore Apps (Windows 8.1)
            if (data.NeonStore != null)
            {
                foreach (var app in data.NeonStore)
                {
                    NeonStore.Add(app);
                    ProcessAppMetadata(app);
                }
            }
        }

        private static void ProcessAppMetadata(AppItem app)
        {
            // Filter Top Apps vs Standard Apps
            if (string.Equals(app.TopApp, "Yes", StringComparison.OrdinalIgnoreCase))
            {
                if (!TopApps.Contains(app))
                    TopApps.Add(app);
            }
            else
            {
                if (!OtherApps.Contains(app))
                    OtherApps.Add(app);
            }

            // Populate unique categories
            if (!string.IsNullOrWhiteSpace(app.Category) && !Categories.Contains(app.Category))
            {
                Categories.Add(app.Category);
            }
        }
    }
}