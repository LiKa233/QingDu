#!/usr/bin/env bash
set -euo pipefail
export PATH=/mingw64/bin:/usr/bin:$PATH
repo=$(cd "$(dirname "$0")/.." && pwd)
work=${1:?Usage: build-media.sh WORK_DIRECTORY}
mkdir -p "$work"; work=$(cd "$work" && pwd)
prefix="$work/prefix"
cd "$repo/third_party/sources"
echo '8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e  ffmpeg-9.0.2.tar.xz' | sha256sum -c
echo 'd9ef89e2b8ef9f85b13ac3c2faec30e20acf2c9f3a9c8c45ce637f2bc95e576c  rubberband-3.3.0.tar.bz2' | sha256sum -c
mkdir -p "$work/sources"
tar -xf ffmpeg-9.0.2.tar.xz -C "$work/sources"
tar -xf rubberband-3.3.0.tar.bz2 -C "$work/sources"
meson setup "$work/rubberband-build" "$work/sources/rubberband-3.3.0" --prefix="$prefix" --libdir=lib --buildtype=release -Ddefault_library=static -Dfft=builtin -Dresampler=builtin -Djni=disabled -Dladspa=disabled -Dlv2=disabled -Dvamp=disabled -Dcmdline=disabled -Dtests=disabled
meson compile -C "$work/rubberband-build"
meson install -C "$work/rubberband-build"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
mkdir -p "$work/ffmpeg-build"; cd "$work/ffmpeg-build"
export CFLAGS="-ffile-prefix-map=$(cygpath -m "$work")=qingdu-build -ffile-prefix-map=$work=qingdu-build"
../sources/ffmpeg-9.0.2/configure --prefix=/qingdu-media --target-os=mingw32 --arch=x86_64 --enable-gpl --enable-version3 --enable-librubberband --disable-autodetect --disable-shared --enable-static --disable-debug --disable-doc --disable-network --disable-everything --enable-ffmpeg --disable-ffplay --disable-ffprobe --enable-protocol=file,pipe --enable-demuxer=mp3,pcm_s16le,wav --enable-muxer=pcm_s16le,wav --enable-decoder=mp3float,pcm_s16le --enable-encoder=pcm_s16le --enable-parser=mpegaudio --enable-filter=abuffer,abuffersink,aformat,anull,aresample,atempo,rubberband --extra-cflags="-I../prefix/include" --extra-ldflags="-L../prefix/lib -static" --extra-libs="-lstdc++ -lpthread"
make -j4

mkdir -p "$repo/build/media"
cp ffmpeg.exe "$repo/build/media/ffmpeg.exe"
strip "$repo/build/media/ffmpeg.exe"
"$repo/build/media/ffmpeg.exe" -version > "$repo/build/media/version.txt"
objdump -p "$repo/build/media/ffmpeg.exe" | sed -n '/DLL Name/p' > "$repo/build/media/imports.txt"
cp config.h config_components.h ffbuild/config.mak "$repo/build/media/"
gcc --version > "$repo/build/media/compiler.txt"
