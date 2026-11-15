using System;
using System.Windows.Input;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// First-launch-after-upgrade OWNER setup. Shown only when the installation has real data but no
    /// account yet: accounts are now required, so the customer creates the owner before anything else
    /// is reachable. The business data is never touched. Two steps in one window:
    /// <list type="number">
    /// <item>create the owner (e-mail, company, password);</item>
    /// <item>save the one-time recovery code (shown ONCE, large, copyable and printable) and tick a box
    /// to confirm it was saved before finishing.</item>
    /// </list>
    /// On finish the owner is already signed in and setup is marked complete, so the path never runs
    /// again.
    /// </summary>
    public sealed class OwnerSetupViewModel : ObservableObject
    {
        private readonly AppServices _services;

        private bool _isSaveStep;
        private string _email = string.Empty;
        private string _company = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isError;
        private string _recoveryCode = string.Empty;
        private bool _savedConfirmed;

        public OwnerSetupViewModel(AppServices services)
        {
            _services = services;
            CreateCommand = new RelayCommand(Create);
            FinishCommand = new RelayCommand(Finish);

            // Pre-fill the e-mail/company from an existing (but not-yet-owner) account if any.
            if (_services.CustomerAccount != null && _services.CustomerAccount.HasAccount)
            {
                _email = _services.CustomerAccount.Email;
                _company = _services.CustomerAccount.Company;
            }
        }

        /// <summary>Raised to close the window; true once the owner is created and the code saved.</summary>
        public Action<bool> CloseRequested { get; set; }

        /// <summary>Supplied by the window to read the two PasswordBoxes securely (never bound/stored).</summary>
        public Func<string> PasswordAccessor { get; set; }
        public Func<string> ConfirmPasswordAccessor { get; set; }

        public string ProductName => "OptiPaie PRO";

        // Bilingual by mandate (AR + FR), kept in the VM so no resource key can go missing at render.
        public string Title => "إنشاء حساب المالك / Création du compte propriétaire";

        public string Intro =>
            "أصبحت الحسابات مطلوبة في هذا الإصدار لحماية بياناتك. أنشئ حساب المالك للمتابعة؛ " +
            "لن تُمسّ بياناتك الحالية.\n" +
            "Les comptes sont désormais requis pour protéger vos données. Créez le compte " +
            "propriétaire pour continuer ; vos données existantes ne seront pas touchées.";

        public string EmailLabel => "البريد الإلكتروني / E-mail";
        public string CompanyLabel => "المؤسسة / Entreprise";
        public string PasswordLabel => "كلمة السرّ (10 محارف على الأقلّ) / Mot de passe (10 caractères min.)";
        public string ConfirmLabel => "تأكيد كلمة السرّ / Confirmer le mot de passe";
        public string CreateButton => "إنشاء الحساب / Créer le compte";

        public string SaveTitle => "احفظ رمز الاسترجاع / Enregistrez votre code de récupération";
        public string SaveIntro =>
            "هذا الرمز يُعرض مرّة واحدة فقط. اكتبه أو اطبعه واحفظه في مكان آمن — به تستعيد الدخول إذا نسيت كلمة السرّ.\n" +
            "Ce code n'est affiché qu'une seule fois. Copiez-le ou imprimez-le et conservez-le en lieu sûr — " +
            "il vous permet de récupérer l'accès si vous oubliez votre mot de passe.";
        public string SavedConfirmText => "لقد حفظت رمز الاسترجاع في مكان آمن / J'ai enregistré ce code en lieu sûr";
        public string CopyButton => "نسخ / Copier";
        public string PrintButton => "طباعة / Imprimer";
        public string FinishButton => "إنهاء / Terminer";

        public bool IsCreateStep => !_isSaveStep;
        public bool IsSaveStep { get => _isSaveStep; private set { if (Set(ref _isSaveStep, value)) Raise(nameof(IsCreateStep)); } }

        public string Email { get => _email; set => Set(ref _email, value); }
        public string Company { get => _company; set => Set(ref _company, value); }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public bool IsError { get => _isError; private set => Set(ref _isError, value); }

        public string RecoveryCode { get => _recoveryCode; private set => Set(ref _recoveryCode, value); }
        public bool SavedConfirmed { get => _savedConfirmed; set => Set(ref _savedConfirmed, value); }

        public ICommand CreateCommand { get; }
        public ICommand FinishCommand { get; }

        private void Create()
        {
            string password = PasswordAccessor != null ? PasswordAccessor() : string.Empty;
            string confirm = ConfirmPasswordAccessor != null ? ConfirmPasswordAccessor() : string.Empty;

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                IsError = true;
                StatusMessage = "كلمتا السرّ غير متطابقتين.\nLes deux mots de passe ne correspondent pas.";
                return;
            }

            Result<string> result = _services.OwnerSetup.CreateOwner(_email, _company, password);
            if (!result.IsSuccess)
            {
                IsError = true;
                StatusMessage = result.Error;
                return;
            }

            IsError = false;
            StatusMessage = string.Empty;
            RecoveryCode = result.Value;
            IsSaveStep = true; // move to "save your recovery code"
        }

        private void Finish()
        {
            if (!SavedConfirmed)
            {
                IsError = true;
                StatusMessage = "يرجى تأكيد أنّك حفظت رمز الاسترجاع.\nVeuillez confirmer que vous avez enregistré le code.";
                return;
            }

            CloseRequested?.Invoke(true);
        }
    }
}
