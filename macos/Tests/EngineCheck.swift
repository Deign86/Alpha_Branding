import AppKit
import AVFoundation
import ImageIO
import UniformTypeIdentifiers

/// Exercises the branding engine on generated photos and videos and checks output pixels.
/// Run through macos/test.sh; exits non-zero on any failure.
@main
struct EngineCheck {
    typealias RGB = (r: Int, g: Int, b: Int)

    static let red = CGColor(srgbRed: 1, green: 0, blue: 0, alpha: 1)
    static let green = CGColor(srgbRed: 0, green: 1, blue: 0, alpha: 1)
    static let blue = CGColor(srgbRed: 0, green: 0, blue: 1, alpha: 1)
    static let yellow = CGColor(srgbRed: 1, green: 1, blue: 0, alpha: 1)
    static let white = CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1)
    static let canvas: RGB = (18, 18, 18)

    static var failures = 0

    static func main() async throws {
        let output = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "build/engine-check")
        try? FileManager.default.removeItem(at: output)
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        func file(_ name: String) -> URL { output.appendingPathComponent(name) }

        checkFileNames()

        // Photos. "rotated.jpg" stores landscape pixels with EXIF orientation 6, like a sideways phone shot.
        let landscape = try writeJPEG(solid(4000, 3000, blue), to: file("landscape.jpg"))
        let portraitA = try writeJPEG(solid(3000, 4000, red), to: file("portrait_a.jpg"))
        let portraitB = try writeJPEG(solid(3000, 4000, green), to: file("portrait_b.jpg"))
        let rotated = try writeJPEG(solid(4000, 3000, yellow), to: file("rotated.jpg"), orientation: 6)
        let twoTone = try writeJPEG(halves(400, 200, left: red, right: green), to: file("two_tone.jpg"))
        let overlay = try ImageEngine.renderOverlay(try writePNG(markerOverlay(), to: file("overlay.png")))

        check(ImageEngine.pixelSize(of: rotated) == CGSize(width: 3000, height: 4000), "EXIF orientation applied to size")
        check(ImageEngine.isPortrait(rotated), "sideways phone photo counts as portrait")
        let decodedRotated = try ImageEngine.loadImage(rotated)
        check(decodedRotated.width == 3000 && decodedRotated.height == 4000, "EXIF orientation applied when decoding")

        checkPlanner(landscape: landscape, portraitA: portraitA, portraitB: portraitB)

        // Pair: left half from the first portrait, right half from the second, overlay on top.
        let pair = try ImageEngine.compose(.pair(portraitA, portraitB, CropSettings(), CropSettings()), overlay: overlay)
        check(pair.width == 1200 && pair.height == 1000, "output is 1200×1000")
        expect(pair, 300, 500, (255, 0, 0), "pair left half shows first photo")
        expect(pair, 900, 500, (0, 255, 0), "pair right half shows second photo")
        expect(pair, 50, 50, (255, 255, 255), "overlay drawn on top, upright (top-left marker)")

        let filled = try ImageEngine.compose(.landscape(landscape, CropSettings()), overlay: overlay)
        expect(filled, 1190, 990, (0, 0, 255), "landscape fills the frame")

        let soloPortrait = try ImageEngine.compose(.solo(rotated, CropSettings()), overlay: overlay)
        expect(soloPortrait, 600, 500, (255, 255, 0), "solo portrait centered")
        expect(soloPortrait, 150, 500, canvas, "solo portrait is fitted, not cropped")

        var panned = CropSettings()
        panned.panX = 300
        let pannedImage = try ImageEngine.compose(.landscape(landscape, panned), overlay: overlay)
        expect(pannedImage, 150, 500, canvas, "pan right reveals the canvas on the left")
        expect(pannedImage, 600, 500, (0, 0, 255), "pan keeps the photo under the center")

        var turned = CropSettings()
        turned.rotation = 90
        let turnedImage = try ImageEngine.compose(.landscape(twoTone, turned), overlay: overlay)
        expect(turnedImage, 600, 250, (255, 0, 0), "rotate 90° is clockwise (left edge moves to the top)")
        expect(turnedImage, 600, 750, (0, 255, 0), "rotate 90° is clockwise (right edge moves to the bottom)")

        var marked = CropSettings()
        marked.strokes = [RedactStroke(points: [CGPoint(x: 0.45, y: 0.5), CGPoint(x: 0.55, y: 0.5)], brushSizeNormalized: 0.05)]
        let markedImage = try ImageEngine.compose(.landscape(landscape, marked), overlay: overlay)
        expect(markedImage, 600, 500, (0, 0, 0), "pen mark burned into the export")
        expect(markedImage, 600, 600, (0, 0, 255), "pen mark stays where it was drawn")

        checkFramingRoundTrip()

        let jpeg = try ImageEngine.jpegData(pair)
        let decoded = CGImageSourceCreateWithData(jpeg as CFData, nil).flatMap { CGImageSourceCreateImageAtIndex($0, 0, nil) }
        check(decoded?.width == 1200 && decoded?.height == 1000, "JPEG encodes at 1200×1000")

        // Videos: a 16:9 clip is letterboxed; a clip recorded sideways (90° track transform) is pillarboxed.
        let wide = file("wide.mov")
        try await writeVideo(wide, width: 1280, height: 720, transform: .identity, color: blue)
        let wideOut = file("wide_branded.mp4")
        try await VideoEngine.brand(wide, overlay: overlay, to: wideOut) { _ in }
        if let frame = try await videoFrame(wideOut) {
            check(frame.width == 1200 && frame.height == 1000, "branded video is 1200×1000")
            expect(frame, 600, 500, (0, 0, 255), "video frame centered")
            expect(frame, 600, 100, (0, 0, 0), "16:9 video letterboxed on black")
            expect(frame, 50, 50, (255, 255, 255), "overlay drawn on video, upright")
        } else {
            check(false, "branded video has a readable frame")
        }

        let tall = file("tall.mov")
        try await writeVideo(tall, width: 1280, height: 720, transform: CGAffineTransform(rotationAngle: .pi / 2), color: red)
        let tallOut = file("tall_branded.mp4")
        try await VideoEngine.brand(tall, overlay: overlay, to: tallOut) { _ in }
        if let frame = try await videoFrame(tallOut) {
            expect(frame, 600, 500, (255, 0, 0), "rotated video frame centered")
            expect(frame, 150, 500, (0, 0, 0), "rotated phone video shown upright (pillarboxed)")
        } else {
            check(false, "rotated branded video has a readable frame")
        }

        // Real template samples for a visual check.
        if CommandLine.arguments.count > 2 {
            let template = try ImageEngine.renderOverlay(URL(fileURLWithPath: CommandLine.arguments[2]))
            try ImageEngine.jpegData(try ImageEngine.compose(.pair(portraitA, rotated, CropSettings(), CropSettings()), overlay: template))
                .write(to: file("sample_pair.jpg"))
            try ImageEngine.jpegData(try ImageEngine.compose(.landscape(landscape, marked), overlay: template))
                .write(to: file("sample_landscape.jpg"))
        }

        print(failures == 0 ? "All engine checks passed." : "\(failures) engine check(s) failed.")
        exit(failures == 0 ? 0 : 1)
    }

    static func check(_ condition: Bool, _ name: String) {
        print(condition ? "PASS" : "FAIL", name)
        if !condition { failures += 1 }
    }

    static func expect(_ image: CGImage, _ x: Int, _ y: Int, _ wanted: RGB, _ name: String) {
        let got = pixel(image, x, y)
        let close = abs(got.r - wanted.r) < 48 && abs(got.g - wanted.g) < 48 && abs(got.b - wanted.b) < 48
        check(close, close ? name : "\(name) — pixel (\(x), \(y)) is \(got), expected \(wanted)")
    }

    static func checkFileNames() {
        check(FileNameGenerator.generate(nil, index: 0, total: 10) == "AlphaPremier_Photo_01.jpg", "default file name")
        check(FileNameGenerator.generate("Sunset: Villa/7", index: 4, total: 10) == "Sunset Villa7_05.jpg", "unsafe characters removed")
        check(FileNameGenerator.generate("Lot", index: 0, total: 120, kind: .video) == "Lot_001.mp4", "three digits from 100 items")
        check(FileNameGenerator.generate("CON", index: 0, total: 2) == "AlphaPremier_Photo_01.jpg", "reserved names fall back")
        check(FileNameGenerator.generate("...", index: 0, total: 2) == "AlphaPremier_Photo_01.jpg", "dots-only prefix falls back")
    }

    static func checkPlanner(landscape: URL, portraitA: URL, portraitB: URL) {
        func staged(_ url: URL) -> StagedItem { StagedItem(url: url, kind: .image, sizeText: "") }
        let none = CropSettings()

        let combined = ImageEngine.planBatch([staged(landscape), staged(portraitA), staged(portraitB)], mode: .combine)
        check(combined == [.landscape(landscape, none), .pair(portraitA, portraitB, none, none)], "portraits paired, landscape alone")

        let odd = ImageEngine.planBatch([staged(portraitA), staged(landscape)], mode: .combine)
        check(odd == [.pair(portraitA, landscape, none, none)], "odd portrait matched with a landscape")

        let lone = ImageEngine.planBatch([staged(portraitA)], mode: .combine)
        check(lone == [.pair(portraitA, portraitA, none, none)], "lone portrait shown side by side with itself")

        let separate = ImageEngine.planBatch([staged(landscape), staged(portraitA)], mode: .separate)
        check(separate == [.solo(landscape, none), .solo(portraitA, none)], "keep separate makes solos")

        var left = staged(landscape), right = staged(portraitB)
        left.partner = portraitB
        right.partner = landscape
        let explicit = ImageEngine.planBatch([left, staged(portraitA), right], mode: .combine)
        check(explicit == [.pair(landscape, portraitB, none, none), .pair(portraitA, portraitA, none, none)],
              "explicit pair wins over automatic pairing")

        var solo = staged(portraitA)
        solo.isSolo = true
        let withSolo = ImageEngine.planBatch([solo, staged(portraitB)], mode: .combine)
        check(withSolo == [.solo(portraitA, none), .pair(portraitB, portraitB, none, none)], "solo photo excluded from pairing")
    }

    static func checkFramingRoundTrip() {
        for rotation in [0, 90, 180, 270] {
            var crop = CropSettings()
            crop.rotation = rotation
            crop.zoom = 1.3
            crop.panX = 40
            crop.panY = -25
            let framing = ImageEngine.Framing(crop: crop, sourceWidth: 4000, sourceHeight: 3000, targetWidth: 1200, targetHeight: 1000)
            let point = CGPoint(x: 0.37, y: 0.61)
            let back = framing.normalizedPoint(fromTarget: framing.targetPoint(fromNormalized: point))
            check(abs(back.x - point.x) < 1e-9 && abs(back.y - point.y) < 1e-9, "pen coordinates round-trip at \(rotation)°")
        }
    }

    // MARK: Fixtures

    static func solid(_ width: Int, _ height: Int, _ color: CGColor) -> CGImage {
        let context = ImageEngine.makeContext(width: width, height: height, fill: color)
        return context.makeImage()!
    }

    static func halves(_ width: Int, _ height: Int, left: CGColor, right: CGColor) -> CGImage {
        let context = ImageEngine.makeContext(width: width, height: height, fill: right)
        context.setFillColor(left)
        context.fill(CGRect(x: 0, y: 0, width: width / 2, height: height))
        return context.makeImage()!
    }

    /// Transparent 1200×1000 frame with an opaque white square in the top-left corner.
    static func markerOverlay() -> CGImage {
        let context = ImageEngine.makeContext(width: 1200, height: 1000)
        context.setFillColor(white)
        context.fill(CGRect(x: 0, y: 900, width: 100, height: 100))
        return context.makeImage()!
    }

    static func writeJPEG(_ image: CGImage, to url: URL, orientation: Int = 1) throws -> URL {
        let destination = CGImageDestinationCreateWithURL(url as CFURL, UTType.jpeg.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(destination, image, [kCGImagePropertyOrientation: orientation] as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { throw EngineError("Couldn't write \(url.lastPathComponent)") }
        return url
    }

    static func writePNG(_ image: CGImage, to url: URL) throws -> URL {
        let destination = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(destination, image, nil)
        guard CGImageDestinationFinalize(destination) else { throw EngineError("Couldn't write \(url.lastPathComponent)") }
        return url
    }

    static func writeVideo(_ url: URL, width: Int, height: Int, transform: CGAffineTransform, color: CGColor) async throws {
        try? FileManager.default.removeItem(at: url)
        let writer = try AVAssetWriter(outputURL: url, fileType: .mov)
        let input = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: width, AVVideoHeightKey: height
        ])
        input.transform = transform
        let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: input, sourcePixelBufferAttributes: [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
            kCVPixelBufferWidthKey as String: width, kCVPixelBufferHeightKey as String: height
        ])
        writer.add(input)
        writer.startWriting()
        writer.startSession(atSourceTime: .zero)
        for frame in 0..<60 {
            while !input.isReadyForMoreMediaData { try await Task.sleep(nanoseconds: 5_000_000) }
            var buffer: CVPixelBuffer?
            CVPixelBufferPoolCreatePixelBuffer(nil, adaptor.pixelBufferPool!, &buffer)
            guard let buffer else { throw EngineError("No pixel buffer") }
            CVPixelBufferLockBaseAddress(buffer, [])
            let context = CGContext(
                data: CVPixelBufferGetBaseAddress(buffer), width: width, height: height, bitsPerComponent: 8,
                bytesPerRow: CVPixelBufferGetBytesPerRow(buffer), space: CGColorSpace(name: CGColorSpace.sRGB)!,
                bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)!
            context.setFillColor(color)
            context.fill(CGRect(x: 0, y: 0, width: width, height: height))
            CVPixelBufferUnlockBaseAddress(buffer, [])
            adaptor.append(buffer, withPresentationTime: CMTime(value: Int64(frame), timescale: 30))
        }
        input.markAsFinished()
        await writer.finishWriting()
        if writer.status != .completed { throw writer.error ?? EngineError("Couldn't write \(url.lastPathComponent)") }
    }

    static func videoFrame(_ url: URL) async throws -> CGImage? {
        let generator = AVAssetImageGenerator(asset: AVURLAsset(url: url))
        generator.appliesPreferredTrackTransform = true
        generator.requestedTimeToleranceBefore = .zero
        generator.requestedTimeToleranceAfter = .zero
        return try await generator.image(at: CMTime(seconds: 1, preferredTimescale: 600)).image
    }

    static func pixel(_ image: CGImage, _ x: Int, _ y: Int) -> RGB {
        let context = ImageEngine.makeContext(width: image.width, height: image.height)
        context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        let data = context.data!.assumingMemoryBound(to: UInt8.self)
        let offset = y * context.bytesPerRow + x * 4
        return (Int(data[offset]), Int(data[offset + 1]), Int(data[offset + 2]))
    }
}
