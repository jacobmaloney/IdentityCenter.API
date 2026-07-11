using System.Runtime.CompilerServices;

// The test project pins internal seams (migration checksum classification, control-plane SQL
// shapes, row-shape invariants) without widening their visibility.
[assembly: InternalsVisibleTo("IdentityCenter.Tests")]
