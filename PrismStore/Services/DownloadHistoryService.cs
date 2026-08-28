using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        // Add item, update badge count, and save to storage
        public async void Add(DownloadItem item)
        {
            Downloads.Insert(0, item);
            await SaveAsync();
        }

        // Clear all items, reset badge, and save state
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

        // Load download collection from local storage
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
                    foreach (var item in items)
                        Downloads.Add(item);
                }
            }
            catch
            {
                // File does not exist on first run
            }
        }
    }
}