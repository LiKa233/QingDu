# Third-party notices

QingDu is distributed under GPL-3.0. Full license texts are in `licenses/`.

| Component | Version | Source / license |
| --- | --- | --- |
| FFmpeg | 9.0.2 | https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz ; configured with GPL and version3; resulting executable GPLv3-or-later |
| Rubber Band Library | 3.3.0 | https://breakfastquay.com/files/releases/rubberband-3.3.0.tar.bz2 ; GPLv2-or-later, combined here under GPLv3-or-later |
| GCC runtime, libstdc++ | 16.2.0 (MSYS2 Rev6) | GPLv3 with GCC Runtime Library Exception 3.1; exception and license included |
| MinGW-w64 CRT, headers, winpthreads | 14.0.0 (MSYS2 snapshots) | permissive notices, public-domain portions and third-party notices reproduced in licenses/crt, headers, libwinpthread |

FFmpeg Copyright (c) 2000-2026 the FFmpeg developers. Rubber Band Copyright (c) 2007-2023 Particular Programs Ltd and contributors. See their original source archives for all component-level notices, including the built-in FFT/resampler and bundled helper code. No modifications to upstream sources were made. `third_party/patches/README.md` records this fact.

The separate `QingDu-v0.2.6-corresponding-source.zip` contains the exact FFmpeg/Rubber Band source archives, source hashes, build recipe, configuration and licenses for the shipped executable. The GitHub repository also contains those source archives. No source-code offer is substituted for the actual source delivery. GCC runtime redistribution relies on its runtime exception; Windows system DLLs and .NET Framework are system prerequisites and are not redistributed.

Build tooling (MSYS2, Meson, Ninja, compiler, NASM) is not shipped as an application runtime. Source provenance and toolchain inventory are in `third_party/BUILD.md` and `third_party/toolchain-packages.txt`. This application is independent of MiMo and Fish Audio; those names identify third-party services, whose own terms govern API use.

The application logo is included as a project asset under the project license. `tests/tone.mp3` is a synthetic 440Hz sine-wave test fixture, dedicated to the public domain under CC0: https://creativecommons.org/publicdomain/zero/1.0/ . It is not a voice sample.
