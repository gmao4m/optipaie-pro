using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Common;
using OptiPaie.Admin.Mvvm;

namespace OptiPaie.Admin.ViewModels
{
    public sealed class Kv
    {
        public Kv(string key, int value) { Key = key; Value = value; }
        public string Key { get; }
        public int Value { get; }
    }

    public sealed class ReportsViewModel : SectionViewModel
    {
        public ObservableCollection<Kv> TypeStats { get; } = new ObservableCollection<Kv>();
        public ObservableCollection<Kv> StatusStats { get; } = new ObservableCollection<Kv>();

        public override async void Load()
        {
            Busy = true;
            try
            {
                // Every licence unlocks all sections — the only breakdowns that matter now
                // are by duration (type) and by status.
                var licenses = await App.Api.SelectAsync<License>("licenses", "select=type,status");
                Fill(TypeStats, Aggregate(licenses, l => l.Type));
                Fill(StatusStats, Aggregate(licenses, l => l.Status));
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
            finally { Busy = false; }
        }

        private static Dictionary<string, int> Aggregate<T>(T[] rows, Func<T, string> key)
        {
            var map = new Dictionary<string, int>();
            foreach (T r in rows)
            {
                string k = key(r) ?? "—";
                map[k] = map.TryGetValue(k, out int c) ? c + 1 : 1;
            }
            return map;
        }

        private static void Fill(ObservableCollection<Kv> target, Dictionary<string, int> map)
        {
            target.Clear();
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string k in keys) target.Add(new Kv(k, map[k]));
        }
    }
}
