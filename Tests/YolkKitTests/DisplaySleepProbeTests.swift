import Foundation
import Testing

@testable import YolkKit

@Suite("DisplaySleepProbe")
struct DisplaySleepProbeTests {
    /// Verbatim `pmset -g` output from macOS 26, tabs and all.
    static let realOutput = """
        System-wide power settings:
         DestroyFVKeyOnStandby\t\t0
        Currently in use:
         standby              1
         Sleep On Power Button 1
         SleepServices        0
         hibernatefile        /var/vm/sleepimage
         powernap             0
         networkoversleep     0
         disksleep            10
         sleep                1 (sleep prevented by powerd, coreaudiod, caffeinate)
         hibernatemode        3
         ttyskeepawake        1
         displaysleep         30
         tcpkeepalive         1
         lowpowermode         0
         womp                 0
        """

    @Test("reads displaysleep minutes from real pmset output as seconds")
    func parsesRealOutput() {
        #expect(DisplaySleepProbe.parse(pmsetOutput: Self.realOutput) == 1_800)
    }

    @Test("treats a displaysleep of 0 as disabled")
    func zeroMeansDisabled() {
        #expect(DisplaySleepProbe.parse(pmsetOutput: " displaysleep         0") == nil)
    }

    @Test("returns nil when there is no displaysleep line")
    func missingLine() {
        #expect(DisplaySleepProbe.parse(pmsetOutput: " disksleep            10") == nil)
    }

    @Test("returns nil for empty or unparseable output", arguments: [
        "",
        " displaysleep",
        " displaysleep         never",
    ])
    func unparseable(output: String) {
        #expect(DisplaySleepProbe.parse(pmsetOutput: output) == nil)
    }

    @Test("does not match a key that merely contains displaysleep")
    func doesNotMatchSubstringKeys() {
        #expect(DisplaySleepProbe.parse(pmsetOutput: " notdisplaysleep      15") == nil)
    }
}
