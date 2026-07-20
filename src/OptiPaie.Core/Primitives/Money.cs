using System;
using System.Globalization;

namespace OptiPaie.Core.Primitives
{
    /// <summary>
    /// An immutable monetary amount in Algerian Dinar (DZD), backed by
    /// <see cref="decimal"/> to avoid binary floating-point error.
    /// <para>
    /// Arithmetic preserves full precision; rounding is never implicit. Round
    /// explicitly through a <see cref="RoundingPolicy"/> at the statutory points
    /// defined by the Payroll Engine Specification. This value object documents
    /// monetary intent and provides value equality.
    /// </para>
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        /// <summary>The exact amount, in dinars.</summary>
        public decimal Amount { get; }

        /// <summary>Creates a money value from an exact decimal amount.</summary>
        public Money(decimal amount)
        {
            Amount = amount;
        }

        /// <summary>Zero dinars.</summary>
        public static Money Zero => new Money(0m);

        /// <summary>True when the amount is exactly zero.</summary>
        public bool IsZero => Amount == 0m;

        /// <summary>True when the amount is strictly negative.</summary>
        public bool IsNegative => Amount < 0m;

        /// <summary>Creates a money value from a decimal amount.</summary>
        public static Money FromDecimal(decimal amount)
        {
            return new Money(amount);
        }

        /// <summary>Returns a new money value rounded per the supplied policy.</summary>
        public Money Round(RoundingPolicy policy)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            return new Money(policy.Round(Amount));
        }

        public static Money operator +(Money left, Money right) => new Money(left.Amount + right.Amount);

        public static Money operator -(Money left, Money right) => new Money(left.Amount - right.Amount);

        public static Money operator -(Money value) => new Money(-value.Amount);

        /// <summary>Scales a money amount by a unitless factor (e.g. a rate or quantity).</summary>
        public static Money operator *(Money value, decimal factor) => new Money(value.Amount * factor);

        /// <summary>Scales a money amount by a unitless factor (e.g. a rate or quantity).</summary>
        public static Money operator *(decimal factor, Money value) => new Money(value.Amount * factor);

        public static bool operator ==(Money left, Money right) => left.Equals(right);

        public static bool operator !=(Money left, Money right) => !left.Equals(right);

        public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

        public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

        public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

        public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

        public bool Equals(Money other) => Amount == other.Amount;

        public override bool Equals(object obj) => obj is Money other && Equals(other);

        public override int GetHashCode() => Amount.GetHashCode();

        public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

        /// <summary>Culture-invariant representation, used for logging and snapshots (not display).</summary>
        public override string ToString() => Amount.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
