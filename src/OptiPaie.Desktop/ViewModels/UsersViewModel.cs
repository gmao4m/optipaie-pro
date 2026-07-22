using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>A role option with a French label (for the role combo).</summary>
    public sealed class RoleOption
    {
        public RoleOption(UserRole value, string label) { Value = value; Label = label; }
        public UserRole Value { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    /// <summary>One user row in the management list.</summary>
    public sealed class UserRowViewModel
    {
        public UserRowViewModel(User user, string roleLabel, string activeLabel)
        {
            User = user;
            RoleLabel = roleLabel;
            ActiveLabel = activeLabel;
        }

        public User User { get; }
        public long Id => User.Id;
        public string Username => User.Username;
        public string FullName => User.FullName;
        public string RoleLabel { get; }
        public string Department => User.Department;
        public string ActiveLabel { get; }
    }

    /// <summary>
    /// Admin screen: create/manage local users and switch the login gate on/off. Passwords
    /// go straight to the hashing service; nothing here touches payroll.
    /// </summary>
    public sealed class UsersViewModel : ObservableObject
    {
        private readonly AppServices _services;

        private UserRowViewModel _selected;
        private string _newUsername = string.Empty;
        private string _newFullName = string.Empty;
        private string _newPassword = string.Empty;
        private string _newDepartment = string.Empty;
        private RoleOption _newRole;
        private string _statusMessage = string.Empty;

        public UsersViewModel(AppServices services)
        {
            _services = services;

            Roles.Add(new RoleOption(UserRole.Admin, L("Role_Admin")));
            Roles.Add(new RoleOption(UserRole.Manager, L("Role_Manager")));
            _newRole = Roles[0];

            AddCommand = new RelayCommand(Add);
            DeleteCommand = new RelayCommand(Delete, () => _selected != null);
            ToggleActiveCommand = new RelayCommand(ToggleActive, () => _selected != null);
            ResetPasswordCommand = new RelayCommand(ResetPassword, () => _selected != null);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Refresh();
        }

        public Action RequestClose { get; set; }

        public ObservableCollection<UserRowViewModel> Users { get; } = new ObservableCollection<UserRowViewModel>();
        public ObservableCollection<RoleOption> Roles { get; } = new ObservableCollection<RoleOption>();

        public UserRowViewModel Selected { get => _selected; set => Set(ref _selected, value); }

        public string NewUsername { get => _newUsername; set => Set(ref _newUsername, value); }
        public string NewFullName { get => _newFullName; set => Set(ref _newFullName, value); }
        public string NewPassword { get => _newPassword; set => Set(ref _newPassword, value); }
        public string NewDepartment { get => _newDepartment; set => Set(ref _newDepartment, value); }
        public RoleOption NewRole { get => _newRole; set => Set(ref _newRole, value); }

        public bool LoginEnabled
        {
            get => _services.Users.IsLoginEnabled();
            set
            {
                if (value && _services.Users.ActiveUserCount() == 0)
                {
                    Dialogs.Info("Créez au moins un administrateur avant d'activer la connexion.");
                    Raise(nameof(LoginEnabled));
                    return;
                }
                _services.Users.SetLoginEnabled(value);
                Raise(nameof(LoginEnabled));
                StatusMessage = value ? "Connexion activée — effective au prochain démarrage." : "Connexion désactivée.";
            }
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand AddCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ToggleActiveCommand { get; }
        public ICommand ResetPasswordCommand { get; }
        public ICommand CloseCommand { get; }

        public void Refresh()
        {
            Users.Clear();
            foreach (User u in _services.Users.GetAll())
            {
                string role = u.Role == UserRole.Admin ? L("Role_Admin") : L("Role_Manager");
                string active = L(u.IsActive ? "State_Active" : "State_Inactive");
                Users.Add(new UserRowViewModel(u, role, active));
            }
            StatusMessage = Users.Count + " " + L("Users_CountSuffix");
        }

        private string L(string key) => _services.Localization.GetString(key);

        private string Err(Result r) => Localization.ResultText.Localize(_services.Localization, r.Error, r.ErrorCode);

        private void Add()
        {
            Result<long> r = _services.Users.Create(_newUsername, _newFullName, _newPassword, _newRole.Value, _newDepartment);
            if (r.IsFailure) { Dialogs.Error(Err(r)); return; }

            NewUsername = NewFullName = NewPassword = NewDepartment = string.Empty;
            Refresh();
            StatusMessage = "Utilisateur créé.";
        }

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer cet utilisateur ?")) return;
            Result r = _services.Users.Delete(_selected.Id);
            if (r.IsFailure) { Dialogs.Error(Err(r)); return; }
            Refresh();
        }

        private void ToggleActive()
        {
            User u = _services.Users.Get(_selected.Id);
            if (u == null) return;
            u.IsActive = !u.IsActive;
            Result r = _services.Users.Update(u);
            if (r.IsFailure) { Dialogs.Error(Err(r)); return; }
            Refresh();
        }

        private void ResetPassword()
        {
            // Reuses the "initial password" field as the new password for the selected user.
            if (string.IsNullOrWhiteSpace(_newPassword))
            {
                Dialogs.Info(L("Users_ResetPasswordHint"));
                return;
            }

            Result r = _services.Users.ChangePassword(_selected.Id, _newPassword);
            if (r.IsFailure) { Dialogs.Error(Err(r)); return; }

            NewPassword = string.Empty;
            StatusMessage = L("Users_PasswordReset");
        }
    }
}
