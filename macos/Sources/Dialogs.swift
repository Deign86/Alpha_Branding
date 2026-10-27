import AppKit
import UniformTypeIdentifiers

@MainActor
enum Dialogs {
    enum SessionChoice { case export, discard, cancel }

    static func openMedia() -> [URL]? {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.allowedContentTypes = StudioModel.mediaTypes
        panel.message = "Choose property photos and videos"
        return panel.runModal() == .OK ? panel.urls : nil
    }

    static func openTemplate() -> URL? {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.allowedContentTypes = TemplateStore.extensions.compactMap { UTType(filenameExtension: $0) }
        panel.message = "Import a branding template (a transparent PNG frame works best)"
        return panel.runModal() == .OK ? panel.url : nil
    }

    static func saveZip(defaultName: String) -> URL? {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.zip]
        panel.nameFieldStringValue = defaultName
        return panel.runModal() == .OK ? panel.url : nil
    }

    static func saveFile(named name: String, kind: MediaKind) -> URL? {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [kind == .video ? .mpeg4Movie : .jpeg]
        panel.nameFieldStringValue = name
        return panel.runModal() == .OK ? panel.url : nil
    }

    static func chooseFolder() -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.prompt = "Export Here"
        panel.message = "Choose a folder for the branded files"
        return panel.runModal() == .OK ? panel.url : nil
    }

    /// Cancel is the default button, so Return never discards work by accident.
    static func confirm(_ title: String, _ message: String, action: String) -> Bool {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = title
        alert.informativeText = message
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: action).hasDestructiveAction = true
        return alert.runModal() == .alertSecondButtonReturn
    }

    static func sessionPrompt(title: String, message: String) -> SessionChoice {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = title
        alert.informativeText = message
        alert.addButton(withTitle: "Export ZIP…")
        alert.addButton(withTitle: "Discard").hasDestructiveAction = true
        alert.addButton(withTitle: "Cancel")
        switch alert.runModal() {
        case .alertFirstButtonReturn: return .export
        case .alertSecondButtonReturn: return .discard
        default: return .cancel
        }
    }

    static func error(_ title: String, _ message: String) {
        let alert = NSAlert()
        alert.alertStyle = .critical
        alert.messageText = title
        alert.informativeText = message
        alert.runModal()
    }
}
