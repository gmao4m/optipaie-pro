using System;
using System.Collections.ObjectModel;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Common;
using OptiPaie.Admin.Mvvm;

namespace OptiPaie.Admin.ViewModels
{
    public sealed class StatCard
    {
        public StatCard(string label, int value) { Label = label; Value = value; }
        public string Label { get; }
        public int Value { get; }
    }

    public sealed class DashboardViewModel : SectionViewModel
    {
        public ObservableCollection<StatCard> Cards { get; } = new ObservableCollection<StatCard>();
        public ObservableCollection<AuditRow> Recent { get; } = new ObservableCollection<AuditRow>();
        public ObservableCollection<UpdateRow> LatestUpdates { get; } = new ObservableCollection<UpdateRow>();

        public override async void Load()
        {
            Busy = true;
            try
            {
                Overview o = await App.Api.SelectSingleAsync<Overview>("v_admin_overview", "select=*");
                Cards.Clear();
                if (o != null)
                {
                    Cards.Add(new StatCard("Total licences", o.TotalLicenses));
                    Cards.Add(new StatCard("Licences actives", o.ActiveLicenses));
                    Cards.Add(new StatCard("Expirées", o.ExpiredLicenses));
                    Cards.Add(new StatCard("Désactivées", o.DisabledLicenses));
                    Cards.Add(new StatCard("Appareils actifs", o.ActiveDevices));
                    Cards.Add(new StatCard("Nouvelles ce mois", o.NewThisMonth));
                }

                Recent.Clear();
                foreach (AuditRow a in await App.Api.SelectAsync<AuditRow>("audit_log", "select=*&order=created_at.desc&limit=8"))
                {
                    Recent.Add(a);
                }

                LatestUpdates.Clear();
                foreach (UpdateRow u in await App.Api.SelectAsync<UpdateRow>("updates", "select=*&order=published_at.desc&limit=5"))
                {
                    LatestUpdates.Add(u);
                }
            }
            catch (Exception ex)
            {
                Dialogs.Error(ex.Message);
            }
            finally
            {
                Busy = false;
            }
        }
    }
}
