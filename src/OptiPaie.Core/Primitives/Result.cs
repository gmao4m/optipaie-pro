using System;

namespace OptiPaie.Core.Primitives
{
    /// <summary>
    /// Represents the outcome of an operation that can succeed or fail in an
    /// expected, business-meaningful way (e.g. validation failure, duplicate
    /// payroll). Reserved exceptions are used only for the truly unexpected.
    /// <para>
    /// On failure, <see cref="ErrorCode"/> carries a stable, localisation-friendly
    /// key (resolved to an Arabic/French message by the presentation layer) while
    /// <see cref="Error"/> holds a developer-readable fallback.
    /// </para>
    /// </summary>
    public class Result
    {
        /// <summary>True when the operation succeeded.</summary>
        public bool IsSuccess { get; }

        /// <summary>True when the operation failed. Convenience inverse of <see cref="IsSuccess"/>.</summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>Developer-readable error description. Empty on success.</summary>
        public string Error { get; }

        /// <summary>Stable error key used by the UI to look up the localised message. Empty on success.</summary>
        public string ErrorCode { get; }

        /// <summary>Initialises a new <see cref="Result"/>. Use the static factories instead.</summary>
        protected Result(bool isSuccess, string error, string errorCode)
        {
            if (isSuccess && !string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException("A successful result cannot carry an error.");
            }

            if (!isSuccess && string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException("A failed result must carry an error.");
            }

            IsSuccess = isSuccess;
            Error = error ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
        }

        /// <summary>Creates a successful result.</summary>
        public static Result Ok()
        {
            return new Result(true, string.Empty, string.Empty);
        }

        /// <summary>Creates a failed result with a message and an optional localisation key.</summary>
        public static Result Fail(string error, string errorCode = "")
        {
            return new Result(false, error, errorCode);
        }

        /// <summary>Creates a successful result carrying a value.</summary>
        public static Result<T> Ok<T>(T value)
        {
            return Result<T>.Success(value);
        }

        /// <summary>Creates a failed typed result with a message and an optional localisation key.</summary>
        public static Result<T> Fail<T>(string error, string errorCode = "")
        {
            return Result<T>.Failure(error, errorCode);
        }
    }

    /// <summary>
    /// A <see cref="Result"/> that carries a value when the operation succeeds.
    /// </summary>
    /// <typeparam name="T">Type of the produced value.</typeparam>
    public sealed class Result<T> : Result
    {
        private readonly T _value;

        private Result(bool isSuccess, T value, string error, string errorCode)
            : base(isSuccess, error, errorCode)
        {
            _value = value;
        }

        /// <summary>
        /// The produced value. Accessing it on a failed result is a programming
        /// error and throws, so callers must check <see cref="Result.IsSuccess"/> first.
        /// </summary>
        public T Value
        {
            get
            {
                if (IsFailure)
                {
                    throw new InvalidOperationException(
                        "Cannot access the value of a failed result. Check IsSuccess first.");
                }

                return _value;
            }
        }

        /// <summary>Creates a successful typed result.</summary>
        public static Result<T> Success(T value)
        {
            return new Result<T>(true, value, string.Empty, string.Empty);
        }

        /// <summary>Creates a failed typed result.</summary>
        public static Result<T> Failure(string error, string errorCode = "")
        {
            return new Result<T>(false, default(T), error, errorCode);
        }
    }
}
