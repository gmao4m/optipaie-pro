using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence for local user accounts (optional login).</summary>
    public interface IUserRepository
    {
        User GetById(long id);

        /// <summary>The active, non-deleted user with this username, or null.</summary>
        User GetByUsername(string username);

        /// <summary>
        /// The non-deleted user with this username REGARDLESS of active state (active preferred),
        /// or null. Used at sign-in so a disabled account can be told apart from a wrong password.
        /// </summary>
        User GetByUsernameIncludingInactive(string username);

        IEnumerable<User> GetAll();

        long Insert(User user);

        void Update(User user);

        void SoftDelete(long id);

        /// <summary>Number of active, non-deleted users.</summary>
        int CountActive();

        /// <summary>Number of active, non-deleted administrators.</summary>
        int CountAdmins();
    }
}
