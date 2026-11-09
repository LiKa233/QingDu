# Audio build provenance

Original archives (unmodified):

```text
8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e  ffmpeg-9.0.2.tar.xz
d9ef89e2b8ef9f85b13ac3c2faec30e20acf2c9f3a9c8c45ce637f2bc95e576c  rubberband-3.3.0.tar.bz2
```

Source URLs and license attribution are in THIRD-PARTY-NOTICES.md. The build was made on Windows x64 with a portable official MSYS2 installation and GCC16.2.0 Rev6. The bootstrap archive was `msys2-base-x86_64-20260927.tar.zst` from https://repo.msys2.org/distrib/x86_64/ with SHA256 `8a2095647afbf799d113d0cae35f7544589d059819042635504e75bd2179bd02`. Current MSYS2 packages at build time are listed separately; later package updates may change binary hashes. This is a source-reproducible recipe, not a claim of deterministic byte-identical builds with arbitrary toolchain versions.

Run scripts/build-media.sh from MSYS2 MINGW64 with a new writable build directory. It verifies both archives, builds a static Rubber Band library with built-in FFT and resampler, then builds a static minimal FFmpeg. No third-party FFT or sample-rate conversion library is needed. Network protocols and automatic external-library discovery are disabled. The enabled audio paths are MP3 and PCM decoding, WAV/raw PCM demux/mux, PCM encoding, resampling, atempo and rubberband. FFmpeg CLI-required helper filters may also be enabled by FFmpeg's build dependencies.

The executable imports only Windows system DLLs: bcrypt.dll, KERNEL32.dll, msvcrt.dll and SHELL32.dll. No external GCC/MSYS2 DLL, Python, CUDA or model runtime is required. GCC runtime code is statically linked under its runtime exception. The complete corresponding source bundle includes the unmodified archives, scripts/build-media.sh, all license files, the version output, enabled configuration headers and toolchain inventory. Configure metadata uses a generic prefix and relative include/library paths, rather than a developer's home path.
