using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Immutable computed payslip line produced by the engine. Maps directly onto a
    /// persisted <see cref="OptiPaie.Core.Entities.PayrollDetail"/>.
    /// </summary>
    public sealed class PayrollLineResult
    {
        /// <summary>Originating element id, for traceability. Null for statutory lines.</summary>
        public long? ElementId { get; }

        /// <summary>Frozen line label in French.</summary>
        public string LabelFr { get; }

        /// <summary>Frozen line label in Arabic.</summary>
        public string LabelAr { get; }

        /// <summary>Whether the line is a gain or a deduction.</summary>
        public ElementType ElementType { get; }

        /// <summary>Base used, when applicable.</summary>
        public decimal? Base { get; }

        /// <summary>Rate/percentage used, when applicable.</summary>
        public decimal? Rate { get; }

        /// <summary>Quantity used, when applicable.</summary>
        public decimal? Quantity { get; }

        /// <summary>Unit price used, when applicable.</summary>
        public decimal? UnitPrice { get; }

        /// <summary>Computed amount of the line.</summary>
        public decimal Amount { get; }

        /// <summary>Whether the line contributed to the contributory base.</summary>
        public bool IsCnasApplicable { get; }

        /// <summary>Whether the line contributed to the taxable base.</summary>
        public bool IsIrgApplicable { get; }

        /// <summary>Position of the line on the payslip.</summary>
        public int DisplayOrder { get; }

        /// <summary>Creates an immutable line result.</summary>
        public PayrollLineResult(
            long? elementId,
            string labelFr,
            string labelAr,
            ElementType elementType,
            decimal? @base,
            decimal? rate,
            decimal? quantity,
            decimal? unitPrice,
            decimal amount,
            bool isCnasApplicable,
            bool isIrgApplicable,
            int displayOrder)
        {
            ElementId = elementId;
            LabelFr = labelFr;
            LabelAr = labelAr;
            ElementType = elementType;
            Base = @base;
            Rate = rate;
            Quantity = quantity;
            UnitPrice = unitPrice;
            Amount = amount;
            IsCnasApplicable = isCnasApplicable;
            IsIrgApplicable = isIrgApplicable;
            DisplayOrder = displayOrder;
        }
    }
}
