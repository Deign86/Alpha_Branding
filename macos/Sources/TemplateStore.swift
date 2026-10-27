import Foundation

struct BrandingTemplate: Identifiable, Codable, Equatable {
    var id: String
    var name: String
    var filePath: String
    var width: Int
    var height: Int
    var createdAt: Date
    var isBuiltIn = false

    private enum CodingKeys: String, CodingKey {
        case id, name, filePath, width, height, createdAt
    }

    var url: URL { URL(fileURLWithPath: filePath) }

    var dimensionsText: String { width > 0 && height > 0 ? "\(width) × \(height) px" : "Standard Resolution" }

    var aspectRatioText: String {
        guard width > 0, height > 0 else { return "6:5 (1.20:1)" }
        let ratio = Double(width) / Double(height)
        if abs(ratio - 1.20) < 0.015 { return "6:5 (1.20:1) • Official Ratio" }
        if abs(ratio - 16.0 / 9.0) < 0.02 { return "16:9 • Widescreen" }
        if abs(ratio - 4.0 / 3.0) < 0.02 { return "4:3 • Standard" }
        if abs(ratio - 1.0) < 0.02 { return "1:1 • Square" }
        return String(format: "%.2f:1 • Preserved Ratio", ratio)
    }
}

final class TemplateStore {
    static let defaultID = "alpha_premier_classic"
    static let extensions: Set<String> = ["png", "jpg", "jpeg", "jfif", "bmp", "webp"]

    private struct StoreData: Codable {
        var activeTemplateId: String
        var customTemplates: [BrandingTemplate]
    }

    let directory: URL
    private(set) var templates: [BrandingTemplate]
    private(set) var activeID = defaultID
    private var metaURL: URL { directory.appendingPathComponent("templates_meta.json") }

    init() {
        directory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Alpha Premier Realty/Branding Studio/Templates", isDirectory: true)
        let classicPath = Bundle.main.url(forResource: "alpha_branding", withExtension: "png")?.path ?? ""
        templates = [BrandingTemplate(
            id: Self.defaultID, name: "Alpha Premier Classic", filePath: classicPath,
            width: 2938, height: 2463, createdAt: Date(timeIntervalSince1970: 1_767_225_600), isBuiltIn: true)]

        if let data = try? Data(contentsOf: metaURL),
           let store = try? JSONDecoder().decode(StoreData.self, from: data) {
            activeID = store.activeTemplateId
            templates += store.customTemplates.filter { FileManager.default.fileExists(atPath: $0.filePath) }
        }
        if !templates.contains(where: { $0.id == activeID }) { activeID = Self.defaultID }
    }

    var active: BrandingTemplate { templates.first { $0.id == activeID } ?? templates[0] }

    func setActive(_ id: String) {
        guard templates.contains(where: { $0.id == id }), id != activeID else { return }
        activeID = id
        save()
    }

    func importTemplate(from source: URL) throws -> BrandingTemplate {
        guard Self.extensions.contains(source.pathExtension.lowercased()) else {
            throw EngineError("Unsupported image format. Please select a PNG, JPG, JFIF, or WEBP image.")
        }
        guard let size = ImageEngine.pixelSize(of: source), size.width > 0, size.height > 0 else {
            throw EngineError("Unable to read image dimensions.")
        }
        let name = source.deletingPathExtension().lastPathComponent
        let id = "tpl_" + UUID().uuidString.replacingOccurrences(of: "-", with: "").prefix(8).lowercased()
        let destination = directory.appendingPathComponent(
            "\(FileNameGenerator.sanitize(name, fallback: "Template"))_\(id).\(source.pathExtension)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try FileManager.default.copyItem(at: source, to: destination)

        let template = BrandingTemplate(id: id, name: name, filePath: destination.path,
                                        width: Int(size.width), height: Int(size.height), createdAt: Date())
        templates.append(template)
        activeID = id
        save()
        return template
    }

    func delete(_ id: String) -> Bool {
        guard let index = templates.firstIndex(where: { $0.id == id }), !templates[index].isBuiltIn else { return false }
        let removed = templates.remove(at: index)
        try? FileManager.default.removeItem(at: removed.url)
        if activeID == id { activeID = Self.defaultID }
        save()
        return true
    }

    private func save() {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = StoreData(activeTemplateId: activeID, customTemplates: templates.filter { !$0.isBuiltIn })
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try? encoder.encode(data).write(to: metaURL, options: .atomic)
    }
}
