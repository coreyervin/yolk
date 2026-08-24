import Foundation
import Testing

@testable import yolk

@Suite("Usage text")
struct UsageTests {
    /// `--help` is the most-seen output yolk has. Diffed against the golden
    /// captured from the pre-refactor binary, byte for byte.
    @Test("the help text matches the golden exactly")
    func helpMatchesGolden() throws {
        let golden = try GoldenFile.read("cli-help.txt")
        // The golden is a file and ends with a newline; `print` supplies that.
        #expect(Usage.text + "\n" == golden)
    }

    @Test("the version line matches the golden exactly")
    func versionMatchesGolden() throws {
        let golden = try GoldenFile.read("cli-version.txt")
        #expect(Usage.versionLine + "\n" == golden)
    }
}
