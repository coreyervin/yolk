import Foundation

/// Where `AppModel` persists its settings.
///
/// A struct of closures, matching `SystemEnvironment`: the app wires it to
/// `UserDefaults`, tests use an in-memory dictionary, and nothing in between
/// needs a protocol witness.
public struct SettingsStore: Sendable {
    public var readDouble: @Sendable (String) -> Double?
    public var writeDouble: @Sendable (String, Double?) -> Void
    public var readString: @Sendable (String) -> String?
    public var writeString: @Sendable (String, String?) -> Void

    public init(
        readDouble: @escaping @Sendable (String) -> Double?,
        writeDouble: @escaping @Sendable (String, Double?) -> Void,
        readString: @escaping @Sendable (String) -> String?,
        writeString: @escaping @Sendable (String, String?) -> Void
    ) {
        self.readDouble = readDouble
        self.writeDouble = writeDouble
        self.readString = readString
        self.writeString = writeString
    }

    /// The app's real store.
    public static func standard(_ defaults: UserDefaults = .standard) -> SettingsStore {
        // UserDefaults is documented as thread-safe but is not marked Sendable,
        // so the guarantee has to be carried explicitly rather than assumed.
        let box = UncheckedBox(defaults)
        return SettingsStore(
            // `object(forKey:)` rather than `double(forKey:)` so an unset key
            // reads as nil instead of silently becoming 0.
            readDouble: { box.value.object(forKey: $0) as? Double },
            writeDouble: { key, value in box.value.set(value, forKey: key) },
            readString: { box.value.string(forKey: $0) },
            writeString: { key, value in box.value.set(value, forKey: key) })
    }

    private struct UncheckedBox<Wrapped>: @unchecked Sendable {
        let value: Wrapped
        init(_ value: Wrapped) { self.value = value }
    }

    /// An in-memory store, so tests never touch the real user's preferences.
    public static func ephemeral() -> SettingsStore {
        let box = Box()
        return SettingsStore(
            readDouble: { box.doubles[$0] },
            writeDouble: { key, value in box.doubles[key] = value },
            readString: { box.strings[$0] },
            writeString: { key, value in box.strings[key] = value })
    }

    private final class Box: @unchecked Sendable {
        var doubles: [String: Double] = [:]
        var strings: [String: String] = [:]
    }
}
