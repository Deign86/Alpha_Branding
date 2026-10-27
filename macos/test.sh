#!/bin/bash
# Runs the branding engine checks against generated photos and videos. Outputs land in macos/build/engine-check.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p build
swiftc -swift-version 5 -parse-as-library \
  Tests/EngineCheck.swift Sources/ImageEngine.swift Sources/Models.swift \
  Sources/FileNameGenerator.swift Sources/VideoEngine.swift \
  -o build/engine-check
build/engine-check build/engine-check-output ../src/Alpha.Branding/Assets/alpha_branding.png
