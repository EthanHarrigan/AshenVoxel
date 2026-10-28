# Binary transport fixture

`signed-negative-fake.atdr` contains a deliberately fake4×5 region at native
row−3/column−2. It is a format/CRC/axis test, not neural terrain. The height
formula is `row*100+column+0.125`; five climate planes add0/1000/2000/3000/4000,
source biome is9, and land is height>=0. The file explicitly labels its backend
FAKE-CONTRACT-TEST-NOT-NEURAL. Read the world ID from its metadata rather than
assuming a particular environment's job fingerprint.
