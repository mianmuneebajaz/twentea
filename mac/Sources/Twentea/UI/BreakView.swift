import SwiftUI

struct BreakView: View {
    enum Style { case card, overlay }

    @ObservedObject var model: BreakViewModel
    @ObservedObject var scheduler: Scheduler
    let style: Style

    var body: some View {
        if let session = model.session {
            TimelineView(.periodic(from: .now, by: 0.5)) { context in
                content(session, now: context.date)
            }
        }
    }

    @ViewBuilder
    private func content(_ s: BreakSession, now: Date) -> some View {
        let left = max(0, s.endsAt.timeIntervalSince(now))
        let total = max(1, s.endsAt.timeIntervalSince(s.startedAt))
        let big = style == .overlay

        VStack(spacing: big ? 22 : 10) {
            HStack(spacing: 14) {
                ZStack {
                    Circle().stroke(Color.white.opacity(0.2), lineWidth: big ? 8 : 5)
                    Circle()
                        .trim(from: 0, to: left / total)
                        .stroke(Color.orange, style: StrokeStyle(lineWidth: big ? 8 : 5, lineCap: .round))
                        .rotationEffect(.degrees(-90))
                    Text(Self.format(left))
                        .font(.system(size: big ? 34 : 17, weight: .semibold, design: .rounded))
                        .monospacedDigit()
                }
                .frame(width: big ? 140 : 64, height: big ? 140 : 64)

                if !big {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(s.kind.title).font(.headline)
                        Text(Self.tip(s.kind)).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    }
                }
            }

            if big {
                Text(s.kind.title).font(.system(size: 30, weight: .bold, design: .rounded))
                Text(Self.tip(s.kind)).font(.title3).foregroundStyle(.white.opacity(0.8))
                    .multilineTextAlignment(.center).frame(maxWidth: 520)
                if s.level >= .softDim, scheduler.settings.features.restartOnInput {
                    Text("Moving the mouse or typing restarts the timer.")
                        .font(.callout).foregroundStyle(.white.opacity(0.5))
                }
            }

            buttons(s, now: now)
        }
        .padding(big ? 40 : 16)
        .foregroundStyle(big ? Color.white : Color.primary)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background {
            if !big {
                RoundedRectangle(cornerRadius: 18, style: .continuous).fill(.regularMaterial)
            }
        }
    }

    @ViewBuilder
    private func buttons(_ s: BreakSession, now: Date) -> some View {
        HStack(spacing: 10) {
            if s.canPostpone, scheduler.postponesLeft > 0 {
                Button("Postpone \(scheduler.settings.config(for: s.kind).postponeMinutes) min") { scheduler.postpone() }
            }
            if let at = s.skipAvailableAt {
                let wait = Int(ceil(at.timeIntervalSince(now)))
                Button(wait > 0 ? "Skip (\(wait))" : "Skip") { scheduler.skip() }
                    .disabled(wait > 0)
            } else {
                Text("Hold to end early")
                    .font(.callout)
                    .padding(.horizontal, 12).padding(.vertical, 6)
                    .background(Capsule().stroke(Color.white.opacity(0.4)))
                    .onLongPressGesture(minimumDuration: 3) { scheduler.forceEnd() }
            }
        }
        .controlSize(style == .overlay ? .large : .regular)
    }

    static func format(_ seconds: TimeInterval) -> String {
        let s = Int(seconds.rounded(.up))
        return s >= 60 ? String(format: "%d:%02d", s / 60, s % 60) : "\(s)"
    }

    static func tip(_ kind: BreakKind) -> String {
        kind == .micro
            ? "Look at something at least 6 metres away, ideally out a window. Blink slowly."
            : "Stand up, walk around, refill your tea. Keep your eyes off screens."
    }
}
