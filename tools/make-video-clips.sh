#!/bin/bash
# Makes the short clips the GPU video decode test plays (src/Mazesta.Diagnostics.Gpu/Benchmarks/Video, embedded in the assembly).
# A card decodes what it was built for whatever encoders it has, so the streams are made here once, in software, and shipped:
# AV1 on a card without an AV1 encoder (RTX 30), VP9 (no card encodes it), 10-bit and 4:2:2.
# Name: <codec>-<height>p-<bits>b-<chroma>.mp4. Needs ffmpeg with libx264, libx265, libvpx-vp9 and libsvtav1. Run from the repo root.
set -euo pipefail
out=src/Mazesta.Diagnostics.Gpu/Benchmarks/Video; mkdir -p "$out"; rm -f "$out"/*.mp4
frames=30
clip() {   # codec height bits chroma(420|422) bitrate-kbps
  local codec=$1 h=$2 bits=$3 chroma=$4 kbps=$5 w
  case $h in 1080) w=1920;; 2160) w=3840;; 4320) w=7680;; esac
  local pix="yuv${chroma}p"; [ "$bits" = 10 ] && pix="${pix}10le"
  local args
  case $codec in
    h264) args="-c:v libx264 -preset veryfast -x264-params keyint=$frames:bframes=2";;
    hevc) args="-c:v libx265 -preset ultrafast -tag:v hvc1 -x265-params keyint=$frames:bframes=2:log-level=error";;
    vp9)  args="-c:v libvpx-vp9 -deadline realtime -cpu-used 8 -row-mt 1 -g $frames";;
    av1)  args="-c:v libsvtav1 -preset 12 -g $frames -svtav1-params log-level=1";;
  esac
  ffmpeg -loglevel error -y -f lavfi -i "testsrc2=size=${w}x${h}:rate=30,noise=alls=6:allf=t" -frames:v $frames -pix_fmt $pix \
    $args -b:v ${kbps}k $([ $codec = av1 ] || echo "-maxrate ${kbps}k -bufsize $((kbps*2))k") -an -movflags +faststart "$out/$codec-${h}p-${bits}b-$chroma.mp4"
}
for c in h264 hevc vp9 av1; do clip $c 1080 8 420 4000; clip $c 2160 8 420 12000; clip $c 2160 10 420 12000; done
for c in h264 hevc; do clip $c 2160 8 422 14000; clip $c 2160 10 422 14000; done
for c in hevc av1; do clip $c 4320 10 420 30000; done
ls -la "$out"
