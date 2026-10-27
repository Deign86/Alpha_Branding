import AppKit
import AVFoundation
@preconcurrency import CoreImage

enum VideoEngine {
    static let extensions: Set<String> = ["mp4", "mov", "m4v"]

    static func durationText(_ seconds: Double) -> String {
        guard seconds.isFinite, seconds > 0 else { return "" }
        let total = Int(seconds.rounded())
        let hours = total / 3600, minutes = (total % 3600) / 60, secs = total % 60
        return hours > 0
            ? String(format: "%d:%02d:%02d", hours, minutes, secs)
            : String(format: "%d:%02d", minutes, secs)
    }

    static func metadata(_ url: URL) async -> (thumbnail: NSImage?, duration: String) {
        let asset = AVURLAsset(url: url)
        let seconds = (try? await asset.load(.duration))?.seconds ?? 0
        let generator = AVAssetImageGenerator(asset: asset)
        generator.appliesPreferredTrackTransform = true
        generator.maximumSize = CGSize(width: 540, height: 540)
        let time = CMTime(seconds: min(1, seconds / 2), preferredTimescale: 600)
        let image = try? await generator.image(at: time).image
        return (image.map { NSImage(cgImage: $0, size: .zero) }, durationText(seconds))
    }

    /// Renders the clip fitted and centered on a black 1200×1000 canvas with the template on top.
    /// Source audio is kept.
    static func brand(_ url: URL, overlay: CGImage, to output: URL,
                      progress: @escaping @Sendable (Double) -> Void) async throws {
        let asset = AVURLAsset(url: url)
        guard try await !asset.loadTracks(withMediaType: .video).isEmpty else {
            throw EngineError("\(url.lastPathComponent) has no video track.")
        }

        let frame = CGRect(x: 0, y: 0, width: ImageEngine.targetWidth, height: ImageEngine.targetHeight)
        let overlayImage = CIImage(cgImage: overlay)
        let background = CIImage(color: .black).cropped(to: frame)
        let composition = try await AVMutableVideoComposition.videoComposition(with: asset) { request in
            let source = request.sourceImage
            let extent = source.extent
            let scale = min(frame.width / extent.width, frame.height / extent.height)
            let width = (extent.width * scale).rounded(), height = (extent.height * scale).rounded()
            let placed = source
                .transformed(by: CGAffineTransform(translationX: -extent.minX, y: -extent.minY))
                .transformed(by: CGAffineTransform(scaleX: width / extent.width, y: height / extent.height))
                .transformed(by: CGAffineTransform(translationX: ((frame.width - width) / 2).rounded(),
                                                   y: ((frame.height - height) / 2).rounded()))
            request.finish(with: overlayImage.composited(over: placed.composited(over: background)).cropped(to: frame),
                           context: nil)
        }
        composition.renderSize = frame.size

        guard let export = AVAssetExportSession(asset: asset, presetName: AVAssetExportPresetHighestQuality) else {
            throw EngineError("Can't export \(url.lastPathComponent).")
        }
        export.videoComposition = composition
        export.outputURL = output
        export.outputFileType = .mp4
        export.shouldOptimizeForNetworkUse = true

        let poller = Task {
            while !Task.isCancelled {
                progress(Double(export.progress))
                try? await Task.sleep(nanoseconds: 250_000_000)
            }
        }
        await export.export()
        poller.cancel()
        guard export.status == .completed else {
            throw export.error ?? EngineError("Video export failed for \(url.lastPathComponent).")
        }
    }
}
