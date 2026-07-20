using System;
using OptiPaie.Core.Primitives;

namespace OptiPaie.PayrollEngine.Money
{
    /// <summary>
    /// The single point through which every statutory monetary value is rounded.
    /// Arithmetic is performed in <see cref="decimal"/> (never float/double); the
    /// rounding policy (whole dinar vs. centime) is injected from the legal snapshot,
    /// so the entire engine rounds consistently.
    /// </summary>
    public sealed class MoneyEngine
    {
        private readonly RoundingPolicy _rounding;

        public MoneyEngine(RoundingPolicy rounding)
        {
            _rounding = rounding ?? throw new ArgumentNullException(nameof(rounding));
        }

        /// <summary>Rounds an amount according to the active rounding policy.</summary>
        public decimal Round(decimal amount)
        {
            return _rounding.Round(amount);
        }
    }
}
