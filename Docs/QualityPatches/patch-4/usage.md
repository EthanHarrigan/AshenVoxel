# DLSS/DLAA patch

F1 > Image quality cycles native SMAA, NVIDIA DLAA and DLSS Quality. Save these
options persists the selection. Default DLAA retains full-resolution rendering.
DLSS Quality uses NVIDIA's recommended input resolution. Unsupported hardware
falls back to native SMAA. History resets after floating-origin changes.

This uses Unity17.3's installed preview IUpscaler framework and NVIDIA module.
The native DX12 test ran real GPU contexts on RTX3080Ti/driver610.88; no driver
update was needed. Frame Generation is unavailable on this RTX30 card according
to [NVIDIA's supported GPU table](https://www.nvidia.com/en-us/geforce/technologies/dlss/).
Camera motion in a small fixture is verified; full-world ghosting, frame times
and repeated mode-switch stress remain acceptance gates.
