# ispc_texcomp.dll

Intel's [ISPC Texture Compressor](https://github.com/GameTechDev/ISPCTextureCompressor) (MIT, see `LICENSE.txt`),
built for x64 Windows. OrthoStreamXP uses only its BC1 encoder (`CompressBlocksBC1`), from
`src/OrthoStreamXP/Textures/Dds/IspcBc1.cs`; when the DLL cannot be loaded, the program falls back to its own
encoder.

Why: on 4096 px aerial textures it encodes BC1 6-8 times faster than the built-in range fit (Ryzen 7 9800X3D,
the 13-level chain on one thread: ~60 ms against ~370 ms; ~12 ms against ~95 ms on 4 threads) and 0.1-0.4 dB PSNR
better at the full size, 0.4-0.9 dB at the smaller levels. `IspcBc1` replaces its one-colour blocks (ISPC lands up
to a 565 step off a flat colour) with the endpoint pair that mixes closest, keeps every block in the opaque
four-colour mode, and refines every other block's two colours by least squares (`Bc1Refinement`, two passes, as
nvtt's fast mode does; ISPC fits a block once): about 0.4 dB closer on cached Bing imagery, the chain ~46 ms
instead of ~11 ms on 4 threads.

The DLL is checked in, built by `build.ps1` from pinned sources (compressor commit `79ddbc9`, ISPC v1.31.0), with
Intel's flags plus an AVX-512 target; ISPC picks SSE2, SSE4, AVX, AVX2 or AVX-512 by the CPU at run time. To
rebuild, with the Visual Studio C++ build tools (x64) installed:

```powershell
.\native\ispc_texcomp\build.ps1
```
