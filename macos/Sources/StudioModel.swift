import AppKit
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class StudioModel: ObservableObject {
    static let mediaTypes: [UTType] = (ImageEngine.photoExtensions.union(VideoEngine.extensions))
        .compactMap { UTType(filenameExtension: $0) }

    static let placeholder: NSImage = {
        let image = NSImage(size: NSSize(width: 320, height: 240))
        image.lockFocus()
        NSColor(srgbRed: 24 / 255, green: 24 / 255, blue: 24 / 255, alpha: 1).setFill()
        NSRect(x: 0, y: 0, width: 320, height: 240).fill()
        image.unlockFocus()
        return image
    }()

    @Published private(set) var staged: [StagedItem] = []
    @Published private(set) var results: [BrandedResult] = []
    @Published private(set) var thumbnails: [URL: NSImage] = [:]
    @Published var prefix = FileNameGenerator.defaultPrefix { didSet { prefixChanged() } }
    @Published var layoutMode: LayoutMode = .combine
    @Published private(set) var templates: [BrandingTemplate]
    @Published var selectedTemplateID: String { didSet { templateChanged() } }
    @Published private(set) var isBusy = false
    @Published private(set) var status = "Select property photos and videos to begin."
    @Published private(set) var progress = 0.0
    @Published private(set) var hasUnsavedEdits = false
    @Published var isDragOver = false

    private let templateStore: TemplateStore
    private var brandedFiles: [URL] = []
    private var exportedSinceChange = false

    init() {
        let store = TemplateStore()
        templateStore = store
        templates = store.templates
        selectedTemplateID = store.activeID
    }

    // MARK: Derived state

    var activeTemplate: BrandingTemplate? { templates.first { $0.id == selectedTemplateID } }
    var hasResults: Bool { !results.isEmpty }
    var canApply: Bool { !isBusy && !staged.isEmpty }
    var canExport: Bool { !isBusy && !results.isEmpty }
    var hasStagedEdits: Bool { results.isEmpty && staged.contains(where: \.hasEdits) }
    var needsSavePrompt: Bool { !results.isEmpty && (hasUnsavedEdits || !exportedSinceChange) }
    var zipName: String { "\(FileNameGenerator.folderName(prefix))_Export.zip" }

    var patternPreview: String {
        FileNameGenerator.generate(prefix, index: 0, total: results.isEmpty ? 10 : results.count)
    }

    var selectionSummary: String {
        let videos = staged.filter { $0.kind == .video }.count
        let photos = staged.count - videos
        if staged.isEmpty { return "No media selected" }
        if videos == 0 { return "\(photos) photo(s) selected" }
        if photos == 0 { return "\(videos) video(s) selected" }
        return "\(staged.count) item(s) selected (\(photos) photos, \(videos) videos)"
    }

    var applyHint: String? {
        guard !staged.isEmpty, !results.isEmpty else { return nil }
        if hasUnsavedEdits { return "Unsaved edits in current session." }
        guard staged.map(\.url) != brandedFiles else { return nil }
        return "\(staged.count) item(s) staged — applying branding will start a new session."
    }

    static func isSupported(_ url: URL) -> Bool {
        let ext = url.pathExtension.lowercased()
        return (ImageEngine.photoExtensions.contains(ext) || VideoEngine.extensions.contains(ext))
            && FileManager.default.fileExists(atPath: url.path)
    }

    // MARK: Selecting media

    func selectMedia() {
        guard !isBusy, let urls = Dialogs.openMedia() else { return }
        if hasStagedEdits && !Dialogs.confirm(
            "Discard edits?",
            "Replace the \(staged.count) selected items? Their crop, pen and pairing edits will be lost. Use ADD MORE to keep them.",
            action: "Replace") { return }
        Task { await loadFiles(urls) }
    }

    func addMore() {
        guard !isBusy, let urls = Dialogs.openMedia() else { return }
        addFiles(urls)
    }

    /// Files dropped on the window or opened from Finder. Adding to staged media keeps its edits.
    func open(_ urls: [URL]) {
        guard !isBusy else { return }
        if !staged.isEmpty && results.isEmpty {
            addFiles(urls)
        } else {
            Task { await loadFiles(urls) }
        }
    }

    func loadFiles(_ urls: [URL]) async {
        guard !isBusy else { return }
        let files = unique(urls.filter(Self.isSupported))
        guard !files.isEmpty else {
            status = "No supported photos or videos in that selection."
            return
        }
        guard await confirmEndSession(
            title: "Start a new branding session?",
            unsaved: "You have unsaved edits in the current session. Loading a new batch replaces the current items and discards those edits.",
            active: "Starting a new session replaces the branded items in the current session. Export them first, discard them, or cancel?")
        else { return }
        setStaged(files)
        status = "\(selectionSummary). Click APPLY BRANDING to watermark."
    }

    func addFiles(_ urls: [URL]) {
        guard !isBusy else { return }
        let existing = Set(staged.map(\.url))
        let added = unique(urls.filter(Self.isSupported)).filter { !existing.contains($0) }
        if !added.isEmpty { setStaged(staged.map(\.url) + added) }
        status = added.isEmpty
            ? "No new supported files to add."
            : "Added \(added.count) \(added.count == 1 ? "item" : "items") to the selection."
    }

    func remove(_ url: URL) {
        guard !isBusy, let item = staged.first(where: { $0.url == url }) else { return }
        unpair(url)
        setStaged(staged.map(\.url).filter { $0 != url })
        status = "Removed \(item.fileName) from the selection."
    }

    func clearSelection() {
        guard !isBusy, !staged.isEmpty else { return }
        if hasStagedEdits && !Dialogs.confirm(
            "Discard edits?",
            "Remove all \(staged.count) selected items? Their crop, pen and pairing edits will be lost.",
            action: "Remove All") { return }
        let count = staged.count
        setStaged([])
        status = "Removed \(count) \(count == 1 ? "item" : "items") from the selection."
    }

    func clearSession() async {
        guard !isBusy else { return }
        guard await confirmEndSession(
            title: "Clear all branded items?",
            unsaved: "You have unsaved edits in the current session. Clearing removes every branded item and the selection, and discards those edits.",
            active: "Clearing removes every branded item and the selection. Export them first if you still need them.")
        else { return }
        discardResults()
        setStaged([])
        status = "Cleared the session. Select media to start again."
    }

    private func unique(_ urls: [URL]) -> [URL] {
        var seen = Set<URL>()
        return urls.map(\.standardizedFileURL).filter { seen.insert($0).inserted }
    }

    private func setStaged(_ urls: [URL]) {
        let existing = Dictionary(staged.map { ($0.url, $0) }, uniquingKeysWith: { first, _ in first })
        let present = Set(urls)
        var items = urls.map { existing[$0] ?? Self.makeItem($0) }
        for index in items.indices where items[index].partner.map({ !present.contains($0) }) ?? false {
            items[index].partner = nil
        }
        staged = items
        thumbnails = thumbnails.filter { present.contains($0.key) }
        loadThumbnails(for: items.filter { thumbnails[$0.url] == nil })
    }

    private static func makeItem(_ url: URL) -> StagedItem {
        let bytes = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
        let megabytes = Double(bytes) / 1_048_576
        let size = megabytes >= 1 ? String(format: "%.1f MB", megabytes) : "\(max(1, bytes / 1024)) KB"
        let kind: MediaKind = VideoEngine.extensions.contains(url.pathExtension.lowercased()) ? .video : .image
        return StagedItem(url: url, kind: kind, sizeText: size)
    }

    private func loadThumbnails(for items: [StagedItem]) {
        for item in items {
            let url = item.url
            Task {
                if item.kind == .video {
                    let meta = await VideoEngine.metadata(url)
                    thumbnails[url] = meta.thumbnail ?? Self.placeholder
                    if let index = staged.firstIndex(where: { $0.url == url }) { staged[index].durationText = meta.duration }
                } else {
                    let image = await Task.detached { try? ImageEngine.loadImage(url, maxPixelSize: 540) }.value
                    thumbnails[url] = image.map { NSImage(cgImage: $0, size: .zero) } ?? Self.placeholder
                }
            }
        }
    }

    // MARK: Staged edits

    func pairCandidates(for url: URL) -> [StagedItem] {
        staged.filter { $0.url != url && $0.canPair }
    }

    func pair(_ url: URL, with partnerURL: URL) {
        guard !isBusy, let a = staged.firstIndex(where: { $0.url == url }),
              let b = staged.firstIndex(where: { $0.url == partnerURL }),
              a != b, staged[a].canPair, staged[b].canPair else { return }
        unpair(url)
        unpair(partnerURL)
        staged[a].partner = partnerURL
        staged[b].partner = url
        status = "Paired \(staged[a].fileName) with \(staged[b].fileName)."
    }

    func unpair(_ url: URL) {
        for index in staged.indices where staged[index].url == url || staged[index].partner == url {
            staged[index].partner = nil
        }
    }

    func toggleSolo(_ url: URL) {
        guard !isBusy, let index = staged.firstIndex(where: { $0.url == url }) else { return }
        staged[index].isSolo.toggle()
        if staged[index].isSolo { unpair(url) }
    }

    func resetStagedCrop(_ url: URL) {
        guard let index = staged.firstIndex(where: { $0.url == url }) else { return }
        staged[index].crop.resetFraming()
    }

    func resetAllStagedCrops() {
        for index in staged.indices { staged[index].crop.resetFraming() }
        status = "All staged image crops have been reset to default."
    }

    func cropRequest(forStaged url: URL) -> CropRequest? {
        guard !isBusy, let item = staged.first(where: { $0.url == url }), item.kind == .image else { return nil }
        return CropRequest(target: .staged(url), title: item.fileName, leftURL: url, rightURL: nil,
                           left: item.crop, right: CropSettings(), overlayURL: activeTemplate?.url)
    }

    func cropRequest(forResult id: UUID) -> CropRequest? {
        guard !isBusy, let result = results.first(where: { $0.id == id }), result.canEdit else { return nil }
        return CropRequest(target: .result(id), title: result.fileName,
                           leftURL: result.source.leftURL, rightURL: result.source.rightURL,
                           left: result.source.leftCrop ?? CropSettings(), right: result.source.rightCrop ?? CropSettings(),
                           overlayURL: result.overlayURL)
    }

    func applyCrop(_ request: CropRequest, left: CropSettings, right: CropSettings) {
        switch request.target {
        case let .staged(url):
            if let index = staged.firstIndex(where: { $0.url == url }) { staged[index].crop = left }
        case let .result(id):
            Task { await updateResultCrop(id, left: left, right: request.rightURL == nil ? nil : right) }
        }
    }

    // MARK: Branding

    func apply() async {
        guard canApply else { return }
        guard let template = activeTemplate, FileManager.default.fileExists(atPath: template.filePath) else {
            Dialogs.error("Branding failed", "The branding template file can't be found. Choose or import another template.")
            return
        }
        guard await confirmEndSession(
            title: "Start a new branding session?",
            unsaved: "You have unsaved edits in the current session. Applying branding replaces the current items and discards those edits.",
            active: "Applying branding replaces the branded items in the current session. Export them first, discard them, or cancel?")
        else { return }

        isBusy = true
        results = []
        hasUnsavedEdits = false
        exportedSinceChange = false
        progress = 0
        defer { isBusy = false }

        status = "Analyzing media files…"
        let items = staged, mode = layoutMode, overlayURL = template.url
        let prepared = await Task.detached {
            Result { (ImageEngine.planBatch(items, mode: mode), try ImageEngine.renderOverlay(overlayURL)) }
        }.value
        guard case let .success((plan, overlay)) = prepared else {
            status = "Couldn't read the branding template \(template.name)."
            return
        }

        var skipped: [String] = []
        for (index, item) in plan.enumerated() {
            status = "Processing \(index + 1) of \(plan.count) (\(item.description))…"
            do {
                results.append(try await brand(item, overlay: overlay, overlayURL: overlayURL, index: index, total: plan.count))
            } catch {
                skipped.append("\(item.description) (\(error.localizedDescription))")
            }
            progress = Double(index + 1) / Double(plan.count)
        }

        brandedFiles = staged.map(\.url)
        let noun = results.contains { $0.kind == .video } ? "item(s)" : "image(s)"
        status = skipped.isEmpty
            ? "Completed \(results.count) \(noun)."
            : "Completed \(results.count) \(noun); skipped \(skipped.count): \(skipped.joined(separator: "; ")). Replace or remove those files, then apply branding again."
    }

    private func brand(_ item: BatchItem, overlay: CGImage, overlayURL: URL, index: Int, total: Int) async throws -> BrandedResult {
        let name = FileNameGenerator.generate(prefix, index: index, total: total, kind: item.kind)
        if case let .video(url) = item {
            let output = FileManager.default.temporaryDirectory
                .appendingPathComponent("alpha_video_\(UUID().uuidString).mp4")
            let base = Double(index) / Double(total), portion = 1 / Double(total)
            try await VideoEngine.brand(url, overlay: overlay, to: output) { fraction in
                Task { @MainActor in
                    self.progress = min(1, base + fraction * portion)
                    self.status = "Watermarking video \(index + 1) of \(total) (\(Int(fraction * 100))%)…"
                }
            }
            let meta = await VideoEngine.metadata(output)
            return BrandedResult(fileName: name, kind: .video, preview: meta.thumbnail ?? Self.placeholder,
                                 videoURL: output, durationText: meta.duration,
                                 sequenceIndex: index, batchSize: total, source: item, overlayURL: overlayURL)
        }
        let (data, image) = try await Self.render(item, overlay: overlay)
        return BrandedResult(fileName: name, kind: .image, jpegData: data, preview: NSImage(cgImage: image, size: .zero),
                             sequenceIndex: index, batchSize: total, source: item, overlayURL: overlayURL)
    }

    private nonisolated static func render(_ item: BatchItem, overlay: CGImage) async throws -> (Data, CGImage) {
        try await Task.detached {
            let image = try ImageEngine.compose(item, overlay: overlay)
            return (try ImageEngine.jpegData(image), image)
        }.value
    }

    func updateResultCrop(_ id: UUID, left: CropSettings, right: CropSettings?) async {
        guard !isBusy, let result = results.first(where: { $0.id == id }), result.canEdit else { return }
        isBusy = true
        defer { isBusy = false }
        let source = result.source.withCrops(left: left, right: right)
        let overlayURL = activeTemplate?.url ?? result.overlayURL
        do {
            let overlay = try await Task.detached { try ImageEngine.renderOverlay(overlayURL) }.value
            let (data, image) = try await Self.render(source, overlay: overlay)
            guard let index = results.firstIndex(where: { $0.id == id }) else { return }
            results[index].jpegData = data
            results[index].preview = NSImage(cgImage: image, size: .zero)
            results[index].source = source
            results[index].overlayURL = overlayURL
            markEdited()
            status = "Updated crop for \(results[index].fileName)."
        } catch {
            status = "Failed to update crop: \(error.localizedDescription)"
        }
    }

    func resetResultCrop(_ id: UUID) async {
        guard let result = results.first(where: { $0.id == id }), result.hasCustomCrop else { return }
        var left = result.source.leftCrop ?? CropSettings()
        left.resetFraming()
        var right = result.source.rightCrop
        right?.resetFraming()
        await updateResultCrop(id, left: left, right: right)
    }

    func resetAllResultCrops() async {
        let ids = results.filter { $0.canEdit && $0.hasCustomCrop }.map(\.id)
        for id in ids { await resetResultCrop(id) }
        status = ids.isEmpty
            ? "All branded images already use default framing."
            : "Reset crops on \(ids.count) branded image(s)."
    }

    // MARK: Saving and exporting

    func save(_ id: UUID) async {
        guard !isBusy, let result = results.first(where: { $0.id == id }),
              let url = Dialogs.saveFile(named: result.fileName, kind: result.kind) else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            try await Task.detached { try Self.write(result, to: url) }.value
            status = "Saved \(url.lastPathComponent)."
            if results.count == 1 { markExported() }
        } catch {
            Dialogs.error("Save failed", error.localizedDescription)
        }
    }

    func exportZipInteractive() {
        guard canExport, let url = Dialogs.saveZip(defaultName: zipName) else { return }
        Task {
            do { try await exportZip(to: url) } catch { Dialogs.error("Export failed", error.localizedDescription) }
        }
    }

    func exportFolderInteractive() {
        guard canExport, let folder = Dialogs.chooseFolder() else { return }
        Task {
            do { try await exportIndividual(to: folder) } catch { Dialogs.error("Export failed", error.localizedDescription) }
        }
    }

    func exportZip(to url: URL) async throws {
        guard !results.isEmpty else { throw EngineError("Apply branding before exporting.") }
        isBusy = true
        defer { isBusy = false }
        status = "Creating ZIP export…"
        let folder = FileNameGenerator.folderName(prefix)
        let files = exportFiles()
        try await Task.detached {
            let manager = FileManager.default
            let staging = manager.temporaryDirectory.appendingPathComponent("alpha_zip_\(UUID().uuidString)")
            defer { try? manager.removeItem(at: staging) }
            let root = staging.appendingPathComponent(folder)
            try manager.createDirectory(at: root, withIntermediateDirectories: true)
            for (result, name) in files { try Self.write(result, to: root.appendingPathComponent(name)) }

            let archive = staging.appendingPathComponent("export.zip")
            let ditto = Process()
            ditto.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
            // No resource forks or extended attributes, so Windows recipients don't get __MACOSX clutter.
            ditto.arguments = ["-c", "-k", "--norsrc", "--noextattr", "--noqtn", "--keepParent", root.path, archive.path]
            try ditto.run()
            ditto.waitUntilExit()
            guard ditto.terminationStatus == 0 else { throw EngineError("Couldn't create the ZIP archive.") }
            if manager.fileExists(atPath: url.path) { try manager.removeItem(at: url) }
            try manager.moveItem(at: archive, to: url)
        }.value
        markExported()
        status = "ZIP export complete: \(url.lastPathComponent)."
    }

    func exportIndividual(to folder: URL) async throws {
        guard !results.isEmpty else { throw EngineError("Apply branding before exporting.") }
        isBusy = true
        defer { isBusy = false }
        let files = exportFiles()
        progress = 0
        for (index, (result, name)) in files.enumerated() {
            status = "Saving \(index + 1) of \(files.count) (\(name))…"
            try await Task.detached { try Self.write(result, to: folder.appendingPathComponent(name)) }.value
            progress = Double(index + 1) / Double(files.count)
        }
        markExported()
        status = "Export complete: \(files.count) file(s) saved to \(folder.lastPathComponent)."
    }

    private func exportFiles() -> [(BrandedResult, String)] {
        results.map { ($0, FileNameGenerator.generate(prefix, index: $0.sequenceIndex, total: $0.batchSize, kind: $0.kind)) }
    }

    private nonisolated static func write(_ result: BrandedResult, to url: URL) throws {
        if result.kind == .video, let video = result.videoURL {
            if FileManager.default.fileExists(atPath: url.path) { try FileManager.default.removeItem(at: url) }
            try FileManager.default.copyItem(at: video, to: url)
        } else {
            try result.jpegData.write(to: url, options: .atomic)
        }
    }

    // MARK: Templates

    func importTemplate() {
        guard !isBusy, let url = Dialogs.openTemplate() else { return }
        do {
            let template = try templateStore.importTemplate(from: url)
            templates = templateStore.templates
            selectedTemplateID = template.id
            status = "Template '\(template.name)' saved and activated (\(template.dimensionsText))."
        } catch {
            Dialogs.error("Invalid template", error.localizedDescription)
        }
    }

    func deleteTemplate() {
        guard !isBusy, let template = activeTemplate, !template.isBuiltIn,
              Dialogs.confirm("Delete template?",
                              "Delete the branding template \"\(template.name)\"? This can't be undone.",
                              action: "Delete") else { return }
        if templateStore.delete(template.id) {
            templates = templateStore.templates
            selectedTemplateID = templateStore.activeID
            status = "Template '\(template.name)' deleted."
        }
    }

    private func templateChanged() {
        guard let template = activeTemplate, template.id != templateStore.activeID else { return }
        templateStore.setActive(template.id)
        status = "Active template: \(template.name) (\(template.dimensionsText))."
    }

    // MARK: Session

    /// Asks before an action ends a session with branded results. Returns true when the action may
    /// proceed (nothing to lose, discarded, or exported first).
    private func confirmEndSession(title: String, unsaved: String, active: String) async -> Bool {
        guard needsSavePrompt else {
            discardResults()
            return true
        }
        switch Dialogs.sessionPrompt(title: title, message: hasUnsavedEdits ? unsaved : active) {
        case .export:
            guard let url = Dialogs.saveZip(defaultName: zipName) else {
                status = "Save canceled. Current session retained."
                return false
            }
            do {
                try await exportZip(to: url)
            } catch {
                status = "Export failed: \(error.localizedDescription)"
                return false
            }
            discardResults()
            return true
        case .discard:
            discardResults()
            return true
        case .cancel:
            status = "Canceled. Current session retained."
            return false
        }
    }

    func confirmQuit() async -> Bool {
        await confirmEndSession(
            title: "Quit with unexported branded items?",
            unsaved: "You have unsaved edits in the current session. Export them before quitting?",
            active: "The branded items in this session haven't been exported. Export them before quitting?")
    }

    func removeTemporaryVideos() {
        for url in results.compactMap(\.videoURL) { try? FileManager.default.removeItem(at: url) }
    }

    private func discardResults() {
        removeTemporaryVideos()
        results = []
        hasUnsavedEdits = false
        exportedSinceChange = false
    }

    private func markEdited() {
        hasUnsavedEdits = true
        exportedSinceChange = false
    }

    private func markExported() {
        hasUnsavedEdits = false
        exportedSinceChange = true
    }

    private func prefixChanged() {
        guard !results.isEmpty else { return }
        for index in results.indices {
            results[index].fileName = FileNameGenerator.generate(
                prefix, index: results[index].sequenceIndex, total: results[index].batchSize, kind: results[index].kind)
        }
        if !isBusy { markEdited() }
    }
}
