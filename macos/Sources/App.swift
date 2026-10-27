import SwiftUI

@main
struct AlphaBrandingApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        Window("Alpha Premier Branding Studio", id: "main") {
            MainView()
                .environmentObject(appDelegate.model)
                .frame(minWidth: 960, minHeight: 700)
                .preferredColorScheme(.dark)
        }
        .defaultSize(width: 1260, height: 920)
        .commands { StudioCommands(model: appDelegate.model) }
    }
}

private struct StudioCommands: Commands {
    @ObservedObject var model: StudioModel

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("Select Media…") { model.selectMedia() }
                .keyboardShortcut("o")
                .disabled(model.isBusy)
            Button("Add More Media…") { model.addMore() }
                .keyboardShortcut("o", modifiers: [.command, .shift])
                .disabled(model.isBusy || model.staged.isEmpty || model.hasResults)
        }
        CommandMenu("Branding") {
            Button("Apply Branding") { Task { await model.apply() } }
                .keyboardShortcut("b")
                .disabled(!model.canApply)
            Divider()
            Button("Export as ZIP Archive…") { model.exportZipInteractive() }
                .keyboardShortcut("e")
                .disabled(!model.canExport)
            Button("Export as Individual Files…") { model.exportFolderInteractive() }
                .keyboardShortcut("e", modifiers: [.command, .shift])
                .disabled(!model.canExport)
            Divider()
            Button("Import Branding Template…") { model.importTemplate() }
                .disabled(model.isBusy)
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    let model = StudioModel()

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    /// Photos dropped on the Dock icon or opened with "Open With" in Finder.
    func application(_ application: NSApplication, open urls: [URL]) {
        model.open(urls)
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard model.needsSavePrompt, !model.isBusy else { return .terminateNow }
        Task {
            sender.reply(toApplicationShouldTerminate: await model.confirmQuit())
        }
        return .terminateLater
    }

    func applicationWillTerminate(_ notification: Notification) {
        model.removeTemporaryVideos()
    }
}
