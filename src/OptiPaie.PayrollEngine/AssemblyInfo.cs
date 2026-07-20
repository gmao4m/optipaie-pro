using System.Runtime.CompilerServices;

// Exposes the engine's internal pipeline (calculation context and rules) to the
// test project so each rule can be unit-tested in isolation, while keeping the
// public surface limited to the engine, calculators and legal profile types.
[assembly: InternalsVisibleTo("OptiPaie.Tests")]
