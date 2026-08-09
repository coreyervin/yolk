import Foundation
import Testing

@testable import YolkKit

@Suite("ConsoleState")
struct ConsoleStateTests {
    private func session(onConsole: Bool?, locked: Bool?) -> [String: Any] {
        var dict: [String: Any] = [:]
        if let onConsole { dict[SessionKey.onConsole] = onConsole }
        if let locked { dict[SessionKey.screenLocked] = locked }
        return dict
    }

    @Test("is active when on console and unlocked")
    func activeWhenOnConsoleAndUnlocked() {
        #expect(ConsoleState.from(session: session(onConsole: true, locked: false)) == .active)
    }

    @Test("is locked when the screen is locked")
    func lockedWhenScreenLocked() {
        #expect(ConsoleState.from(session: session(onConsole: true, locked: true)) == .locked)
    }

    @Test("is switched out when not on console")
    func switchedOutWhenNotOnConsole() {
        #expect(ConsoleState.from(session: session(onConsole: false, locked: false)) == .switchedOut)
    }

    @Test("lock takes precedence when both locked and switched out")
    func lockWinsOverSwitchedOut() {
        #expect(ConsoleState.from(session: session(onConsole: false, locked: true)) == .locked)
    }

    @Test("is unknown when the session dictionary is unreadable")
    func unknownWhenSessionMissing() {
        #expect(ConsoleState.from(session: nil) == .unknown)
    }

    @Test("a missing on-console key fails safe rather than assuming active")
    func missingOnConsoleKeyFailsSafe() {
        let state = ConsoleState.from(session: session(onConsole: nil, locked: false))
        #expect(state != .active)
        #expect(state.pausesSimulation)
    }

    @Test("only active permits simulation")
    func onlyActivePermitsSimulation() {
        #expect(ConsoleState.active.pausesSimulation == false)
        #expect(ConsoleState.locked.pausesSimulation)
        #expect(ConsoleState.switchedOut.pausesSimulation)
        #expect(ConsoleState.unknown.pausesSimulation)
    }
}
