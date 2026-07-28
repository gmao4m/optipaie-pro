using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Shared orchestration for the asset hand-over / return / edit dialogs. Both the asset
    /// list and the asset detail view call these so an asset behaves identically wherever it
    /// is opened. Each method opens the matching dialog, applies the service result and
    /// returns <c>true</c> when the asset changed (so the caller can refresh).
    /// </summary>
    internal static class AssetActions
    {
        /// <summary>Opens the create dialog (with the optional immediate hand-over section).</summary>
        public static bool Create(AppServices services, long companyId)
        {
            return OpenEditor(services, companyId, null);
        }

        /// <summary>Opens the edit dialog for an existing asset.</summary>
        public static bool Edit(AppServices services, long companyId, long assetId)
        {
            return OpenEditor(services, companyId, services.Assets.Get(assetId));
        }

        private static bool OpenEditor(AppServices services, long companyId, Asset existing)
        {
            var vm = new AssetEditViewModel(services, companyId, existing);
            var window = new AssetEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }

        public static bool Assign(AppServices services, long companyId, long assetId)
        {
            IReadOnlyList<Employee> employees = services.Employees.GetByCompany(companyId, false);
            if (employees.Count == 0)
            {
                Dialogs.Info(services.Localization.GetString("AssetEdit_NoEmployees"));
                return false;
            }

            var vm = new AssetAssignViewModel(employees);
            var window = new AssetAssignWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() != true || vm.SelectedEmployee == null) return false;

            Result result = services.Assets.Assign(assetId, vm.SelectedEmployee.Id, vm.Date, vm.Condition, vm.Notes);
            if (result.IsFailure) { Dialogs.Error(result.Error); return false; }
            return true;
        }

        public static bool Return(AppServices services, long assetId)
        {
            // Current holders (an exclusive asset has one; a shared asset can have several).
            var holders = services.Assets.GetHistory(assetId).Where(a => a.ReturnedDate == null).ToList();
            if (holders.Count == 0)
            {
                Dialogs.Info("Ce matériel n'est attribué à personne.");
                return false;
            }

            var vm = new AssetReturnViewModel(holders);
            var window = new AssetReturnWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() != true) return false;

            // A shared asset with several holders returns only the chosen one; otherwise the
            // single holder is implied.
            Result result = holders.Count > 1 && vm.SelectedHolder != null
                ? services.Assets.ReturnFrom(assetId, vm.SelectedHolder.EmployeeId, vm.Date, vm.Condition)
                : services.Assets.Return(assetId, vm.Date, vm.Condition);

            if (result.IsFailure) { Dialogs.Error(result.Error); return false; }
            return true;
        }
    }
}
