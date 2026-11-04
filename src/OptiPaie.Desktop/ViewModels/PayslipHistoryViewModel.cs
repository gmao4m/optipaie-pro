using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One superseded/cancelled payslip row shown in the history window.</summary>
    public sealed class PayslipHistoryItem
    {
        public long PayslipId { get; set; }
        public string EmployeeName { get; set; }
        public string PeriodText { get; set; }
        public string NetText { get; set; }
        public string StatusText { get; set; }
        public string Reason { get; set; }
        public string RevisedBy { get; set; }
        /// <summary>Only a cancelled payslip can be restored (a superseded one was replaced by a correction).</summary>
        public bool CanRestore { get; set; }
    }

    /// <summary>
    /// The "cancelled / corrected payslips" history for a company and period. Read-only, except for
    /// restoring a cancelled payslip (refused by the service when an active one already occupies the
    /// slot). A cancelled payslip is never reprintable from here — only restorable.
    /// </summary>
    public sealed class PayslipHistoryViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly long _companyId;
        private readonly int _year;
        private readonly int? _month;

        public PayslipHistoryViewModel(AppServices services, long companyId, int year, int? month)
        {
            _services = services;
            _companyId = companyId;
            _year = year;
            _month = month;

            Rows = new ObservableCollection<PayslipHistoryItem>();
            RestoreCommand = new RelayCommand(p => Restore(p as PayslipHistoryItem));
            Reload();
        }

        public ObservableCollection<PayslipHistoryItem> Rows { get; }
        public ICommand RestoreCommand { get; }

        public string Title => L("Archive_HistoryTitle");
        public bool IsEmpty => Rows.Count == 0;

        private void Reload()
        {
            Rows.Clear();
            foreach (PayslipHistoryRow r in _services.PayslipRevision.GetHistory(_companyId, _year, _month)
                         .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenBy(r => r.EmployeeName))
            {
                Rows.Add(new PayslipHistoryItem
                {
                    PayslipId = r.PayslipId,
                    EmployeeName = r.EmployeeName,
                    PeriodText = MonthName(r.Month) + " " + r.Year,
                    NetText = r.NetSalaire.ToString("N2", Fr) + " DA",
                    StatusText = L(r.Status == PayslipStatus.Cancelled ? "History_Status_Cancelled" : "History_Status_Superseded"),
                    Reason = r.Reason,
                    RevisedBy = r.RevisedByUser,
                    CanRestore = r.Status == PayslipStatus.Cancelled
                });
            }

            Raise(nameof(IsEmpty));
        }

        private void Restore(PayslipHistoryItem item)
        {
            if (item == null || !item.CanRestore)
            {
                return;
            }

            if (!Dialogs.Confirm(L("Restore_Confirm")))
            {
                return;
            }

            Result result = _services.PayslipRevision.Restore(item.PayslipId);
            if (!result.IsSuccess)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Dialogs.Info(L("Restore_Done"));
            Reload();
        }

        private string L(string key) => _services.Localization.GetString(key);

        private string MonthName(int m)
        {
            if (m < 1 || m > 12) return m.ToString("00", CultureInfo.InvariantCulture);
            CultureInfo ci = _services.Localization.CurrentCulture;
            string name = ci.DateTimeFormat.GetMonthName(m);
            return string.IsNullOrEmpty(name) ? name : char.ToUpper(name[0], ci) + name.Substring(1);
        }
    }
}
