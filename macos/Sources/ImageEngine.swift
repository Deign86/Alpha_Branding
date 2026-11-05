import AppKit
import CoreImage
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
    /// Blurs in sRGB values, like ImageMagick on Windows, rather than in linear light.
    private static let effectContext = CIContext(options: [.workingColorSpace: CGColorSpace(name: CGColorSpace.sRGB)!])

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
            applyRedaction(stroke, framing: framing, to: context, width: width, height: height)
        }
        return context.makeImage()!
    }

    /// Pixelate cell size, in output pixels, for a mark of the given brush diameter. Strength follows the
    /// brush, so a mark sized to cover a line of text leaves at most two cells per letter height. Same as Windows.
    static func redactBlockSize(_ diameter: Double) -> Int { max(4, Int((diameter / 2).rounded())) }

    /// Gaussian blur sigma, in output pixels, for a mark of the given brush diameter.
    static func redactBlurSigma(_ diameter: Double) -> Double { max(2, diameter / 3) }

    /// Replaces the pixels under one mark with a pixelated or blurred copy of themselves, using the same
    /// working area and cell grid as the Windows renderer. `context` is flipped to top-left coordinates.
    static func applyRedaction(_ stroke: RedactStroke, framing: Framing, to context: CGContext, width: Int, height: Int) {
        let diameter = framing.strokeWidth(stroke)
        let points = stroke.points.map(framing.targetPoint)
        let block = redactBlockSize(diameter), sigma = redactBlurSigma(diameter)
        // Blur reads pixels around the mark, so the working area includes the kernel's reach.
        let reach = diameter / 2 + (stroke.effect == .blur ? (3 * sigma).rounded(.up) : 0)
        let xs = points.map { Double($0.x) }, ys = points.map { Double($0.y) }
        var left = Int((xs.min()! - reach).rounded(.down)), top = Int((ys.min()! - reach).rounded(.down))
        var right = Int((xs.max()! + reach).rounded(.up)), bottom = Int((ys.max()! + reach).rounded(.up))
        if stroke.effect == .pixelate {
            // Cells sit on a grid anchored at the frame's top-left corner.
            left = Int((Double(left) / Double(block)).rounded(.down)) * block
            top = Int((Double(top) / Double(block)).rounded(.down)) * block
            right = Int((Double(right) / Double(block)).rounded(.up)) * block
            bottom = Int((Double(bottom) / Double(block)).rounded(.up)) * block
        }
        left = max(0, left)
        top = max(0, top)
        right = min(width, right)
        bottom = min(height, bottom)
        guard right > left, bottom > top,
              let area = context.makeImage()?.cropping(to: CGRect(x: left, y: top, width: right - left, height: bottom - top))
        else { return }

        let replacement: CGImage?
        switch stroke.effect {
        case .pixelate:
            replacement = averageCells(of: area, columns: (area.width + block - 1) / block, rows: (area.height + block - 1) / block)
        case .blur:
            let input = CIImage(cgImage: area)
            replacement = effectContext.createCGImage(
                input.clampedToExtent().applyingGaussianBlur(sigma: sigma).cropped(to: input.extent), from: input.extent)
        }
        guard let replacement else { return }

        let path = CGMutablePath()
        path.addLines(between: points.count == 1 ? [points[0], points[0]] : points)
        context.saveGState()
        context.addPath(path.copy(strokingWithWidth: CGFloat(diameter), lineCap: .round, lineJoin: .round, miterLimit: 10))
        context.clip()
        context.interpolationQuality = stroke.effect == .pixelate ? .none : .high
        // Images draw upside down in the flipped context; flip back locally.
        context.translateBy(x: CGFloat(left), y: CGFloat(bottom))
        context.scaleBy(x: 1, y: -1)
        context.draw(replacement, in: CGRect(x: 0, y: 0, width: area.width, height: area.height))
        context.restoreGState()
    }

    /// Shrinks `image` to one pixel per cell, each the mean of the pixels it covers (a box filter, like
    /// ImageMagick's Scale on Windows).
    static func averageCells(of image: CGImage, columns: Int, rows: Int) -> CGImage? {
        let pixels = makeContext(width: image.width, height: image.height)
        pixels.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        let cells = makeContext(width: columns, height: rows)
        guard let source = pixels.data?.assumingMemoryBound(to: UInt8.self),
              let target = cells.data?.assumingMemoryBound(to: UInt8.self) else { return nil }
        for row in 0..<rows {
            for column in 0..<columns {
                var red = 0, green = 0, blue = 0, alpha = 0, count = 0
                for y in row * image.height / rows..<(row + 1) * image.height / rows {
                    for x in column * image.width / columns..<(column + 1) * image.width / columns {
                        let offset = y * pixels.bytesPerRow + x * 4
                        red += Int(source[offset])
                        green += Int(source[offset + 1])
                        blue += Int(source[offset + 2])
                        alpha += Int(source[offset + 3])
                        count += 1
                    }
                }
                guard count > 0 else { continue }
                let offset = row * cells.bytesPerRow + column * 4
                target[offset] = UInt8((red + count / 2) / count)
                target[offset + 1] = UInt8((green + count / 2) / count)
                target[offset + 2] = UInt8((blue + count / 2) / count)
                target[offset + 3] = UInt8((alpha + count / 2) / count)
            }
        }
        return cells.makeImage()
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
