using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="Company"/>.</summary>
    internal sealed class CompanyRepository : RepositoryBase, ICompanyRepository
    {
        public CompanyRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public Company GetById(long id)
        {
            return Connection.QuerySingleOrDefault<Company>(
                "SELECT * FROM Companies WHERE Id = @id;",
                new { id }, Transaction);
        }

        public IEnumerable<Company> GetAll(bool includeDeleted = false)
        {
            return Connection.Query<Company>(
                "SELECT * FROM Companies WHERE (@all = 1 OR IsDeleted = 0) ORDER BY NameFr;",
                new { all = includeDeleted ? 1 : 0 }, Transaction);
        }

        public long Insert(Company company)
        {
            company.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO Companies " +
                "(NameFr, NameAr, LegalForm, AddressFr, AddressAr, Nif, Nis, Rc, ArticleImposition, " +
                " CnasEmployerNumber, Cacobatph, BtphSector, CacobatphEnabled, Bank, BankAccount, Currency, Phone, Email, Logo, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@NameFr, @NameAr, @LegalForm, @AddressFr, @AddressAr, @Nif, @Nis, @Rc, @ArticleImposition, " +
                " @CnasEmployerNumber, @Cacobatph, @BtphSector, @CacobatphEnabled, @Bank, @BankAccount, @Currency, @Phone, @Email, @Logo, " +
                " @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, company, Transaction);
            company.Id = id;
            return id;
        }

        public void Update(Company company)
        {
            company.UpdatedAtUtc = DateTime.UtcNow;

            const string sql =
                "UPDATE Companies SET " +
                "NameFr = @NameFr, NameAr = @NameAr, LegalForm = @LegalForm, AddressFr = @AddressFr, " +
                "AddressAr = @AddressAr, Nif = @Nif, Nis = @Nis, Rc = @Rc, ArticleImposition = @ArticleImposition, " +
                "CnasEmployerNumber = @CnasEmployerNumber, Cacobatph = @Cacobatph, " +
                "BtphSector = @BtphSector, CacobatphEnabled = @CacobatphEnabled, Bank = @Bank, " +
                "BankAccount = @BankAccount, Currency = @Currency, Phone = @Phone, Email = @Email, Logo = @Logo, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, company, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE Companies SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        public bool ExistsById(long id)
        {
            return Connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM Companies WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction) > 0;
        }
    }
}
