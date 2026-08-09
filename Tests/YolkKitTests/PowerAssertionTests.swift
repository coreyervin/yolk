import Foundation
import Testing

@testable import YolkKit

@Suite("PowerAssertion")
struct PowerAssertionTests {
    @Test("creates an assertion with the given reason")
    func createsWithReason() throws {
        let recorder = AssertionRecorder()
        _ = try PowerAssertion(
            reason: "keeping Mac awake", environment: .fake(recorder: recorder))
        #expect(recorder.created == ["keeping Mac awake"])
    }

    @Test("releases the assertion it created when released explicitly")
    func releasesExplicitly() throws {
        let recorder = AssertionRecorder()
        let assertion = try PowerAssertion(reason: "test", environment: .fake(recorder: recorder))
        #expect(recorder.released.isEmpty)

        assertion.release()

        #expect(recorder.released == [AssertionHandle(rawValue: 1)])
    }

    @Test("releases the assertion when deallocated")
    func releasesOnDeinit() throws {
        let recorder = AssertionRecorder()
        var assertion: PowerAssertion? = try PowerAssertion(
            reason: "test", environment: .fake(recorder: recorder))
        #expect(recorder.released.isEmpty)

        assertion = nil

        #expect(assertion == nil)
        #expect(recorder.released == [AssertionHandle(rawValue: 1)])
    }

    @Test("does not double-release when explicitly released then deallocated")
    func doesNotDoubleRelease() throws {
        let recorder = AssertionRecorder()
        var assertion: PowerAssertion? = try PowerAssertion(
            reason: "test", environment: .fake(recorder: recorder))

        assertion?.release()
        assertion = nil

        #expect(recorder.released.count == 1)
    }

    @Test("repeated explicit release reaches the system only once")
    func repeatedReleaseIsIdempotent() throws {
        let recorder = AssertionRecorder()
        let assertion = try PowerAssertion(reason: "test", environment: .fake(recorder: recorder))

        assertion.release()
        assertion.release()
        assertion.release()

        #expect(recorder.released.count == 1)
    }

    @Test("propagates a creation failure instead of pretending to hold one")
    func propagatesCreationFailure() {
        let recorder = AssertionRecorder()
        recorder.failure = .creationFailed(-536_870_212)

        #expect(throws: AssertionError.creationFailed(-536_870_212)) {
            try PowerAssertion(reason: "test", environment: .fake(recorder: recorder))
        }
        #expect(recorder.released.isEmpty)
    }
}
