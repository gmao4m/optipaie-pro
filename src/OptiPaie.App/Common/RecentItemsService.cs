using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OptiPaie.Core.Interfaces.Services;

namespace OptiPaie.App.Common
{
    /// <summary>
    /// Tracks recently-used companies, payrolls and archive documents, persisted as
    /// JSON in the application settings. Each list is capped; the most recent entry
    /// is first.
    /// </summary>
    public sealed class RecentItemsService
    {
        public const string KindCompany = "company";
        public const string KindPayroll = "payroll";
        public const string KindArchive = "archive";

        private const int MaxPerKind = 10;
        private const string SettingPrefix = "RECENT_";

        private readonly ISettingsService _settings;

        public RecentItemsService(ISettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void Record(string kind, long id, string label)
        {
            if (string.IsNullOrWhiteSpace(kind) || id <= 0)
            {
                return;
            }

            List<RecentItem> items = Load(kind);
            items.RemoveAll(i => i.Id == id);
            items.Insert(0, new RecentItem { Kind = kind, Id = id, Label = label ?? string.Empty, WhenUtc = DateTime.UtcNow });

            if (items.Count > MaxPerKind)
            {
                items = items.Take(MaxPerKind).ToList();
            }

            Save(kind, items);
        }

        public IReadOnlyList<RecentItem> Get(string kind)
        {
            return Load(kind);
        }

        /// <summary>All recent items across kinds, newest first.</summary>
        public IReadOnlyList<RecentItem> GetAll(int take = 15)
        {
            return Load(KindCompany)
                .Concat(Load(KindPayroll))
                .Concat(Load(KindArchive))
                .OrderByDescending(i => i.WhenUtc)
                .Take(take)
                .ToList();
        }

        private List<RecentItem> Load(string kind)
        {
            string raw = _settings.Get(SettingPrefix + kind.ToUpperInvariant(), null);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<RecentItem>();
            }

            try
            {
                return JsonConvert.DeserializeObject<List<RecentItem>>(raw) ?? new List<RecentItem>();
            }
            catch (JsonException)
            {
                return new List<RecentItem>();
            }
        }

        private void Save(string kind, List<RecentItem> items)
        {
            _settings.Set(SettingPrefix + kind.ToUpperInvariant(), JsonConvert.SerializeObject(items));
        }
    }
}
