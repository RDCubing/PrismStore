using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using PrismStore.Models;

namespace PrismStore.Services
{
    public static class TileService
    {
        private static readonly Random _rng = new Random();

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        /// <summary>
        /// Picks up to 3 unique random apps from the catalog and queues them on the Start Menu Live Tile.
        /// </summary>
        public static void UpdateRandomAppsQueue(int count = 5)
        {
            var combined = new List<AppItem>();

            if (PrismStoreService.PrismStore != null)
                combined.AddRange(PrismStoreService.PrismStore);

            if (PrismStoreService.NeonStore != null)
                combined.AddRange(PrismStoreService.NeonStore);

            if (PrismStoreService.TopApps != null)
                combined.AddRange(PrismStoreService.TopApps);

            if (PrismStoreService.OtherApps != null)
                combined.AddRange(PrismStoreService.OtherApps);

            var uniqueAllApps = combined
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Title))
                .GroupBy(a => !string.IsNullOrWhiteSpace(a.Id) ? a.Id : a.Title, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            UpdateRandomAppsQueue(uniqueAllApps, count);
        }

        /// <summary>
        /// Picks up to 5 unique random apps from the catalog and queues them on the Start Menu Live Tile.
        /// </summary>
        public static void UpdateRandomAppsQueue(IList<AppItem> catalogApps, int count = 5)
        {
            if (catalogApps == null || catalogApps.Count == 0)
                return;

            var validApps = catalogApps
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Title))
                .ToList();

            if (validApps.Count == 0)
                return;

            int queueCount = Math.Min(Math.Min(count, 5), validApps.Count);

            var randomPicks = validApps
                .OrderBy(_ => _rng.Next())
                .Take(queueCount)
                .ToList();

            var updater = TileUpdateManager.CreateTileUpdaterForApplication();
            updater.EnableNotificationQueue(true);
            updater.Clear();

            for (int i = 0; i < randomPicks.Count; i++)
            {
                string badge = !string.IsNullOrWhiteSpace(randomPicks[i].Category)
                    ? randomPicks[i].Category
                    : "Discover";

                SendRandomAppTile(updater, randomPicks[i], badgeLabel: badge, tag: $"RandomApp_{i}");
            }
        }

        private static void SendRandomAppTile(
            TileUpdater updater,
            AppItem app,
            string badgeLabel,
            string tag)
        {
            string title = EscapeXml(app.Title);
            string subtitle = EscapeXml(!string.IsNullOrWhiteSpace(app.Subtitle) ? app.Subtitle : app.Publisher);
            string publisher = EscapeXml(app.Publisher);
            string version = EscapeXml(app.Version ?? string.Empty);
            string desc = EscapeXml(!string.IsNullOrWhiteSpace(app.Description) ? app.Description : subtitle);
            string badge = EscapeXml(badgeLabel);
            string icon = EscapeXml(app.ImagePath);

            string mediumIconXml = !string.IsNullOrEmpty(icon)
                ? $"<image src=\"{icon}\" placement=\"peek\" />"
                : string.Empty;

            string wideIconXml = !string.IsNullOrEmpty(icon)
                ? $"<subgroup hint-weight=\"30\"><image src=\"{icon}\" /></subgroup>"
                : string.Empty;

            string widePublisherXml = !string.IsNullOrEmpty(publisher)
                ? $"<text hint-style=\"captionSubtle\" hint-maxLines=\"1\">{publisher}</text>"
                : string.Empty;

            string largeIconXml = !string.IsNullOrEmpty(icon)
                ? $"<subgroup hint-weight=\"24\"><image src=\"{icon}\" /></subgroup>"
                : string.Empty;

            string largeMetaLine = !string.IsNullOrEmpty(version)
                ? $"{publisher} • {version}"
                : publisher;

            string largeMetaXml = !string.IsNullOrEmpty(largeMetaLine)
                ? $"<text hint-style=\"captionSubtle\" hint-maxLines=\"1\">{largeMetaLine}</text>"
                : string.Empty;

            string xml = $@"
<tile>
  <visual branding=""nameAndLogo"">
    <binding template=""TileMedium"" displayName=""{badge}"">
      {mediumIconXml}
      <text hint-style=""base"" hint-maxLines=""1"">{title}</text>
      <text hint-style=""captionSubtle"" hint-wrap=""true"" hint-maxLines=""2"">{subtitle}</text>
    </binding>

    <binding template=""TileWide"" displayName=""{badge}"">
      <group>
        {wideIconXml}
        <subgroup hint-weight=""70"" hint-textStacking=""center"">
          <text hint-style=""base"" hint-maxLines=""1"">{title}</text>
          <text hint-style=""captionSubtle"" hint-wrap=""true"" hint-maxLines=""2"">{subtitle}</text>
          {widePublisherXml}
        </subgroup>
      </group>
    </binding>

    <binding template=""TileLarge"" displayName=""{badge}"">
      <group>
        {largeIconXml}
        <subgroup hint-weight=""76"" hint-textStacking=""center"">
          <text hint-style=""subtitle"" hint-maxLines=""1"">{title}</text>
          {largeMetaXml}
        </subgroup>
      </group>
      <text />
      <text hint-style=""captionSubtle"" hint-wrap=""true"" hint-maxLines=""6"">{desc}</text>
    </binding>
  </visual>
</tile>";

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            updater.Update(new TileNotification(doc) { Tag = tag });
        }
    }
}