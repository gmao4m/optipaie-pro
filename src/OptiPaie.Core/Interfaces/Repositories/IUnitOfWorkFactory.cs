namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Creates a fresh <see cref="IUnitOfWork"/> (and underlying connection) per
    /// logical operation. Services request a unit of work, use it and dispose it,
    /// which keeps connections short-lived and the design testable (the factory is
    /// trivially mockable).
    /// </summary>
    public interface IUnitOfWorkFactory
    {
        /// <summary>Creates and returns a new unit of work with an open connection.</summary>
        IUnitOfWork Create();
    }
}
