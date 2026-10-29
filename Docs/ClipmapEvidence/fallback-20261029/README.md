# Standing-still clipmap fallback repair

The user's Play reached fine coverage, then fell back with repeated worker
failures. Initial logging omitted the nested exception. A manual retry with
expanded worker diagnostics identified `ArgumentOutOfRangeException("climate")`
from `ExplorationRefinementField.State.TrySampleClimate`, after live patches
were published. No agent controlled Unity.

The failing job was generation69, level9, lattice rectangle(-348,-142,16,29),
spacing102.40000152587891. The captured source included
`refined-source/ab25f619be757dedd6c53750ebb0ee24d465a64831e932e55cec18c744aa8748`.
See diagnostic-user-play-failure.log for full nested exceptions and source IDs.

A production-path regression uses valid constant climates, CopySnapshot,
TryPublish and actual128m patch weights. A base value0.91 blended with three
zero-valued overlapping patches (core distances0/1/2m) produced a tiny negative
value and reproduced the same exception before the repair.

The fix only projects finite non-temperature blend values back onto their
validated bounds when the excursion is within64 machine epsilons scaled by the
channel range. Larger errors and nonfinite values still fail. Temperature's
physical-range policy is unchanged. Interior values retain exactly the same
bits; material/colour rules and source data are unchanged.

Validation:31 boundary/interior/input/temperature checks and the existing27
wrapper checks pass. All six runtime assemblies compile against the installed
Unity API. Red/green results and hashes are retained here. A separate complete
3,932,160-sample static source reproduction had no exception; it did not include
live patch blending, and is not evidence that the failing runtime was correct.

Current status: fix ready for the user's manual Play retry. Native sustained
operation, visual acceptance, frame time and memory acceptance remain pending.
Current low system commit headroom was observed but was not the proved cause of
this failure. No extra full-world probes are running during the retry.
