using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The duplicate-element rule protects against the SAME CATALOG ELEMENT being applied twice — and
    /// only that. Free-text lines (worksheet lines and the automatic loan instalment, stamped id 0 by the
    /// payroll service) carry no real element id and are excluded from the check. Field bug: an employee
    /// with a loan who also got one manual deduction was refused at payroll generation.
    /// </summary>
    [TestFixture]
    public sealed class DuplicateElementRuleTests
    {
        private static PayrollElementInput FreeText(string label, decimal amount, long? id = 0) => new PayrollElementInput
        {
            ElementId = id, LabelFr = label, LabelAr = label, ElementType = ElementType.Deduction,
            CalculationMethod = CalculationMethod.FixedAmount, Amount = amount,
            IsIncludedInGross = false, Periodicity = ElementPeriodicity.Monthly
        };

        private static PayrollElementInput Catalog(long id, string label, decimal amount) => new PayrollElementInput
        {
            ElementId = id, LabelFr = label, LabelAr = label, ElementType = ElementType.Gain,
            CalculationMethod = CalculationMethod.FixedAmount, Amount = amount,
            IsCnasApplicable = true, IsIrgApplicable = true, CnasFactor = 1m, IrgFactor = 1m,
            IsIncludedInGross = true, Periodicity = ElementPeriodicity.Monthly
        };

        private static PayrollResult Run(params PayrollElementInput[] elements) =>
            new PayrollCalculationEngine().Calculate(new PayrollContext
            {
                Period = new PayrollPeriod(2026, 7), BaseSalary = 60000m, WorkedDays = 31m, WorkableDays = 31m,
                Legal = new LegalSnapshot(0.09m, 0.26m, 24000m, RoundingPolicy.Centime),
                Elements = elements.ToList()
            });

        private static string Errors(PayrollResult r) => string.Join(" / ", r.Errors.Select(e => e.Code + ":" + e.Text));

        [Test]
        public void Loan_Instalment_Plus_Two_Manual_Deductions_Is_Accepted()
        {
            PayrollResult r = Run(
                FreeText("Remboursement prêt", 5000m),
                FreeText("Avance exceptionnelle", 2000m),
                FreeText("Retenue matériel perdu", 1500m));

            Assert.That(r.IsSuccess, Is.True, Errors(r));
            Assert.That(r.Lines.Count(l => l.ElementType == ElementType.Deduction), Is.EqualTo(3), "all three lines kept");
            Assert.That(r.Totals.NetSalaire,
                Is.EqualTo(r.Totals.SalaireBrut - r.Totals.CnasEmployee - r.Totals.Irg - 8500m), "all three deducted");
        }

        [Test]
        public void Lines_With_No_Element_Id_At_All_Are_Not_Duplicates()
        {
            PayrollResult r = Run(FreeText("Ligne A", 1000m, id: null), FreeText("Ligne B", 1000m, id: null));
            Assert.That(r.IsSuccess, Is.True, Errors(r));
        }

        [Test]
        public void The_Same_Catalog_Element_Twice_Is_Still_Refused_With_The_Same_Message()
        {
            PayrollResult r = Run(Catalog(7, "Prime de rendement", 8000m), Catalog(7, "Prime de rendement", 8000m));

            Assert.That(r.IsSuccess, Is.False);
            PayrollMessage e = r.Errors.Single();
            Assert.That(e.Code, Is.EqualTo(PayrollErrorCodes.DuplicateElement));
            Assert.That(e.Text, Is.EqualTo("Une rubrique de paie est dupliquée: Prime de rendement"), "message unchanged");
        }

        [Test]
        public void A_Catalog_Duplicate_Is_Refused_Even_Among_Free_Text_Lines()
        {
            PayrollResult r = Run(
                FreeText("Remboursement prêt", 5000m),
                Catalog(7, "Prime de rendement", 8000m),
                FreeText("Avance exceptionnelle", 2000m),
                Catalog(7, "Prime de rendement", 8000m));

            Assert.That(r.IsSuccess, Is.False);
            Assert.That(r.Errors.Single().Text, Is.EqualTo("Une rubrique de paie est dupliquée: Prime de rendement"));
        }

        [Test]
        public void Different_Catalog_Elements_And_A_Single_Free_Text_Line_Are_Accepted_As_Before()
        {
            PayrollResult r = Run(Catalog(7, "Prime de rendement", 8000m), Catalog(8, "Prime de panier", 3000m),
                FreeText("Avance sur salaire", 3000m));
            Assert.That(r.IsSuccess, Is.True, Errors(r));
            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(71000m));
        }
    }
}
