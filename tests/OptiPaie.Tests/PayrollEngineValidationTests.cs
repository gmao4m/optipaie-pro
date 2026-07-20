using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Verifies the validation engine stops bad inputs before calculation and that
    /// known limitations are reported (never silently mis-calculated).
    /// </summary>
    [TestFixture]
    public sealed class PayrollEngineValidationTests
    {
        private static PayrollResult Calculate(PayrollContext context)
        {
            return new PayrollCalculationEngine().Calculate(context);
        }

        private static bool HasError(PayrollResult result, string code)
        {
            return result.Messages.Any(m => m.Code == code && m.Severity == PayrollMessageSeverity.Error);
        }

        [Test]
        public void NullContext_Fails()
        {
            PayrollResult r = Calculate(null);
            Assert.That(r.IsSuccess, Is.False);
            Assert.That(HasError(r, PayrollErrorCodes.ContextMissing), Is.True);
        }

        [Test]
        public void NegativeBaseSalary_Fails()
        {
            PayrollResult r = Calculate(PayrollTestFactory.Context(-100m));
            Assert.That(r.IsSuccess, Is.False);
            Assert.That(HasError(r, PayrollErrorCodes.BaseSalaryNegative), Is.True);
        }

        [Test]
        public void DuplicateElement_Fails()
        {
            PayrollElementInput a = PayrollTestFactory.Gain("Prime", 1000m);
            PayrollElementInput b = PayrollTestFactory.Gain("Prime", 2000m);
            a.ElementId = 5;
            b.ElementId = 5;

            PayrollResult r = Calculate(PayrollTestFactory.Context(50000m, a, b));
            Assert.That(HasError(r, PayrollErrorCodes.DuplicateElement), Is.True);
        }

        [Test]
        public void UnsupportedMethod_IsReported()
        {
            // An out-of-range method value (e.g. a future Formula method) is rejected,
            // never silently mis-calculated.
            PayrollElementInput element = PayrollTestFactory.Gain("Inconnue", 0m);
            element.CalculationMethod = (CalculationMethod)99;

            PayrollResult r = Calculate(PayrollTestFactory.Context(50000m, element));
            Assert.That(HasError(r, PayrollErrorCodes.UnsupportedCalculationMethod), Is.True);
        }

        [Test]
        public void UnsupportedCalculationBase_Fails()
        {
            PayrollElementInput element = PayrollTestFactory.Percentage("Prime", 0.10m);
            element.CalculationBase = CalculationBase.SalaireBrut;

            PayrollResult r = Calculate(PayrollTestFactory.Context(50000m, element));
            Assert.That(HasError(r, PayrollErrorCodes.UnsupportedCalculationBase), Is.True);
        }

        [Test]
        public void MissingCalculationBase_Fails()
        {
            PayrollElementInput element = PayrollTestFactory.Percentage("Prime", 0.10m);
            element.CalculationBase = null;

            PayrollResult r = Calculate(PayrollTestFactory.Context(50000m, element));
            Assert.That(HasError(r, PayrollErrorCodes.MissingCalculationBase), Is.True);
        }

        [Test]
        public void NegativeAmount_Fails()
        {
            PayrollElementInput element = PayrollTestFactory.Gain("Prime", -100m);
            PayrollResult r = Calculate(PayrollTestFactory.Context(50000m, element));
            Assert.That(HasError(r, PayrollErrorCodes.NegativeAmount), Is.True);
        }

        [Test]
        public void InvalidPeriod_Fails()
        {
            PayrollContext context = PayrollTestFactory.Context(50000m);
            context.Period = default(OptiPaie.Core.Primitives.PayrollPeriod);

            PayrollResult r = Calculate(context);
            Assert.That(HasError(r, PayrollErrorCodes.PeriodInvalid), Is.True);
        }

        [Test]
        public void MissingLegalSnapshot_Fails()
        {
            PayrollContext context = PayrollTestFactory.Context(50000m);
            context.Legal = null;

            PayrollResult r = Calculate(context);
            Assert.That(HasError(r, PayrollErrorCodes.LegalMissing), Is.True);
        }

        [Test]
        public void NetNegative_IsReported()
        {
            PayrollResult r = Calculate(PayrollTestFactory.Context(10000m,
                PayrollTestFactory.NetDeduction("Avance", 50000m)));

            Assert.That(r.IsSuccess, Is.False);
            Assert.That(HasError(r, PayrollErrorCodes.NetNegative), Is.True);
        }
    }
}
