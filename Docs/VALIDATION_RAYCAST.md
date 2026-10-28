# Independent voxel-ray validation

**Executed: PASS, 11,637 accepted comparisons** on 2026-10-26 using the installed .NET 8 SDK **8.0.427** and its Roslyn compiler. No Unity Editor or GPU was involved. This validates the engine-independent targeting query against an independently constructed geometric oracle; it does not validate Unity physics, rendered surfaces, shaders, or player movement.

The fixed PRNG seed is **2770235713** (`0xA51E7141`). Full result metadata and exact source SHA-256 hashes are in [raycast-validation.json](raycast-validation.json).

## Reproduce without restore or network

From the Unity project root, with Python 3 and .NET 8 SDK/reference assemblies installed:

```bash
python3 Tools/run_raycast_checks.py
```

Or choose an installed SDK explicitly:

```bash
python3 Tools/run_raycast_checks.py --dotnet /path/to/dotnet
```

The runner invokes the installed compiler directly with warnings treated as errors and references the installed framework assemblies. It does not invoke NuGet restore. It compiles the real core files plus [the oracle program](../Tools/RaycastChecks/Program.cs), runs the comparisons, and writes the result JSON only after successful execution. It records the source hashes and rejects a run whose sources changed during compilation/execution.

## Independent oracle

The tested implementation uses grid DDA traversal. The oracle uses a different algorithm:

1. Enumerate every occupied solid cell in the small test world.
2. Construct its physical axis-aligned box from cell coordinates and voxel width.
3. Intersect the ray with that box using three independent slab intervals.
4. Reject boxes with no positive-length ray interior, boxes behind the origin, and entries beyond maximum reach.
5. Choose the nearest accepted box across the entire world.
6. Compare hit/miss, exact cell identity, and physical entry distance against `VoxelRaycast.TryCast`.

The oracle has no grid stepping, next-boundary state, chunk traversal, neighbor progression, or call back into the DDA. It normalizes the supplied direction so its intersection parameter is meters, independently of the input magnitude.

Randomized fixtures use 100 small worlds with 4–12 cells along each axis, empty/full/mixed occupancy, varying solid material IDs, and 110 primary rays per world. Additional probes put maximum reach just before and just after the oracle's first hit. The tested scales include the required **0.1 m** grid plus exact-binary **0.125 m** and **0.03125 m** core-coordinate stress cases; these optional test scales do not change the game's 10 cm requirement.

Directions include arbitrary oblique rays, all six axial signs, mixed zero components, and magnitudes scaled through approximately ten orders of magnitude. Origins include world interior, solid interior, outside both sides of the domain, and rays aimed toward or away from it. Eighteen targeted fixtures add zero direction, zero reach inside a solid, exact reach, face ownership, and edge/corner contact cases.

## Contact semantics and ambiguity

A ray that merely touches a cell at a point or along an edge without entering its interior must not hit that side cell. Targeted exact diagonal fixtures place a side-touching solid before a genuinely entered diagonal cell and verify the latter is selected.

For a direction component equal to zero, a ray lying in an axis plane uses half-open cell ownership `[minimum, maximum)`. An entry at exactly maximum reach is included if continuing the ray would enter that cell. A ray starting inside solid returns distance zero. Facing away from a solid face must not count that face as an interior hit.

Random cases with nearly zero-length intersections, near-equal cell-entry events, or maximum-reach uncertainty within `voxelSize × 10⁻⁷` are excluded rather than assigning arbitrary ownership. **85 cases were excluded** under that rule. The exact targeted point/edge fixtures are retained with their stated semantics. This avoids claiming that a loose numerical comparison proves every floating-point grazing configuration.

Accepted distance error tolerance is `max(2 × 10⁻⁶ m, voxelSize × 2 × 10⁻⁵ + expectedDistance × 2 × 10⁻⁶)`. Hit cell identity must match exactly; distance tolerance cannot conceal a wrong cell.

## Observed coverage

| Observation | Count/result |
| --- | ---: |
| Accepted comparisons | 11,637 |
| Excluded ambiguous random comparisons | 85 |
| Hits | 4,767 |
| Misses | 6,870 |
| Origin outside finite domain | 8,578 |
| Origin inside solid | 1,772 |
| Direction with at least one negative component | 8,677 |
| Non-unit nonzero input direction | 11,614 |
| Maximum observed entry-distance error | 1.1804507993318225 × 10⁻⁷ m |

No mismatch was found and no core code was modified for this validation. The program fails if accepted or category coverage drops below minimum thresholds, so a run that accidentally skips most difficult cases cannot silently pass.

## Limits

The oracle is exhaustive over cells in each fixture, not over every possible world/ray. It does not compare the returned `Previous` cell, invalid NaN/infinite inputs, renderer/collider agreement, or concurrent mutation. Existing [core checks](VALIDATION_CORE.md) cover additional explicit invalid-input and brush behaviors. The Unity [playtest protocol](PLAYTEST.md) remains necessary for user-visible targeting and destruction.
