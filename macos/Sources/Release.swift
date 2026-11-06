import Foundation

/// A GitHub release as the update check reads it, with the rules for picking and verifying the Mac installer.
struct Release: Decodable {
    struct Asset: Decodable {
        let name: String
        let url: URL

        enum CodingKeys: String, CodingKey {
            case name
            case url = "browser_download_url"
        }
    }

    let tag: String
    let notes: String
    let assets: [Asset]

    enum CodingKeys: String, CodingKey {
        case tag = "tag_name"
        case notes = "body"
        case assets
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        tag = try container.decode(String.self, forKey: .tag)
        notes = try container.decodeIfPresent(String.self, forKey: .notes) ?? ""
        assets = try container.decodeIfPresent([Asset].self, forKey: .assets) ?? []
    }

    var version: String { tag.hasPrefix("v") || tag.hasPrefix("V") ? String(tag.dropFirst()) : tag }
    var installer: Asset? { assets.first { $0.name.hasSuffix("-macos.pkg") } }
    var checksums: Asset? { assets.first { $0.name == "SHA256SUMS.txt" } }

    /// The pull request lines of GitHub's generated notes, without the author and link.
    var summary: String {
        notes.components(separatedBy: .newlines)
            .filter { $0.hasPrefix("* ") || $0.hasPrefix("- ") }
            .prefix(8)
            .map { line -> String in
                var text = String(line.dropFirst(2))
                if let author = text.range(of: " by @") { text = String(text[..<author.lowerBound]) }
                return "• " + text
            }
            .joined(separator: "\n")
    }

    /// Compares versions number by number; a leading "v" and any "-beta" or "+build" suffix are ignored.
    static func isNewer(_ candidate: String, than current: String) -> Bool {
        func numbers(_ version: String) -> [Int] {
            var text = version.trimmingCharacters(in: .whitespaces)
            if text.first == "v" || text.first == "V" { text.removeFirst() }
            if let suffix = text.firstIndex(where: { $0 == "-" || $0 == "+" }) { text = String(text[..<suffix]) }
            return text.split(separator: ".").map { Int($0) ?? 0 }
        }
        let new = numbers(candidate), old = numbers(current)
        for index in 0..<max(new.count, old.count) {
            let a = index < new.count ? new[index] : 0, b = index < old.count ? old[index] : 0
            if a != b { return a > b }
        }
        return false
    }

    /// The SHA-256 published for `fileName` in a SHA256SUMS.txt body ("HASH  name" per line), lowercased.
    static func checksum(for fileName: String, in sums: String) -> String? {
        for line in sums.replacingOccurrences(of: "\u{FEFF}", with: "").components(separatedBy: .newlines) {
            let fields = line.split(whereSeparator: \.isWhitespace)
            if fields.count >= 2, fields.last.map({ String($0) }) == fileName { return fields[0].lowercased() }
        }
        return nil
    }
}
