import Foundation
import Testing

@testable import YolkKit

@Suite("DurationParser")
struct DurationParserTests {
    @Test(
        "parses suffixed and bare durations into seconds",
        arguments: [
            ("8h", 28_800.0),
            ("90m", 5_400.0),
            ("45s", 45.0),
            ("1d", 86_400.0),
            ("300", 300.0),
            ("1.5h", 5_400.0),
        ])
    func parsesDurations(input: String, expected: TimeInterval) {
        #expect(DurationParser.parse(input) == expected)
    }

    @Test("suffix matching is case-insensitive")
    func suffixIsCaseInsensitive() {
        #expect(DurationParser.parse("8H") == 28_800)
    }

    @Test(
        "rejects unparseable, non-positive, and non-finite input",
        arguments: [
            "",     // empty
            " ",    // whitespace only
            "0",    // zero
            "-5",   // negative
            "-2h",  // negative with suffix
            "abc",  // non-numeric
            "8x",   // unknown suffix
            "h",    // suffix with no number
            "inf",  // non-finite
            "nan",  // non-finite
        ])
    func rejectsInvalidInput(input: String) {
        #expect(DurationParser.parse(input) == nil)
    }
}
