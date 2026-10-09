import Testing

@testable import YolkKit

@Suite("YolkKit version")
struct VersionTests {
    @Test("YolkKit owns the single source of version truth")
    func versionIsTheOneTruth() {
        #expect(YolkKit.version == "1.0.1")
    }
}
