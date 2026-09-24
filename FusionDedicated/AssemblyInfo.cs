using System.Runtime.CompilerServices;

// Lets the test project reach a few internal details directly, rather than
// rebuilding malformed wire messages just to hit a branch reflection would
// reach in one line.
[assembly: InternalsVisibleTo("FusionDedicated.Tests")]
