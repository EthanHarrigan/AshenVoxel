# Quality patch 6 — wind and restrained material coverage

Installed live on 2026-10-27 UTC: all 20 source hashes match the bounded native
DX12 test, and the main Editor compiled them. The source manifest and native
logs/results retain their original identities. No world samples, generator,
driver, OS setting, running terrain service, or gameplay collision was changed.

- F1 **Wind strength**, default 1, range 0–2; zero disables animation. Wood,
  leaves, fruit and petals share broad branch sway. Leaves and petals add up to
  2.5 cm flutter at default strength. Roots stay fixed. This is visual movement;
  collision remains authoritative and stationary.
- All near forward/shadow/depth/normal passes share deformation. The new motion
  pass has previous/current wind and floating-origin history. Immutable instanced
  placements explicitly opt into that history. Culling bounds have 2 m padding;
  maximum analytical displacement at strength 2 is 1.67 m. Far PNG trees stay
  static because their motion is subpixel and billboard history is not retained.
- Default leaves are opaque outside the small 0.15–0.8 m close-eye fade (maximum
  6%). Transmission still lights their backs. Ice opacity is .98, petals .99 and
  webs .65. Every material still has manual opacity/transmission controls.
- Coverage is attached to undeformed 1 cm surface cells, shared across passes,
  rather than the screen grid. Terrain anchors survive origin wrapping. Shadow
  transmission coverage has only an 8% maximum accent.
- A passive editor heartbeat records population/horizon/loading counters. Camera
  capture saves a diagnostic before the capture's draw-submission tick.

## Executed verification

Native Unity 6000.3.27f1, DX12, RTX 3080 Ti: 24 manual material opacity tests,
default coverage, near-eye fade and directional transmission passed. Frozen
coverage changed zero pixels; one-pixel camera translation changed zero of 6,084
compared surface pixels. Leaf/wood wind changed 4,085/3,972 pixels, while stone,
roots and frozen-time frames changed zero. All five WorldSurface passes and all
changed terrain/forest passes compiled. Three actual tree prototypes passed
sun-facing, backlit and sky-only brightness gates. Main Editor's native mesh
regression then passed 32,198 unit faces and 128,416 AO vertices across 12 worlds.

CPU/shim validation: 31 bootstrap, 31 streaming, 27 shading contract checks;
five runtime assemblies compiled against installed Unity APIs. A 4,000-sample
wind math audit checks derivatives, normals, bounds and origin continuity.
These measurements are not full-scene FPS or DLAA/DLSS temporal-history tests.
Visual motion feel, camera travel and temporal ghosting remain Play acceptance.

## Separate missing-middle-distance diagnosis

The other chat reproduced the user's complaint on the imported world: all
11,734 trees generated, but only 405 submitted when near meshes consumed the
32M triangle budget. A candidate reserves fallback tree coverage and improves
publication scheduling. That candidate is separate from patch 6 and remains
under validation; consult its own later evidence before claiming it is live.

Main Play follow-up at2026-10-27T06:46:52UTC: Ready on importedworld14fb32,
DLAA active, world/terrain/forest shadererror flagsfalse. Passive snapshot records
8,761 resident instances in92cells with1,622pending; this early capture is
not evidence of complete distant residency or the separate schedulingfix.
See live/native.json and live/view.png.
