import AppKit
import ImageIO
import UniformTypeIdentifiers

struct EngineError: LocalizedError {
    let errorDescription: String?
    init(_ message: String) { errorDescription = message }
}

enum ImageEngine {
    static let targetWidth = 1200
    static let targetHeight = 1000
    static let halfWidth = 600
    static let photoExtensions: Set<String> = ["jpg", "jpeg", "jfif", "png", "bmp", "webp", "heic", "heif"]
    static let canvasColor = CGColor(srgbRed: 18 / 255, green: 18 / 255, blue: 18 / 255, alpha: 1)

    /// Placement of a photo inside a target frame, in top-left target pixels. Shared by the
    /// renderer and the crop editor so the editor preview matches the exported image exactly.
    struct Framing {
        let scale: Double
        let drawX: Double
        let drawY: Double
        let sourceWidth: Double
        let sourceHeight: Double
        let rotation: Int

        init(crop: CropSettings, sourceWidth: Int, sourceHeight: Int, targetWidth: Int, targetHeight: Int) {
            rotation = crop.rotation
            self.sourceWidth = Double(sourceWidth)
            self.sourceHeight = Double(sourceHeight)
            let quarterTurn = rotation == 90 || rotation == 270
            let rotatedWidth = Double(quarterTurn ? sourceHeight : sourceWidth)
            let rotatedHeight = Double(quarterTurn ? sourceWidth : sourceHeight)
            let frameWidth = Double(targetWidth), frameHeight = Double(targetHeight)
            scale = Self.baseScale(rotatedWidth, rotatedHeight, frameWidth, frameHeight) * crop.zoom
            drawX = (frameWidth - max(1, rotatedWidth * scale)) / 2 + crop.panX
            drawY = (frameHeight - max(1, rotatedHeight * scale)) / 2 + crop.panY
        }

        static func baseScale(_ width: Double, _ height: Double, _ frameWidth: Double, _ frameHeight: Double) -> Double {
            let fit = min(frameWidth / width, frameHeight / height)
            let fill = max(frameWidth / width, frameHeight / height)
            // A portrait shown alone in a landscape frame is fitted so nothing important is cut off.
            return height > width && frameWidth > frameHeight ? fit : fill
        }

        var rotatedWidth: Double { rotation == 90 || rotation == 270 ? sourceHeight : sourceWidth }
        var rotatedHeight: Double { rotation == 90 || rotation == 270 ? sourceWidth : sourceHeight }

        var photoRect: CGRect {
            CGRect(x: drawX, y: drawY, width: max(1, rotatedWidth * scale), height: max(1, rotatedHeight * scale))
        }

        func targetPoint(fromNormalized point: CGPoint) -> CGPoint {
            let x = point.x * sourceWidth, y = point.y * sourceHeight
            let rotated: (x: Double, y: Double)
            switch rotation {
            case 90: rotated = (sourceHeight - y, x)
            case 180: rotated = (sourceWidth - x, sourceHeight - y)
            case 270: rotated = (y, sourceWidth - x)
            default: rotated = (x, y)
            }
            return CGPoint(x: drawX + rotated.x * scale, y: drawY + rotated.y * scale)
        }

        func normalizedPoint(fromTarget point: CGPoint) -> CGPoint {
            let rx = (point.x - drawX) / scale, ry = (point.y - drawY) / scale
            let source: (x: Double, y: Double)
            switch rotation {
            case 90: source = (ry, sourceHeight - rx)
            case 180: source = (sourceWidth - rx, sourceHeight - ry)
            case 270: source = (sourceWidth - ry, rx)
            default: source = (rx, ry)
            }
            return CGPoint(x: min(max(source.x / sourceWidth, 0), 1), y: min(max(source.y / sourceHeight, 0), 1))
        }

        func strokeWidth(_ stroke: RedactStroke) -> Double {
            stroke.brushSizeNormalized * min(sourceWidth, sourceHeight) * scale
        }

        func brushSizeNormalized(forTargetWidth width: Double) -> Double {
            width / (min(sourceWidth, sourceHeight) * scale)
        }
    }

    /// Pixel size with EXIF orientation applied.
    static func pixelSize(of url: URL) -> CGSize? {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let width = properties[kCGImagePropertyPixelWidth] as? Int,
              let height = properties[kCGImagePropertyPixelHeight] as? Int else { return nil }
        let orientation = properties[kCGImagePropertyOrientation] as? Int ?? 1
        return orientation >= 5 ? CGSize(width: height, height: width) : CGSize(width: width, height: height)
    }

    static func isPortrait(_ url: URL) -> Bool {
        guard let size = pixelSize(of: url) else { return false }
        return size.height > size.width
    }

    /// Decodes a photo upright (EXIF orientation applied), optionally downsampled.
    static func loadImage(_ url: URL, maxPixelSize: Int? = nil) throws -> CGImage {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil), CGImageSourceGetCount(source) > 0 else {
            throw EngineError("Can't read \(url.lastPathComponent).")
        }
        let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any]
        let width = properties?[kCGImagePropertyPixelWidth] as? Int ?? 0
        let height = properties?[kCGImagePropertyPixelHeight] as? Int ?? 0
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceShouldCacheImmediately: true,
            kCGImageSourceThumbnailMaxPixelSize: maxPixelSize ?? max(width, height, 1)
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else {
            throw EngineError("Can't decode \(url.lastPathComponent).")
        }
        return image
    }

    static func makeContext(width: Int, height: Int, fill: CGColor? = nil) -> CGContext {
        let context = CGContext(
            data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.interpolationQuality = .high
        if let fill {
            context.setFillColor(fill)
            context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        }
        return context
    }

    /// Scales the template to cover the 1200×1000 canvas, center-cropped.
    static func renderOverlay(_ url: URL) throws -> CGImage {
        let overlay = try loadImage(url)
        let context = makeContext(width: targetWidth, height: targetHeight)
        let scale = max(Double(targetWidth) / Double(overlay.width), Double(targetHeight) / Double(overlay.height))
        let width = Double(overlay.width) * scale, height = Double(overlay.height) * scale
        context.draw(overlay, in: CGRect(
            x: (Double(targetWidth) - width) / 2, y: (Double(targetHeight) - height) / 2, width: width, height: height))
        return context.makeImage()!
    }

    static func renderPhoto(_ photo: CGImage, width: Int, height: Int, crop: CropSettings) -> CGImage {
        let framing = Framing(crop: crop, sourceWidth: photo.width, sourceHeight: photo.height,
                              targetWidth: width, targetHeight: height)
        let context = makeContext(width: width, height: height, fill: canvasColor)
        // CoreGraphics is y-up; flip once so the geometry below is top-left, like the editor.
        context.translateBy(x: 0, y: CGFloat(height))
        context.scaleBy(x: 1, y: -1)

        let rect = framing.photoRect
        let drawnWidth = Double(photo.width) * framing.scale, drawnHeight = Double(photo.height) * framing.scale
        context.saveGState()
        context.translateBy(x: rect.midX, y: rect.midY)
        context.rotate(by: CGFloat(crop.rotation) * .pi / 180)
        // Images draw upside down in a flipped context; flip back locally.
        context.scaleBy(x: 1, y: -1)
        context.draw(photo, in: CGRect(x: -drawnWidth / 2, y: -drawnHeight / 2, width: drawnWidth, height: drawnHeight))
        context.restoreGState()

        for stroke in crop.strokes where !stroke.points.isEmpty {
            let points = stroke.points.map(framing.targetPoint)
            context.setStrokeColor(CGColor(srgbRed: CGFloat(stroke.red) / 255, green: CGFloat(stroke.green) / 255,
                                           blue: CGFloat(stroke.blue) / 255, alpha: 1))
            context.setLineWidth(framing.strokeWidth(stroke))
            context.setLineCap(.round)
            context.setLineJoin(.round)
            context.beginPath()
            context.move(to: points[0])
            for point in points.dropFirst() { context.addLine(to: point) }
            if points.count == 1 { context.addLine(to: points[0]) }
            context.strokePath()
        }
        return context.makeImage()!
    }

    static func compose(_ item: BatchItem, overlay: CGImage) throws -> CGImage {
        let full = CGRect(x: 0, y: 0, width: targetWidth, height: targetHeight)
        let context = makeContext(width: targetWidth, height: targetHeight, fill: canvasColor)
        switch item {
        case let .landscape(url, crop), let .solo(url, crop):
            context.draw(renderPhoto(try loadImage(url), width: targetWidth, height: targetHeight, crop: crop), in: full)
        case let .pair(leftURL, rightURL, leftCrop, rightCrop):
            let leftPhoto = try loadImage(leftURL)
            let left = renderPhoto(leftPhoto, width: halfWidth, height: targetHeight, crop: leftCrop)
            let right = leftURL == rightURL && leftCrop == rightCrop
                ? left
                : renderPhoto(leftURL == rightURL ? leftPhoto : try loadImage(rightURL),
                              width: halfWidth, height: targetHeight, crop: rightCrop)
            context.draw(left, in: CGRect(x: 0, y: 0, width: halfWidth, height: targetHeight))
            context.draw(right, in: CGRect(x: halfWidth, y: 0, width: halfWidth, height: targetHeight))
        case .video:
            throw EngineError("Videos are branded by the video engine.")
        }
        context.draw(overlay, in: full)
        return context.makeImage()!
    }

    static func jpegData(_ image: CGImage, quality: Double = 0.9) throws -> Data {
        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(data, UTType.jpeg.identifier as CFString, 1, nil) else {
            throw EngineError("JPEG encoding is unavailable.")
        }
        CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: quality] as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { throw EngineError("JPEG encoding failed.") }
        return data as Data
    }

    /// Mirrors the Windows planner: explicit pairs first, then portraits two by two, an odd
    /// portrait matched with the last landscape (or itself), remaining landscapes alone.
    /// Output keeps the original selection order.
    static func planBatch(_ items: [StagedItem], mode: LayoutMode) -> [BatchItem] {
        var planned: [(order: Int, item: BatchItem)] = []
        var candidates: [(item: StagedItem, isPortrait: Bool, index: Int)] = []

        for (index, item) in items.enumerated() {
            if item.kind == .video {
                planned.append((index, .video(item.url)))
            } else if mode == .separate || item.isSolo {
                planned.append((index, .solo(item.url, item.crop)))
            } else {
                candidates.append((item, isPortrait(item.url), index))
            }
        }

        var paired = Set<Int>()
        for (position, left) in candidates.enumerated() {
            guard !paired.contains(left.index), let partner = left.item.partner else { continue }
            for right in candidates[(position + 1)...]
            where !paired.contains(right.index) && right.item.url == partner && right.item.partner == left.item.url {
                planned.append((left.index, .pair(left.item.url, right.item.url, left.item.crop, right.item.crop)))
                paired.insert(left.index)
                paired.insert(right.index)
                break
            }
        }

        let free = candidates.filter { !paired.contains($0.index) }
        let portraits = free.filter { $0.isPortrait }
        var landscapes = free.filter { !$0.isPortrait }

        var next = 0
        while next + 1 < portraits.count {
            let left = portraits[next], right = portraits[next + 1]
            planned.append((left.index, .pair(left.item.url, right.item.url, left.item.crop, right.item.crop)))
            next += 2
        }
        if next < portraits.count {
            let lone = portraits[next]
            if let match = landscapes.popLast() {
                planned.append((min(lone.index, match.index),
                                .pair(lone.item.url, match.item.url, lone.item.crop, match.item.crop)))
            } else {
                planned.append((lone.index, .pair(lone.item.url, lone.item.url, lone.item.crop, lone.item.crop)))
            }
        }
        for landscape in landscapes {
            planned.append((landscape.index, .landscape(landscape.item.url, landscape.item.crop)))
        }
        return planned.sorted { $0.order < $1.order }.map { $0.item }
    }
}
