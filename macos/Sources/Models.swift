import AppKit

enum MediaKind { case image, video }

enum LayoutMode { case combine, separate }

struct RedactStroke: Equatable {
    /// Points normalized to 0...1 of the oriented source photo, so marks survive any framing.
    var points: [CGPoint] = []
    var brushSizeNormalized: Double = 0.02
    var red: UInt8 = 0
    var green: UInt8 = 0
    var blue: UInt8 = 0

    mutating func add(_ point: CGPoint) {
        points.append(CGPoint(x: min(max(point.x, 0), 1), y: min(max(point.y, 0), 1)))
    }
}

struct CropSettings: Equatable {
    var panX = 0.0
    var panY = 0.0
    private var storedZoom = 1.0
    private var storedRotation = 0
    var strokes: [RedactStroke] = []

    var zoom: Double {
        get { storedZoom }
        set { storedZoom = min(max(newValue, 0.2), 5.0) }
    }

    var rotation: Int {
        get { storedRotation }
        set { storedRotation = ((newValue % 360) + 360) % 360 }
    }

    var isDefault: Bool {
        abs(panX) < 0.001 && abs(panY) < 0.001 && abs(zoom - 1) < 0.001 && rotation == 0
    }

    var hasRedactions: Bool { !strokes.isEmpty }
    var zoomText: String { "\(Int((zoom * 100).rounded()))%" }

    /// Resets framing only. Pen marks stay: they hide private details and are stored in
    /// source-image coordinates, so they remain valid under any framing.
    mutating func resetFraming() {
        panX = 0
        panY = 0
        zoom = 1
        rotation = 0
    }
}

struct StagedItem: Identifiable, Equatable {
    let url: URL
    let kind: MediaKind
    let sizeText: String
    var durationText = ""
    var crop = CropSettings()
    var isSolo = false
    var partner: URL?

    var id: URL { url }
    var fileName: String { url.lastPathComponent }
    var canPair: Bool { !isSolo && kind == .image }
    var hasEdits: Bool { !crop.isDefault || crop.hasRedactions || partner != nil || isSolo }
}

enum BatchItem: Equatable {
    case landscape(URL, CropSettings)
    case pair(URL, URL, CropSettings, CropSettings)
    case solo(URL, CropSettings)
    case video(URL)

    var kind: MediaKind {
        if case .video = self { return .video }
        return .image
    }

    var description: String {
        switch self {
        case let .pair(left, right, _, _):
            return left == right
                ? "Side-by-side: \(left.lastPathComponent)"
                : "Pair: \(left.lastPathComponent) + \(right.lastPathComponent)"
        case let .landscape(url, _): return url.lastPathComponent
        case let .solo(url, _): return "Solo: \(url.lastPathComponent)"
        case let .video(url): return "Video: \(url.lastPathComponent)"
        }
    }

    var leftURL: URL {
        switch self {
        case let .landscape(url, _), let .solo(url, _), let .video(url), let .pair(url, _, _, _): return url
        }
    }

    var rightURL: URL? {
        if case let .pair(_, right, _, _) = self { return right }
        return nil
    }

    var leftCrop: CropSettings? {
        switch self {
        case let .landscape(_, crop), let .solo(_, crop), let .pair(_, _, crop, _): return crop
        case .video: return nil
        }
    }

    var rightCrop: CropSettings? {
        if case let .pair(_, _, _, crop) = self { return crop }
        return nil
    }

    func withCrops(left: CropSettings, right: CropSettings?) -> BatchItem {
        switch self {
        case let .landscape(url, _): return .landscape(url, left)
        case let .solo(url, _): return .solo(url, left)
        case let .pair(l, r, _, oldRight): return .pair(l, r, left, right ?? oldRight)
        case .video: return self
        }
    }
}

struct BrandedResult: Identifiable {
    let id = UUID()
    var fileName: String
    let kind: MediaKind
    var jpegData = Data()
    var preview: NSImage
    var videoURL: URL?
    var durationText = ""
    let sequenceIndex: Int
    let batchSize: Int
    var source: BatchItem
    var overlayURL: URL

    var canEdit: Bool { kind == .image }
    var hasCustomCrop: Bool {
        !(source.leftCrop?.isDefault ?? true) || !(source.rightCrop?.isDefault ?? true)
    }
}

struct CropRequest: Identifiable {
    enum Target {
        case staged(URL)
        case result(UUID)
    }

    let id = UUID()
    let target: Target
    let title: String
    let leftURL: URL
    let rightURL: URL?
    let left: CropSettings
    let right: CropSettings
    let overlayURL: URL?
}
