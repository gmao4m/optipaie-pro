namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Produces a stable identifier for this machine, used to bind a license to a
    /// single computer. Derived from hardware characteristics so it survives a
    /// Windows reinstall of the same machine but differs across machines.
    /// </summary>
    public interface IDeviceIdentity
    {
        /// <summary>A stable, opaque device id (never throws — falls back if hardware info is unavailable).</summary>
        string GetDeviceId();

        /// <summary>A short human-readable description of the device (machine / OS / user) for the admin panel.</summary>
        string GetDeviceInfo();
    }
}
