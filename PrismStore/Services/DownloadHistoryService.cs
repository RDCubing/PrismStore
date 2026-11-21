using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Newtonsoft.Json;
using PrismStore.Models;

namespace PrismStore.Services
{
    public class DownloadHistoryService
    {
        public static DownloadHistoryService Instance { get; } = new DownloadHistoryService();

        public ObservableCollection<DownloadItem> Downloads { get; }
            = new ObservableCollection<DownloadItem>();

        private const string FileName = "downloads.json";

        // Add item without duplicates, update badge count, and persist
        public async void Add(DownloadItem item)
        {
            if (item == null) return;

            var existing = Downloads.FirstOrDefault(d =>
                (!string.IsNullOrEmpty(item.DownloadUrl) && string.Equals(d.DownloadUrl, item.DownloadUrl, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(item.FileName) && string.Equals(d.FileName, item.FileName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(item.Title) && string.Equals(d.Title, item.Title, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                Downloads.Remove(existing);
            }

            Downloads.Insert(0, item);
            await SaveAsync();
        }

        // Remove a single item, update badge count, and persist
        public async void Remove(DownloadItem item)
        {
            if (item == null) return;

            if (Downloads.Remove(item))
            {
                await SaveAsync();
            }
        }

        // Clear all items, reset badge count, and persist
        public async void Clear()
        {
            Downloads.Clear();
            await SaveAsync();
        }

        // Save download collection to local storage
        public async Task SaveAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder
                    .CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);

                var json = JsonConvert.SerializeObject(Downloads);
                await FileIO.WriteTextAsync(file, json);
            }
            catch
            {
                // Silent fail
            }
        }

        // Load download collection from local storage with deduplication
        public async Task LoadAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName);
                var json = await FileIO.ReadTextAsync(file);

                var items = JsonConvert.DeserializeObject<List<DownloadItem>>(json);

                Downloads.Clear();

                if (items != null)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var item in items)
                    {
                        string key = !string.IsNullOrEmpty(item.DownloadUrl)
                            ? item.DownloadUrl
                            : item.FileName ?? item.Title;

                        if (!string.IsNullOrEmpty(key) && seen.Add(key))
                        {
                            Downloads.Add(item);
                        }
                    }
                }
            }
            catch
            {
                // First run: file does not exist yet
            }
        }
    }
}