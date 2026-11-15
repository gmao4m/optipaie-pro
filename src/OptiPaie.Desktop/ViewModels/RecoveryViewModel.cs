using System;
using System.Windows.Input;
using OptiPaie.Core.Licensing;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Owner password recovery, reachable from the sign-in screen. Three states in one window:
    /// <list type="number">
    /// <item><b>Reset</b> — enter the one-time recovery code and a new password;</item>
    /// <item><b>Success</b> — show the FRESH recovery code (the used one is consumed), to save once;</item>
    /// <item><b>Fallback</b> — for the customer who has neither password nor code: show the install
    /// fingerprint and point to the vendor procedure (docs/multiuser/06-recovery.md).</item>
    /// </list>
    /// On success the owner is already signed in (handled inside <see cref="ICustomerAccountService.RecoverWithCode"/>).
    /// Fully offline.
    /// </summary>
    public sealed class RecoveryViewModel : ObservableObject
    {
        private readonly AppServices _services;

        private int _step; // 0 reset, 1 success, 2 fallback
        private string _code = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isError;
        private string _newRecoveryCode = string.Empty;
        private bool _savedConfirmed;
        private string _fingerprint = string.Empty;

        public RecoveryViewModel(AppServices services)
        {
            _services = services;
            RecoverCommand = new RelayCommand(Recover);
            ShowFallbackCommand = new RelayCommand(ShowFallback);
            BackToResetCommand = new RelayCommand(() => Step = 0);
            FinishCommand = new RelayCommand(Finish);
        }

        /// <summary>Raised to close the window; true once the password is recovered and the owner signed in.</summary>
        public Action<bool> CloseRequested { get; set; }

        /// <summary>Supplied by the window to read the two PasswordBoxes securely (never bound/stored).</summary>
        public Func<string> PasswordAccessor { get; set; }
        public Func<string> ConfirmPasswordAccessor { get; set; }

        public string ProductName => "OptiPaie PRO";

        public string Title => "استرجاع كلمة السرّ / Récupération du mot de passe";
        public string Intro =>
            "أدخل رمز الاسترجاع المكوّن من 20 محرفًا وكلمة سرّ جديدة.\n" +
            "Saisissez votre code de récupération (20 caractères) et un nouveau mot de passe.";
        public string CodeLabel => "رمز الاسترجاع / Code de récupération";
        public string PasswordLabel => "كلمة السرّ الجديدة (10 محارف على الأقلّ) / Nouveau mot de passe (10 car. min.)";
        public string ConfirmLabel => "تأكيد كلمة السرّ / Confirmer le mot de passe";
        public string RecoverButton => "استرجاع / Récupérer";
        public string NeitherLink => "ليس لديّ كلمة السرّ ولا الرمز / Je n'ai ni mot de passe ni code";

        public string SuccessTitle => "تمّ الاسترجاع — احفظ الرمز الجديد / Récupéré — enregistrez le nouveau code";
        public string SuccessIntro =>
            "تمّ تعيين كلمة سرّ جديدة وأنت الآن متّصل. احفظ هذا الرمز الجديد — القديم لم يعد صالحًا.\n" +
            "Un nouveau mot de passe est défini et vous êtes connecté. Enregistrez ce nouveau code — " +
            "l'ancien n'est plus valable.";
        public string SavedConfirmText => "لقد حفظت الرمز الجديد / J'ai enregistré le nouveau code";
        public string CopyButton => "نسخ / Copier";
        public string PrintButton => "طباعة / Imprimer";
        public string FinishButton => "دخول / Entrer";

        public string FallbackTitle => "فقدتَ الاثنين؟ / Les deux perdus ?";
        public string FallbackIntro =>
            "لا يوجد باب خلفي. أرسل «بصمة التثبيت» أدناه إلى البائع؛ يُصدر لك رمز إعادة تعيين موقَّعًا " +
            "صالحًا لهذا الجهاز وحده ولمدّة قصيرة.\n" +
            "Aucune porte dérobée. Envoyez « l'empreinte d'installation » ci-dessous à l'éditeur ; " +
            "il vous délivrera un code de réinitialisation signé, valable pour ce seul appareil et une " +
            "courte durée.";
        public string FingerprintLabel => "بصمة التثبيت / Empreinte d'installation";
        public string CopyFingerprintButton => "نسخ البصمة / Copier l'empreinte";

        public bool IsResetStep => _step == 0;
        public bool IsSuccessStep => _step == 1;
        public bool IsFallbackStep => _step == 2;

        private int Step
        {
            get => _step;
            set
            {
                if (Set(ref _step, value))
                {
                    Raise(nameof(IsResetStep));
                    Raise(nameof(IsSuccessStep));
                    Raise(nameof(IsFallbackStep));
                }
            }
        }

        public string Code { get => _code; set => Set(ref _code, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public bool IsError { get => _isError; private set => Set(ref _isError, value); }
        public string NewRecoveryCode { get => _newRecoveryCode; private set => Set(ref _newRecoveryCode, value); }
        public bool SavedConfirmed { get => _savedConfirmed; set => Set(ref _savedConfirmed, value); }
        public string Fingerprint { get => _fingerprint; private set => Set(ref _fingerprint, value); }

        public ICommand RecoverCommand { get; }
        public ICommand ShowFallbackCommand { get; }
        public ICommand BackToResetCommand { get; }
        public ICommand FinishCommand { get; }

        private void Recover()
        {
            string password = PasswordAccessor != null ? PasswordAccessor() : string.Empty;
            string confirm = ConfirmPasswordAccessor != null ? ConfirmPasswordAccessor() : string.Empty;

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                Fail("كلمتا السرّ غير متطابقتين.\nLes deux mots de passe ne correspondent pas.");
                return;
            }

            RecoveryResult result = _services.CustomerAccount.RecoverWithCode(_code, password);
            if (!result.Success)
            {
                Fail(MessageFor(result.Failure));
                return;
            }

            IsError = false;
            StatusMessage = string.Empty;
            NewRecoveryCode = result.NewRecoveryCode;
            Step = 1; // show the fresh code
        }

        private void ShowFallback()
        {
            try { Fingerprint = _services.DeviceIdentity != null ? _services.DeviceIdentity.GetDeviceId() : string.Empty; }
            catch { Fingerprint = string.Empty; }
            IsError = false;
            StatusMessage = string.Empty;
            Step = 2;
        }

        private void Finish()
        {
            if (!SavedConfirmed)
            {
                Fail("يرجى تأكيد أنّك حفظت الرمز الجديد.\nVeuillez confirmer que vous avez enregistré le code.");
                return;
            }

            CloseRequested?.Invoke(true); // recovered and signed in
        }

        private void Fail(string message)
        {
            IsError = true;
            StatusMessage = message;
        }

        private static string MessageFor(RecoveryFailure failure)
        {
            switch (failure)
            {
                case RecoveryFailure.NoAccount:
                    return "لا يوجد حساب أو رمز استرجاع على هذا الجهاز.\n" +
                           "Aucun compte ou code de récupération sur cet appareil.";
                case RecoveryFailure.Locked:
                    return "محاولات كثيرة متتالية. حاول بعد 15 دقيقة.\n" +
                           "Trop de tentatives. Réessayez dans 15 minutes.";
                case RecoveryFailure.WeakPassword:
                    return "كلمة السرّ قصيرة (10 محارف على الأقلّ).\n" +
                           "Mot de passe trop court (10 caractères minimum).";
                default:
                    return "رمز الاسترجاع غير صحيح.\nCode de récupération incorrect.";
            }
        }
    }
}
