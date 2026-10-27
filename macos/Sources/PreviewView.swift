import AVKit
import SwiftUI

struct PreviewView: View {
    @EnvironmentObject private var model: StudioModel
    @Environment(\.dismiss) private var dismiss
    @State var currentID: UUID

    private var index: Int? { model.results.firstIndex { $0.id == currentID } }

    var body: some View {
        VStack(spacing: 14) {
            if let index {
                let result = model.results[index]
                HStack(spacing: 12) {
                    Text(result.fileName).font(.system(size: 14, weight: .bold)).foregroundStyle(Theme.goldLight)
                    Text("\(index + 1) of \(model.results.count)").font(.system(size: 12)).foregroundStyle(Theme.textMuted)
                    Spacer()
                    Button("SAVE") { Task { await model.save(result.id) } }.studioButton().disabled(model.isBusy)
                    Button("CLOSE") { dismiss() }.studioButton().keyboardShortcut(.cancelAction)
                }
                Group {
                    if result.kind == .video, let url = result.videoURL {
                        VideoPreview(url: url).id(url)
                    } else {
                        Image(nsImage: result.preview).resizable().interpolation(.high).aspectRatio(contentMode: .fit)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Theme.input)
                .clipShape(RoundedRectangle(cornerRadius: 10))
                HStack {
                    Button("‹ PREVIOUS") { step(-1) }
                        .studioButton()
                        .keyboardShortcut(.leftArrow, modifiers: [])
                        .disabled(index == 0)
                    Spacer()
                    Button("NEXT ›") { step(1) }
                        .studioButton()
                        .keyboardShortcut(.rightArrow, modifiers: [])
                        .disabled(index + 1 >= model.results.count)
                }
            } else {
                Button("CLOSE") { dismiss() }.studioButton()
            }
        }
        .padding(20)
        .frame(minWidth: 860, idealWidth: 960, minHeight: 640, idealHeight: 820)
        .background(Theme.surface)
        .preferredColorScheme(.dark)
    }

    private func step(_ offset: Int) {
        guard let index, model.results.indices.contains(index + offset) else { return }
        currentID = model.results[index + offset].id
    }
}

private struct VideoPreview: View {
    let url: URL
    @State private var player: AVPlayer?

    var body: some View {
        VideoPlayer(player: player)
            .onAppear { player = AVPlayer(url: url) }
            .onDisappear { player?.pause() }
    }
}
