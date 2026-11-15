using System;
using System.Windows.Input;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Display-only presentation of a one-time recovery code (large, copyable, printable) with a
    /// confirm-saved checkbox before it can be dismissed. Used when a code is BACKFILLED for an
    /// existing owner account at sign-in — no reset step, just "here is your code, save it".
    /// </summary>
    public sealed class RecoveryCodeViewModel : ObservableObject
    {
        private bool _savedConfirmed;
        private bool _isError;

        public RecoveryCodeViewModel(string code)
        {
            RecoveryCode = code ?? string.Empty;
            FinishCommand = new RelayCommand(Finish);
        }

        public Action CloseRequested { get; set; }

        public string ProductName => "OptiPaie PRO";
        public string Title => "رمز الاسترجاع الخاص بك / Votre code de récupération";
        public string Intro =>
            "أنشأنا لك رمز استرجاع. يُعرض مرّة واحدة — احفظه أو اطبعه. به تستعيد الدخول إذا نسيت كلمة السرّ.\n" +
            "Un code de récupération a été généré pour votre compte. Affiché une seule fois — enregistrez-le " +
            "ou imprimez-le. Il vous permet de récupérer l'accès si vous oubliez votre mot de passe.";
        public string SavedConfirmText => "لقد حفظت الرمز / J'ai enregistré ce code";
        public string CopyButton => "نسخ / Copier";
        public string PrintButton => "طباعة / Imprimer";
        public string FinishButton => "متابعة / Continuer";
        public string StatusMessage => "يرجى تأكيد أنّك حفظت الرمز.\nVeuillez confirmer que vous avez enregistré le code.";

        public string RecoveryCode { get; }
        public bool SavedConfirmed { get => _savedConfirmed; set => Set(ref _savedConfirmed, value); }
        public bool IsError { get => _isError; private set => Set(ref _isError, value); }

        public ICommand FinishCommand { get; }

        private void Finish()
        {
            if (!SavedConfirmed)
            {
                IsError = true;
                return;
            }

            CloseRequested?.Invoke();
        }
    }
}
