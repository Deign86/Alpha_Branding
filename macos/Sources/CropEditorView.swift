import AppKit
import SwiftUI

struct CropEditorView: View {
    let request: CropRequest
    let onSave: (CropSettings, CropSettings) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var left: CropSettings
    @State private var right: CropSettings
    @State private var isLeftSlot = true
    @State private var penMode = false
    @State private var penSize = 12.0
    @State private var leftImage: CGImage?
    @State private var rightImage: CGImage?
    @State private var overlay: CGImage?
    @State private var activeStroke: RedactStroke?
    @State private var dragStartPan: CGPoint?
    @State private var pinchStartZoom: Double?
    @State private var clearedStrokes: (isLeft: Bool, strokes: [RedactStroke])?
    @State private var isHovering = false
    @State private var eventMonitor: Any?

    init(request: CropRequest, onSave: @escaping (CropSettings, CropSettings) -> Void) {
        self.request = request
        self.onSave = onSave
        _left = State(initialValue: request.left)
        _right = State(initialValue: request.right)
    }

    private var isPair: Bool { request.rightURL != nil }
    private var targetWidth: Int { isPair ? ImageEngine.halfWidth : ImageEngine.targetWidth }
    private var targetHeight: Int { ImageEngine.targetHeight }
    private var crop: CropSettings { isLeftSlot ? left : right }
    private var image: CGImage? { isLeftSlot ? leftImage : rightImage }

    private var framing: ImageEngine.Framing? {
        image.map {
            ImageEngine.Framing(crop: crop, sourceWidth: $0.width, sourceHeight: $0.height,
                                targetWidth: targetWidth, targetHeight: targetHeight)
        }
    }

    private func mutate(_ change: (inout CropSettings) -> Void) {
        if isLeftSlot { change(&left) } else { change(&right) }
    }

    private var zoomBinding: Binding<Double> {
        Binding(get: { crop.zoom }, set: { value in mutate { $0.zoom = value } })
    }

    var body: some View {
        VStack(spacing: 12) {
            HStack(spacing: 12) {
                Image(nsImage: Theme.logo).resizable().frame(width: 30, height: 30)
                VStack(alignment: .leading, spacing: 3) {
                    (Text("CROP & ALIGNMENT EDITOR").foregroundColor(Theme.goldLight) + Text(" — \(request.title)"))
                        .font(.system(size: 14, weight: .bold))
                        .lineLimit(1)
                    Text("Drag the photo to position • Scroll or pinch to zoom • The frame overlay shows the final output")
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.textMuted)
                }
                Spacer()
                if isPair {
                    Button("LEFT PHOTO") { switchSlot(toLeft: true) }
                        .studioButton(selected: isLeftSlot)
                        .help(request.leftURL.lastPathComponent)
                    Button("RIGHT PHOTO") { switchSlot(toLeft: false) }
                        .studioButton(selected: !isLeftSlot)
                        .help(request.rightURL?.lastPathComponent ?? "")
                }
                Badge("\(targetWidth) × \(targetHeight) px")
            }
            .panel(padding: 14)

            GeometryReader { geometry in
                canvas(size: geometry.size)
            }
            .frame(minHeight: 380)
            .panel(padding: 10)

            HStack(spacing: 10) {
                FieldLabel("ZOOM:")
                Button("–") { mutate { $0.zoom = min(max($0.zoom - 0.1, 0.2), 4) } }.studioButton()
                Slider(value: zoomBinding, in: 0.2...4).frame(width: 160).tint(Theme.gold)
                Button("+") { mutate { $0.zoom = min(max($0.zoom + 0.1, 0.2), 4) } }.studioButton()
                Badge(crop.zoomText).frame(width: 56)
                Spacer()
                Button("FIT TO FRAME") { fit(fill: false) }.studioButton()
                Button("FILL FRAME") { fit(fill: true) }.studioButton()
                Button("ROTATE 90°") { mutate { $0.rotation += 90 } }.studioButton()
                Button("RESET CROP") { mutate { $0.resetFraming() } }.studioButton()
                Spacer()
                (Text("PAN: ").foregroundColor(Theme.textMuted)
                    + Text("X: \(Int(crop.panX.rounded())) px  Y: \(Int(crop.panY.rounded())) px").foregroundColor(Theme.goldLight))
                    .font(.system(size: 11, weight: .bold).monospacedDigit())
            }
            .panel(padding: 12)

            HStack(spacing: 10) {
                FieldLabel("MODE:")
                Button("PAN") { finishStroke(); penMode = false }.studioButton(selected: !penMode)
                Button("PEN") { penMode = true }.studioButton(selected: penMode)
                    .help("Paint over private details such as faces, plates or house numbers")
                FieldLabel("PEN SIZE").padding(.leading, 8)
                Slider(value: $penSize, in: 2...60).frame(width: 120).tint(Theme.gold)
                Button("UNDO MARK") { undoStroke() }.studioButton().disabled(crop.strokes.isEmpty && clearedStrokes?.isLeft != isLeftSlot)
                Button("CLEAR MARKS") { clearStrokes() }.studioButton().disabled(crop.strokes.isEmpty)
                Spacer()
                Text(penMode ? "Pen marks are burned into the export" : "Arrow keys nudge • Shift for bigger steps")
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.textMuted)
            }
            .panel(padding: 12)

            HStack {
                Text("Esc cancels • Return saves • ⌘Z undoes the last pen mark")
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.textMuted)
                Spacer()
                Button("CANCEL") { dismiss() }
                    .studioButton()
                    .keyboardShortcut(.cancelAction)
                Button("SAVE CHANGES") {
                    finishStroke()
                    onSave(left, right)
                    dismiss()
                }
                .studioButton(.primary)
                .keyboardShortcut(.defaultAction)
            }
            .panel(padding: 14)
        }
        .padding(20)
        .frame(minWidth: 940, idealWidth: 1040, minHeight: 660, idealHeight: 860)
        .background(Theme.background)
        .preferredColorScheme(.dark)
        .task { await loadImages() }
        .onAppear { installEventMonitor() }
        .onDisappear { if let eventMonitor { NSEvent.removeMonitor(eventMonitor) } }
    }

    private func canvas(size: CGSize) -> some View {
        let scale = min(size.width / CGFloat(targetWidth), size.height / CGFloat(targetHeight))
        let frame = CGRect(x: (size.width - CGFloat(targetWidth) * scale) / 2,
                           y: (size.height - CGFloat(targetHeight) * scale) / 2,
                           width: CGFloat(targetWidth) * scale, height: CGFloat(targetHeight) * scale)
        func toTarget(_ point: CGPoint) -> CGPoint {
            CGPoint(x: (point.x - frame.minX) / scale, y: (point.y - frame.minY) / scale)
        }

        return Canvas { context, _ in
            context.clip(to: Path(frame))
            context.fill(Path(frame), with: .color(Color(cgColor: ImageEngine.canvasColor)))
            context.translateBy(x: frame.minX, y: frame.minY)
            context.scaleBy(x: scale, y: scale)

            if let image, let framing {
                var photo = context
                let rect = framing.photoRect
                let width = CGFloat(image.width) * framing.scale, height = CGFloat(image.height) * framing.scale
                photo.translateBy(x: rect.midX, y: rect.midY)
                photo.rotate(by: .degrees(Double(crop.rotation)))
                photo.draw(Image(decorative: image, scale: 1),
                           in: CGRect(x: -width / 2, y: -height / 2, width: width, height: height))

                for stroke in crop.strokes + (activeStroke.map { [$0] } ?? []) where !stroke.points.isEmpty {
                    let points = stroke.points.map(framing.targetPoint)
                    var path = Path()
                    path.move(to: points[0])
                    for point in points.dropFirst() { path.addLine(to: point) }
                    if points.count == 1 { path.addLine(to: points[0]) }
                    context.stroke(path,
                                   with: .color(Color(.sRGB, red: Double(stroke.red) / 255, green: Double(stroke.green) / 255,
                                                      blue: Double(stroke.blue) / 255)),
                                   style: StrokeStyle(lineWidth: framing.strokeWidth(stroke), lineCap: .round, lineJoin: .round))
                }
            }

            var grid = Path()
            for step in 1..<3 {
                let x = CGFloat(targetWidth) * CGFloat(step) / 3, y = CGFloat(targetHeight) * CGFloat(step) / 3
                grid.move(to: CGPoint(x: x, y: 0))
                grid.addLine(to: CGPoint(x: x, y: CGFloat(targetHeight)))
                grid.move(to: CGPoint(x: 0, y: y))
                grid.addLine(to: CGPoint(x: CGFloat(targetWidth), y: y))
            }
            context.stroke(grid, with: .color(.white.opacity(0.12)), lineWidth: 1 / scale)

            if let overlay {
                // A pair slot shows its half of the full frame, exactly as it lands in the export.
                let offset = isPair && !isLeftSlot ? -CGFloat(ImageEngine.halfWidth) : 0
                context.draw(Image(decorative: overlay, scale: 1),
                             in: CGRect(x: offset, y: 0, width: CGFloat(ImageEngine.targetWidth),
                                        height: CGFloat(ImageEngine.targetHeight)))
            }
        }
        .overlay { if image == nil { ProgressView() } }
        .contentShape(Rectangle())
        .onContinuousHover { phase in
            if case .active = phase {
                isHovering = true
                (penMode ? NSCursor.crosshair : NSCursor.openHand).set()
            } else {
                isHovering = false
                NSCursor.arrow.set()
            }
        }
        .gesture(
            DragGesture(minimumDistance: 0)
                .onChanged { value in
                    let start = toTarget(value.startLocation), current = toTarget(value.location)
                    if penMode {
                        addPenPoint(current, startingAt: start)
                    } else {
                        if dragStartPan == nil { dragStartPan = CGPoint(x: crop.panX, y: crop.panY) }
                        let origin = dragStartPan!
                        mutate {
                            $0.panX = origin.x + current.x - start.x
                            $0.panY = origin.y + current.y - start.y
                        }
                    }
                }
                .onEnded { _ in
                    finishStroke()
                    dragStartPan = nil
                })
        .simultaneousGesture(
            MagnificationGesture()
                .onChanged { value in
                    if pinchStartZoom == nil { pinchStartZoom = crop.zoom }
                    let base = pinchStartZoom!
                    mutate { $0.zoom = min(max(base * value, 0.2), 4) }
                }
                .onEnded { _ in pinchStartZoom = nil })
    }

    private func loadImages() async {
        let leftURL = request.leftURL, rightURL = request.rightURL, overlayURL = request.overlayURL
        // A downsampled copy keeps dragging smooth; framing math is resolution independent.
        let loaded = await Task.detached {
            (try? ImageEngine.loadImage(leftURL, maxPixelSize: 2400),
             rightURL.flatMap { try? ImageEngine.loadImage($0, maxPixelSize: 2400) },
             overlayURL.flatMap { try? ImageEngine.renderOverlay($0) })
        }.value
        leftImage = loaded.0
        rightImage = loaded.1
        overlay = loaded.2
    }

    private func switchSlot(toLeft: Bool) {
        finishStroke()
        isLeftSlot = toLeft
    }

    private func fit(fill: Bool) {
        guard let image else { return }
        let quarterTurn = crop.rotation == 90 || crop.rotation == 270
        let width = Double(quarterTurn ? image.height : image.width)
        let height = Double(quarterTurn ? image.width : image.height)
        let frameWidth = Double(targetWidth), frameHeight = Double(targetHeight)
        let base = ImageEngine.Framing.baseScale(width, height, frameWidth, frameHeight)
        let wanted = fill
            ? max(frameWidth / width, frameHeight / height)
            : min(frameWidth / width, frameHeight / height)
        mutate {
            $0.zoom = wanted / base
            $0.panX = 0
            $0.panY = 0
        }
    }

    private func addPenPoint(_ point: CGPoint, startingAt start: CGPoint) {
        guard let framing else { return }
        if activeStroke == nil {
            activeStroke = RedactStroke(brushSizeNormalized: framing.brushSizeNormalized(forTargetWidth: penSize))
            activeStroke?.add(framing.normalizedPoint(fromTarget: start))
        }
        let normalized = framing.normalizedPoint(fromTarget: point)
        if activeStroke?.points.last != normalized { activeStroke?.add(normalized) }
    }

    private func finishStroke() {
        guard let stroke = activeStroke else { return }
        activeStroke = nil
        guard !stroke.points.isEmpty else { return }
        mutate { $0.strokes.append(stroke) }
        clearedStrokes = nil
    }

    private func undoStroke() {
        finishStroke()
        if crop.strokes.isEmpty {
            // One level of undo for CLEAR MARKS, restored only onto the slot it was cleared from.
            if let cleared = clearedStrokes, cleared.isLeft == isLeftSlot {
                mutate { $0.strokes = cleared.strokes }
                clearedStrokes = nil
            }
            return
        }
        mutate { _ = $0.strokes.removeLast() }
    }

    private func clearStrokes() {
        finishStroke()
        guard !crop.strokes.isEmpty else { return }
        clearedStrokes = (isLeftSlot, crop.strokes)
        mutate { $0.strokes.removeAll() }
    }

    /// Mouse-wheel zoom over the canvas, arrow-key nudging, ⌘Z and Esc-to-leave-pen-mode.
    private func installEventMonitor() {
        eventMonitor = NSEvent.addLocalMonitorForEvents(matching: [.scrollWheel, .keyDown]) { event in
            if event.type == .scrollWheel {
                guard isHovering, event.scrollingDeltaY != 0 else { return event }
                let delta = event.hasPreciseScrollingDeltas
                    ? Double(event.scrollingDeltaY) * 0.005
                    : (event.scrollingDeltaY > 0 ? 0.08 : -0.08)
                mutate { $0.zoom = min(max($0.zoom + delta, 0.2), 4) }
                return nil
            }
            let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            if flags == .command && event.charactersIgnoringModifiers == "z" {
                undoStroke()
                return nil
            }
            if event.keyCode == 53 && penMode {
                penMode = false
                return nil
            }
            let step = flags.contains(.shift) ? 50.0 : 10.0
            switch event.keyCode {
            case 123: mutate { $0.panX -= step }
            case 124: mutate { $0.panX += step }
            case 125: mutate { $0.panY += step }
            case 126: mutate { $0.panY -= step }
            default: return event
            }
            return nil
        }
    }
}
