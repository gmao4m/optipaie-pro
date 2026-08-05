using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private string _scope = "mono";
        private string _duration = "lifetime";
        private string[] _keys = new string[0];
        // The scope/duration as they were WHEN the current batch was generated — exported
        // verbatim so a later dropdown change can't mislabel already-minted keys.
        private string _batchType = "Mono-société";
        private string _batchDuration = "À vie";

        public BulkViewModel()
        {
            GenerateCommand = new RelayCommand(async o => await GenerateAsync(Convert.ToInt32(o)));
            ExportCommand = new RelayCommand(Export, () => _keys.Length > 0);
            ExportPdfCommand = new RelayCommand(ExportPdf, () => _keys.Length > 0);
        }

        public Action RequestClose { get; set; }

        public string Note { get => _note; set => Set(ref _note, value); }
        public string Output { get => _output; private set => Set(ref _output, value); }
        public string Scope { get => _scope; set => Set(ref _scope, value); }
        public string Duration { get => _duration; set => Set(ref _duration, value); }

        /// <summary>The two license types (both unlock every feature).</summary>
        public List<ScopeOption> ScopeOptions { get; } = new List<ScopeOption>
        {
            new ScopeOption("mono", "Mono-société (1 entreprise)"),
            new ScopeOption("multi", "Multi-sociétés (illimité)")
        };

        public List<ScopeOption> Durations { get; } = new List<ScopeOption>
        {
            new ScopeOption("lifetime", "À vie (permanente)"),
            new ScopeOption("annual", "Annuelle — 1 an")
        };

        public ICommand GenerateCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ExportPdfCommand { get; }

        private string TypeLabel => _scope == "multi" ? "Multi-sociétés" : "Mono-société";
        private string DurationLabel => _duration == "annual" ? "Annuelle" : "À vie";

        private async System.Threading.Tasks.Task GenerateAsync(int count)
        {
            try
            {
                Row[] rows = await App.Api.RpcAsync<Row[]>("generate_licenses", new
                {
                    p_count = count,
                    p_product_key = "payroll",
                    p_notes = string.IsNullOrWhiteSpace(_note) ? null : _note.Trim()
                });
                _keys = rows != null ? Array.ConvertAll(rows, r => r.license_key) : new string[0];

                if (_keys.Length > 0)
                {
                    // Snapshot the labels for this batch (exports use these, not the live UI).
                    _batchType = TypeLabel;
                    _batchDuration = DurationLabel;

                    // generate_licenses seeds the pool with defaults (Mono / À vie); apply the
                    // chosen scope + duration to the whole batch. Chunked so the ?in.(...) URL
                    // never grows too long even for 1000 keys.
                    int maxCompanies = _scope == "multi" ? 0 : 1;
                    string expiresIso = _duration == "annual"
                        ? DateTime.Today.AddYears(1).ToUniversalTime().ToString("o")
                        : null;

                    foreach (string[] chunk in Chunk(_keys, 50))
                    {
                        string inList = string.Join(",", chunk); // keys are [A-Z0-9-] → URL-safe unquoted
                        await App.Api.UpdateAsync("licenses", "license_key=in.(" + inList + ")", new
                        {
                            type = _duration,
                            max_companies = maxCompanies,
                            expires_at = expiresIso
                        });
                    }
                }

                Output = string.Join("\r\n", _keys);
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private void Export()
        {
            var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "optipaie-licenses.csv" };
            if (dialog.ShowDialog() != true) return;
            var sb = new StringBuilder("license_key,type,duree\r\n");
            foreach (string k in _keys) sb.AppendLine(k + "," + _batchType + "," + _batchDuration);
            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            Dialogs.Info("Exporté : " + dialog.FileName);
        }

        private void ExportPdf()
        {
            var dialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "optipaie-licenses.pdf" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var rows = new List<LicenseBatchRow>(_keys.Length);
                for (int i = 0; i < _keys.Length; i++)
                {
                    rows.Add(new LicenseBatchRow { Index = i + 1, Key = _keys[i], Type = _batchType, Duration = _batchDuration });
                }

                LicensePdf.Save(dialog.FileName, rows, _note, DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                Dialogs.Info("PDF généré : " + dialog.FileName);
            }
            catch (Exception ex) { Dialogs.Error("Génération du PDF impossible : " + ex.Message); }
        }

        private static IEnumerable<string[]> Chunk(string[] items, int size)
        {
            for (int i = 0; i < items.Length; i += size)
            {
                yield return items.Skip(i).Take(size).ToArray();
            }
        }

        private sealed class Row { public string license_key { get; set; } }
    }
}
