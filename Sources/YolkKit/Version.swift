/// The single source of version truth for both products.
///
/// `make check-version` compares this against the Xcode project's
/// `MARKETING_VERSION` before `make release` builds or signs anything, so the
/// app bundle and the CLI can never ship disagreeing about what they are.
public enum YolkKit {
    public static let version = "1.0.1"
}
