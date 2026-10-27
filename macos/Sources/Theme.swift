import SwiftUI

extension Color {
    init(hex: UInt32) {
        self.init(.sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255,
                  blue: Double(hex & 0xFF) / 255)
    }
}

/// Dark gold luxury palette, matching the Windows app.
enum Theme {
    static let background = Color(hex: 0x0A0A0A)
    static let surface = Color(hex: 0x111215)
    static let elevated = Color(hex: 0x18191E)
    static let input = Color(hex: 0x0D0E10)
    static let border = Color(hex: 0x423722)
    static let gold = Color(hex: 0xC5A059)
    static let goldLight = Color(hex: 0xE2C285)
    static let goldDark = Color(hex: 0x9E7B35)
    static let goldSubtle = Color(hex: 0x252015)
    static let textSecondary = Color(hex: 0xB0B4BC)
    static let textMuted = Color(hex: 0x9096A5)
    static let goldGradient = LinearGradient(colors: [goldLight, goldDark], startPoint: .topLeading, endPoint: .bottomTrailing)

    static let logo = NSImage(named: "logo_phoenix") ?? NSImage()
}

struct StudioButtonStyle: ButtonStyle {
    enum Kind { case primary, secondary, hero }

    var kind: Kind = .secondary
    var fill = false
    var selected = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        let gold = kind != .secondary || selected
        configuration.label
            .font(.system(size: kind == .secondary ? 11 : 13, weight: .bold))
            .lineLimit(1)
            .padding(.horizontal, kind == .hero ? 22 : kind == .primary ? 18 : 10)
            .padding(.vertical, kind == .hero ? 22 : kind == .primary ? 10 : 6)
            .frame(maxWidth: fill ? .infinity : nil)
            .foregroundStyle(gold ? Color.black : Color.white)
            .background(
                RoundedRectangle(cornerRadius: 8)
                    .fill(gold ? AnyShapeStyle(Theme.goldGradient) : AnyShapeStyle(Theme.elevated)))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(gold ? Theme.goldLight.opacity(0.5) : Theme.border))
            .opacity(isEnabled ? (configuration.isPressed ? 0.7 : 1) : 0.35)
            .contentShape(Rectangle())
    }
}

extension View {
    func panel(padding: CGFloat = 18) -> some View {
        self.padding(padding)
            .background(RoundedRectangle(cornerRadius: 14).fill(Theme.surface))
            .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(Theme.border))
    }

    func studioButton(_ kind: StudioButtonStyle.Kind = .secondary, fill: Bool = false, selected: Bool = false) -> some View {
        buttonStyle(StudioButtonStyle(kind: kind, fill: fill, selected: selected))
    }
}

struct FieldLabel: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text).font(.system(size: 11, weight: .bold)).foregroundStyle(Theme.gold).tracking(0.5)
    }
}

struct Badge: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.system(size: 10, weight: .bold))
            .foregroundStyle(Theme.goldLight)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(Capsule().fill(Theme.goldSubtle))
            .overlay(Capsule().strokeBorder(Theme.border))
    }
}
