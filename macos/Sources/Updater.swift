import AppKit
import CryptoKit

/// Keeps the Mac app current like the Windows updater: a quiet check 3 seconds after launch and every
/// 8 hours, Check for Updates… on demand, Remind Me Later (8 hours) and Skip This Version. Update Now
/// downloads the release's .pkg, verifies it against the release's SHA256SUMS.txt and opens it in Installer.
@MainActor
final class Updater {
    private static let latestRelease = URL(string: "https://api.github.com/repos/Deign86/Alpha_Branding/releases/latest")!
    private static let interval: TimeInterval = 8 * 60 * 60
    private static let skippedKey = "UpdateSkippedVersion"
    private static let remindKey = "UpdateRemindAfter"

    private let model: StudioModel
    private var isChecking = false

    init(model: StudioModel) {
        self.model = model
    }

    static var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }

    func start() {
        Task {
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            while true {
                await check(manual: false)
                try? await Task.sleep(nanoseconds: UInt64(Self.interval * 1_000_000_000))
            }
        }
    }

    /// Automatic checks stay silent unless an update is due; a manual check always answers.
    func check(manual: Bool) async {
        guard !isChecking else { return }
        isChecking = true
        defer { isChecking = false }

        let release: Release
        do {
            release = try await Self.fetchLatest()
        } catch {
            if manual { Dialogs.error("Couldn't check for updates", error.localizedDescription) }
            return
        }
        let current = Self.currentVersion
        guard Release.isNewer(release.version, than: current) else {
            if manual { Dialogs.info("You're up to date", "Alpha Branding Studio \(current) is the latest version.") }
            return
        }
        guard let installer = release.installer else {
            if manual { Dialogs.error("Update not available for Mac", "Version \(release.version) has no Mac installer.") }
            return
        }

        let defaults = UserDefaults.standard
        if !manual {
            if defaults.string(forKey: Self.skippedKey) == release.tag { return }
            if let remindAfter = defaults.object(forKey: Self.remindKey) as? Date, remindAfter > Date() { return }
            // The prompt is modal; don't interrupt branding that is in progress.
            while model.isBusy { try? await Task.sleep(nanoseconds: 30_000_000_000) }
        }

        switch Dialogs.updatePrompt(version: release.version, current: current, notes: release.summary) {
        case .install:
            await install(release, installer: installer)
        case .later:
            defaults.set(Date().addingTimeInterval(Self.interval), forKey: Self.remindKey)
        case .skip:
            defaults.set(release.tag, forKey: Self.skippedKey)
        }
    }

    private func install(_ release: Release, installer: Release.Asset) async {
        do {
            let package = try await Self.download(installer, verifiedBy: release.checksums)
            NSWorkspace.shared.open(package)
            // Quit so Installer replaces a closed app; unsaved branded results still prompt first.
            NSApp.terminate(nil)
        } catch {
            Dialogs.error("Couldn't install the update", error.localizedDescription)
        }
    }

    private static func fetchLatest() async throws -> Release {
        var request = URLRequest(url: latestRelease, timeoutInterval: 15)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        let (data, response) = try await URLSession.shared.data(for: request)
        switch (response as? HTTPURLResponse)?.statusCode {
        case 200: return try JSONDecoder().decode(Release.self, from: data)
        case 403, 429: throw EngineError("GitHub's limit on update checks was reached. Try again in an hour.")
        case 404: throw EngineError("No releases have been published yet.")
        default: throw EngineError("GitHub didn't answer the update check. Try again later.")
        }
    }

    private static func download(_ installer: Release.Asset, verifiedBy checksums: Release.Asset?) async throws -> URL {
        guard let checksums else {
            throw EngineError("This release has no checksum file, so the download can't be verified.")
        }
        let (sums, _) = try await URLSession.shared.data(from: checksums.url)
        guard let expected = Release.checksum(for: installer.name, in: String(decoding: sums, as: UTF8.self)) else {
            throw EngineError("The checksum file doesn't list \(installer.name).")
        }

        let (temporary, response) = try await URLSession.shared.download(from: installer.url)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else {
            throw EngineError("The installer download failed. Try again later.")
        }
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("AlphaBrandingUpdate", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let package = folder.appendingPathComponent(installer.name)
        try? FileManager.default.removeItem(at: package)
        try FileManager.default.moveItem(at: temporary, to: package)

        let actual = SHA256.hash(data: try Data(contentsOf: package)).map { String(format: "%02x", $0) }.joined()
        guard actual == expected else {
            try? FileManager.default.removeItem(at: package)
            throw EngineError("The downloaded installer doesn't match its published checksum, so it was deleted.")
        }
        return package
    }
}
