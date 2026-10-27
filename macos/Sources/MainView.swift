import SwiftUI
import UniformTypeIdentifiers

private struct PreviewRequest: Identifiable { let id: UUID }

struct MainView: View {
    @EnvironmentObject private var model: StudioModel
    @State private var cropRequest: CropRequest?
    @State private var previewRequest: PreviewRequest?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                HeaderBar()
                ControlPanel()
                if model.hasResults {
                    ResultsSection(
                        onEdit: { cropRequest = model.cropRequest(forResult: $0) },
                        onPreview: { previewRequest = PreviewRequest(id: $0) })
                } else if !model.staged.isEmpty {
                    StagedSection(onEdit: { cropRequest = model.cropRequest(forStaged: $0) })
                } else {
                    EmptyState()
                }
            }
            .padding(24)
        }
        .background(Theme.background)
        .overlay {
            if model.isDragOver {
                RoundedRectangle(cornerRadius: 16)
                    .strokeBorder(Theme.gold, style: StrokeStyle(lineWidth: 3, dash: [10, 6]))
                    .padding(8)
                    .allowsHitTesting(false)
            }
        }
        .onDrop(of: [.fileURL], isTargeted: $model.isDragOver) { providers in
            guard !model.isBusy else { return false }
            Task { model.open(await Self.fileURLs(from: providers)) }
            return true
        }
        .sheet(item: $cropRequest) { request in
            CropEditorView(request: request) { left, right in model.applyCrop(request, left: left, right: right) }
        }
        .sheet(item: $previewRequest) { request in
            PreviewView(currentID: request.id).environmentObject(model)
        }
    }

    private static func fileURLs(from providers: [NSItemProvider]) async -> [URL] {
        var urls: [URL] = []
        for provider in providers {
            let url = await withCheckedContinuation { (continuation: CheckedContinuation<URL?, Never>) in
                _ = provider.loadObject(ofClass: URL.self) { url, _ in continuation.resume(returning: url) }
            }
            if let url { urls.append(url) }
        }
        return urls
    }
}

private struct HeaderBar: View {
    @EnvironmentObject private var model: StudioModel

    var body: some View {
        HStack(spacing: 14) {
            Image(nsImage: Theme.logo).resizable().interpolation(.high).frame(width: 44, height: 44)
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 10) {
                    (Text("ALPHA PREMIER ").foregroundColor(.white) + Text("| REALTY").foregroundColor(Theme.goldLight))
                        .font(.system(size: 20, weight: .bold))
                    Badge("GROUP OF COMPANIES")
                }
                Text("Official Property Listing Watermarking & Studio Suite")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(Theme.gold)
            }
            Spacer()
            HStack(spacing: 8) {
                Circle().fill(Theme.gold).frame(width: 7, height: 7)
                Text(model.selectionSummary).font(.system(size: 12, weight: .bold))
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            .background(Capsule().fill(Theme.input))
            .overlay(Capsule().strokeBorder(Theme.border))
        }
        .panel()
    }
}

private struct ControlPanel: View {
    @EnvironmentObject private var model: StudioModel

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .top, spacing: 20) {
                Button("SELECT MEDIA") { model.selectMedia() }
                    .studioButton(.hero)
                    .disabled(model.isBusy)

                VStack(alignment: .leading, spacing: 6) {
                    FieldLabel("FILENAME PREFIX")
                    TextField("", text: $model.prefix)
                        .textFieldStyle(.plain)
                        .font(.system(size: 13))
                        .padding(8)
                        .frame(width: 210)
                        .background(RoundedRectangle(cornerRadius: 8).fill(Theme.input))
                        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.border))
                        .disabled(model.isBusy)
                    (Text("Preview: ").foregroundColor(Theme.textMuted) + Text(model.patternPreview).foregroundColor(Theme.goldLight))
                        .font(.system(size: 11, weight: .medium))
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .frame(width: 210, alignment: .leading)
                }

                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 8) {
                        FieldLabel("BRANDING TEMPLATE")
                        if let template = model.activeTemplate { Badge(template.aspectRatioText) }
                    }
                    HStack(spacing: 8) {
                        Picker("Branding template", selection: $model.selectedTemplateID) {
                            ForEach(model.templates) { Text($0.name).tag($0.id) }
                        }
                        .labelsHidden()
                        .frame(width: 210)
                        .disabled(model.isBusy)
                        Button("+ IMPORT") { model.importTemplate() }.studioButton().disabled(model.isBusy)
                        if model.activeTemplate?.isBuiltIn == false {
                            Button("DELETE") { model.deleteTemplate() }.studioButton().disabled(model.isBusy)
                        }
                    }
                    Text(model.activeTemplate?.dimensionsText ?? "")
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.textMuted)
                }
                Spacer(minLength: 0)
            }

            HStack(spacing: 10) {
                if let hint = model.applyHint {
                    Text(hint).font(.system(size: 12, weight: .medium)).foregroundStyle(Theme.goldLight)
                }
                Spacer()
                Button("APPLY BRANDING") { Task { await model.apply() } }
                    .studioButton(.primary)
                    .disabled(!model.canApply)
                Button("EXPORT ZIP") { model.exportZipInteractive() }
                    .studioButton()
                    .disabled(!model.canExport)
                Button("EXPORT FILES") { model.exportFolderInteractive() }
                    .studioButton()
                    .disabled(!model.canExport)
            }

            Rectangle().fill(Theme.border).frame(height: 1)

            HStack(spacing: 10) {
                Text("STATUS:").font(.system(size: 12, weight: .bold)).foregroundStyle(Theme.gold)
                Text(model.status)
                    .font(.system(size: 12, weight: .medium))
                    .lineLimit(3)
                    .textSelection(.enabled)
                Spacer()
                ProgressView(value: model.progress)
                    .tint(Theme.gold)
                    .frame(width: 240)
                    .opacity(model.isBusy || model.progress > 0 ? 1 : 0.3)
            }
        }
        .panel()
    }
}

private struct SectionHeader<Buttons: View>: View {
    let title: String
    let detail: String
    @ViewBuilder let buttons: Buttons

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Circle().fill(Theme.gold).frame(width: 7, height: 7)
                (Text(title).foregroundColor(Theme.goldLight).bold() + Text(" — \(detail)").foregroundColor(Theme.textSecondary))
                    .font(.system(size: 12))
            }
            HStack(spacing: 8) { buttons }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .panel(padding: 14)
    }
}

private let cardColumns = [GridItem(.adaptive(minimum: 250, maximum: 380), spacing: 16)]

private struct StagedSection: View {
    @EnvironmentObject private var model: StudioModel
    let onEdit: (URL) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            SectionHeader(title: "SELECTED MEDIA (\(model.staged.count))",
                          detail: "Ready to process. Click APPLY BRANDING to watermark.") {
                Button("COMBINE IMAGES") { model.layoutMode = .combine }
                    .studioButton(selected: model.layoutMode == .combine)
                Button("KEEP SEPARATE") { model.layoutMode = .separate }
                    .studioButton(selected: model.layoutMode == .separate)
                Spacer()
                Button("RESET ALL CROPS") { model.resetAllStagedCrops() }.studioButton()
                Button("ADD MORE") { model.addMore() }.studioButton()
                Button("CLEAR ALL") { model.clearSelection() }.studioButton()
            }
            .disabled(model.isBusy)

            LazyVGrid(columns: cardColumns, spacing: 16) {
                ForEach(model.staged) { item in
                    StagedCard(item: item) { onEdit(item.url) }
                }
            }
        }
    }
}

private struct StagedCard: View {
    @EnvironmentObject private var model: StudioModel
    let item: StagedItem
    let onEdit: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ThumbnailView(image: model.thumbnails[item.url], isVideo: item.kind == .video, duration: item.durationText)
            HStack {
                Text(item.fileName).font(.system(size: 12, weight: .bold)).lineLimit(1).truncationMode(.middle)
                Spacer()
                Text(item.sizeText).font(.system(size: 11)).foregroundStyle(Theme.textMuted)
            }
            HStack(spacing: 6) {
                if !item.crop.isDefault { Badge("CROPPED (\(item.crop.zoomText))") }
                if item.crop.hasRedactions { Badge("PEN MARKS") }
                if item.isSolo { Badge("SOLO") }
                if let partner = item.partner { Badge("PAIRED: \(partner.lastPathComponent)") }
            }
            .lineLimit(1)
            .frame(minHeight: 18)

            if item.kind == .image {
                HStack(spacing: 6) {
                    Button("EDIT CROP", action: onEdit).studioButton(fill: true)
                    Button(item.isSolo ? "SOLO" : "AUTO/COMBINE") { model.toggleSolo(item.url) }
                        .studioButton(fill: true, selected: item.isSolo)
                        .help("SOLO keeps this photo on its own instead of pairing it")
                }
                HStack(spacing: 6) {
                    if item.partner != nil {
                        Button("UNPAIR") { model.unpair(item.url) }.studioButton(fill: true)
                    } else {
                        let candidates = model.pairCandidates(for: item.url)
                        Menu {
                            ForEach(candidates) { candidate in
                                Button(candidate.fileName) { model.pair(item.url, with: candidate.url) }
                            }
                        } label: {
                            Text("PAIR WITH…").font(.system(size: 11, weight: .bold))
                        }
                        .menuStyle(.borderlessButton)
                        .padding(.vertical, 4)
                        .frame(maxWidth: .infinity)
                        .background(RoundedRectangle(cornerRadius: 8).fill(Theme.elevated))
                        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.border))
                        .disabled(!item.canPair || candidates.isEmpty)
                    }
                    Button("RESET CROP") { model.resetStagedCrop(item.url) }
                        .studioButton(fill: true)
                        .disabled(item.crop.isDefault)
                }
            }
            Button("REMOVE FROM SELECTION") { model.remove(item.url) }.studioButton(fill: true)
        }
        .disabled(model.isBusy)
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 12).fill(Theme.surface))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(item.partner != nil ? Theme.gold : Theme.border))
    }
}

private struct ResultsSection: View {
    @EnvironmentObject private var model: StudioModel
    let onEdit: (UUID) -> Void
    let onPreview: (UUID) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            SectionHeader(title: "BRANDED OUTPUT (\(model.results.count))",
                          detail: "Review, fine-tune crops, then export.") {
                Spacer()
                Button("RESET ALL CROPS") { Task { await model.resetAllResultCrops() } }.studioButton()
                Button("CLEAR SESSION") { Task { await model.clearSession() } }.studioButton()
                Button("EXPORT ZIP") { model.exportZipInteractive() }.studioButton(.primary)
            }
            .disabled(model.isBusy)

            LazyVGrid(columns: cardColumns, spacing: 16) {
                ForEach(model.results) { result in
                    ResultCard(result: result, onEdit: { onEdit(result.id) }, onPreview: { onPreview(result.id) })
                }
            }
        }
    }
}

private struct ResultCard: View {
    @EnvironmentObject private var model: StudioModel
    let result: BrandedResult
    let onEdit: () -> Void
    let onPreview: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ThumbnailView(image: result.preview, isVideo: result.kind == .video, duration: result.durationText, height: 200)
                .onTapGesture(perform: onPreview)
            HStack {
                Text(result.fileName)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(Theme.goldLight)
                    .lineLimit(1)
                    .truncationMode(.middle)
                Spacer()
                if result.hasCustomCrop { Badge("CUSTOM CROP") }
            }
            HStack(spacing: 6) {
                Button("PREVIEW", action: onPreview).studioButton(fill: true)
                Button("SAVE") { Task { await model.save(result.id) } }.studioButton(fill: true)
            }
            if result.canEdit {
                HStack(spacing: 6) {
                    Button("EDIT CROP", action: onEdit).studioButton(fill: true)
                    Button("RESET") { Task { await model.resetResultCrop(result.id) } }
                        .studioButton(fill: true)
                        .disabled(!result.hasCustomCrop)
                }
            }
        }
        .disabled(model.isBusy)
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 12).fill(Theme.surface))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Theme.border))
    }
}

struct ThumbnailView: View {
    let image: NSImage?
    var isVideo = false
    var duration = ""
    var height: CGFloat = 180

    var body: some View {
        ZStack {
            Theme.input
            if let image {
                Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fit)
            } else {
                ProgressView().controlSize(.small)
            }
            if isVideo {
                Image(systemName: "play.circle.fill")
                    .font(.system(size: 38))
                    .foregroundStyle(.white.opacity(0.85))
                    .shadow(radius: 4)
            }
        }
        .overlay(alignment: .bottomTrailing) {
            if isVideo && !duration.isEmpty {
                Text(duration)
                    .font(.system(size: 11, weight: .bold).monospacedDigit())
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(Capsule().fill(.black.opacity(0.7)))
                    .padding(8)
            }
        }
        .frame(maxWidth: .infinity)
        .frame(height: height)
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .contentShape(Rectangle())
    }
}

private struct EmptyState: View {
    @EnvironmentObject private var model: StudioModel

    var body: some View {
        VStack(spacing: 14) {
            Image(systemName: "photo.on.rectangle.angled")
                .font(.system(size: 46))
                .foregroundStyle(Theme.gold)
            Text("Drop property photos and videos here")
                .font(.system(size: 18, weight: .bold))
            Text("JPG, PNG, HEIC, WEBP and BMP photos, MP4 and MOV videos. Portrait shots are paired side by side automatically.")
                .font(.system(size: 12))
                .foregroundStyle(Theme.textMuted)
                .multilineTextAlignment(.center)
            Button("SELECT MEDIA TO BEGIN") { model.selectMedia() }
                .studioButton(.primary)
                .padding(.top, 6)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 70)
        .background(RoundedRectangle(cornerRadius: 14).fill(Theme.surface))
        .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(Theme.border, style: StrokeStyle(lineWidth: 1.5, dash: [8, 6])))
    }
}
