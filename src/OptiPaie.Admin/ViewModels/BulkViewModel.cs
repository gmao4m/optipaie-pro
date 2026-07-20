using System;
using System.IO;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Admin.Common;
using OptiPaie.Admin.Mvvm;

namespace OptiPaie.Admin.ViewModels
{
    public sealed class BulkViewModel : ObservableObject
    {
        private string _note = string.Empty;
        private string _output = string.Empty;
        private string[] _keys = new string[0];

        public BulkViewModel()
        {
            GenerateCommand = new RelayCommand(async o => await GenerateAsync(Convert.ToInt32(o)));
            ExportCommand = new RelayCommand(Export, () => _keys.Length > 0);
        }

        public Action RequestClose { get; set; }

        public string Note { get => _note; set => Set(ref _note, value); }
        public string Output { get => _output; private set => Set(ref _output, value); }

        public ICommand GenerateCommand { get; }
        public ICommand ExportCommand { get; }

        private async System.Threading.Tasks.Task GenerateAsync(int count)
        {
            try
            {
                var rows = await App.Api.RpcAsync<Row[]>("generate_licenses", new
                {
                    p_count = count,
                    p_product_key = "payroll",
                    p_notes = string.IsNullOrWhiteSpace(_note) ? null : _note.Trim()
                });
                _keys = rows != null ? Array.ConvertAll(rows, r => r.license_key) : new string[0];
                Output = string.Join("\r\n", _keys);
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private void Export()
        {
            var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "optipaie-licenses.csv" };
            if (dialog.ShowDialog() != true) return;
            var sb = new StringBuilder("license_key\r\n");
            foreach (string k in _keys) sb.AppendLine(k);
            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            Dialogs.Info("Exporté : " + dialog.FileName);
        }

        private sealed class Row { public string license_key { get; set; } }
    }
}
