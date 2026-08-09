import Foundation

/// Reads the display-sleep timeout so yolk can warn when the display would
/// sleep sooner than it would ever act.
public enum DisplaySleepProbe {
    /// Shells out to `pmset -g` and parses the result. Untestable glue by
    /// design — all the decidable behavior lives in `parse(pmsetOutput:)`.
    static func readFromPmset() -> TimeInterval? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/pmset")
        process.arguments = ["-g"]
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        guard (try? process.run()) != nil else { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard let output = String(data: data, encoding: .utf8) else { return nil }
        return parse(pmsetOutput: output)
    }

    /// Extracts the `displaysleep` value from `pmset -g` output, in seconds.
    /// Returns nil when display sleep is disabled (0) or the line is absent
    /// or unparseable.
    static func parse(pmsetOutput: String) -> TimeInterval? {
        for line in pmsetOutput.split(separator: "\n") {
            let parts = line.split(separator: " ", omittingEmptySubsequences: true)
            guard parts.first == "displaysleep", parts.count >= 2,
                  let minutes = Double(parts[1])
            else { continue }
            return minutes > 0 ? minutes * 60 : nil
        }
        return nil
    }
}
