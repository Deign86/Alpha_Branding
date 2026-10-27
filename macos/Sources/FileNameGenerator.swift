import Foundation

enum FileNameGenerator {
    static let defaultPrefix = "AlphaPremier_Photo"
    private static let maxComponentLength = 120
    private static let forbidden = Set("/\\:*?\"<>|")
    // Exports are often shared with Windows users, so Windows device names stay off-limits.
    private static let reservedNames: Set<String> = [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ]

    static func generate(_ prefix: String?, index: Int, total: Int, kind: MediaKind) -> String {
        generate(prefix, index: index, total: total, ext: kind == .video ? "mp4" : "jpg")
    }

    static func generate(_ prefix: String?, index: Int, total: Int, ext: String = "jpg") -> String {
        var safePrefix = sanitize(prefix)
        let digits = total >= 100 ? 3 : 2
        let suffix = "_" + String(format: "%0\(digits)d", index + 1) + "." + ext
        if safePrefix.count + suffix.count > maxComponentLength {
            safePrefix = trimTrailing(String(safePrefix.prefix(max(1, maxComponentLength - suffix.count))))
        }
        return safePrefix + suffix
    }

    static func folderName(_ prefix: String?) -> String { sanitize(prefix) }

    static func sanitize(_ value: String?, fallback: String = defaultPrefix) -> String {
        let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let source = trimmed.isEmpty ? fallback : trimmed
        var clean = String(source.filter { character in
            !forbidden.contains(character)
                && !character.unicodeScalars.contains { CharacterSet.controlCharacters.contains($0) }
        })
        clean = trimTrailing(clean.trimmingCharacters(in: .whitespaces))
        while clean.hasPrefix(".") { clean.removeFirst() }
        let stem = (clean as NSString).deletingPathExtension.uppercased()
        if clean.isEmpty || reservedNames.contains(stem) { clean = fallback }
        if clean.count > maxComponentLength { clean = trimTrailing(String(clean.prefix(maxComponentLength))) }
        return clean.isEmpty ? fallback : clean
    }

    private static func trimTrailing(_ value: String) -> String {
        var result = value
        while let last = result.last, last == " " || last == "." { result.removeLast() }
        return result
    }
}
