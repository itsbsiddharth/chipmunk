import Foundation
import AppKit
import ServiceManagement

/// Settings kept in ~/Library/Application Support/Chipmunk/settings.json.
final class Settings: Codable {
    var hotkeys: [String: String] = Settings.defaultHotkeys
    var pasteDelayMs = 450
    var shrinkImages = true
    var terminalPaths = true
    var restoreClipboard = true
    var collectText = false
    var alwaysOnTop = false
    var onboarded = false
    var theme = "system"
    var dataDir: String?
    var currentDraft: String?
    var bounds: [Double]?
    var autoUpdate = true

    /// Ctrl+Option shortcuts: few Mac apps use them, so they rarely clash.
    static let defaultHotkeys: [String: String] = [
        "toggle": "Ctrl+Option+P",
        "snip": "Ctrl+Option+S",
        "grab": "Ctrl+Option+C",
        "paste": "Ctrl+Option+V",
    ]

    init() {}

    required init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        hotkeys = try c.decodeIfPresent([String: String].self, forKey: .hotkeys) ?? Settings.defaultHotkeys
        pasteDelayMs = try c.decodeIfPresent(Int.self, forKey: .pasteDelayMs) ?? 450
        shrinkImages = try c.decodeIfPresent(Bool.self, forKey: .shrinkImages) ?? true
        terminalPaths = try c.decodeIfPresent(Bool.self, forKey: .terminalPaths) ?? true
        restoreClipboard = try c.decodeIfPresent(Bool.self, forKey: .restoreClipboard) ?? true
        collectText = try c.decodeIfPresent(Bool.self, forKey: .collectText) ?? false
        alwaysOnTop = try c.decodeIfPresent(Bool.self, forKey: .alwaysOnTop) ?? false
        onboarded = try c.decodeIfPresent(Bool.self, forKey: .onboarded) ?? false
        theme = try c.decodeIfPresent(String.self, forKey: .theme) ?? "system"
        dataDir = try c.decodeIfPresent(String.self, forKey: .dataDir)
        currentDraft = try c.decodeIfPresent(String.self, forKey: .currentDraft)
        bounds = try c.decodeIfPresent([Double].self, forKey: .bounds)
        autoUpdate = try c.decodeIfPresent(Bool.self, forKey: .autoUpdate) ?? true
    }

    static var profileDir: URL?

    static var appSupport: URL {
        if let p = profileDir { return p }
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("Chipmunk")
    }

    private static var file: URL { appSupport.appendingPathComponent("settings.json") }

    static func load() -> Settings {
        var s: Settings? = nil
        if let data = try? Data(contentsOf: file) { s = try? JSONDecoder().decode(Settings.self, from: data) }
        let settings = s ?? Settings()
        for (k, v) in defaultHotkeys where settings.hotkeys[k] == nil { settings.hotkeys[k] = v }
        if settings.dataDir == nil || settings.dataDir!.isEmpty {
            if let p = profileDir { settings.dataDir = p.appendingPathComponent("data").path }
            else {
                let docs = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
                settings.dataDir = docs.appendingPathComponent("Chipmunk").path
            }
        }
        settings.pasteDelayMs = max(120, min(3000, settings.pasteDelayMs))
        return settings
    }

    func save() {
        try? FileManager.default.createDirectory(at: Settings.appSupport, withIntermediateDirectories: true)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? enc.encode(self) { try? data.write(to: Settings.file, options: .atomic) }
    }

    var openAtLogin: Bool {
        get { SMAppService.mainApp.status == .enabled }
        set {
            do {
                if newValue { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            } catch { Log.error("Open at login: \(error.localizedDescription)") }
        }
    }

    var darkMode: Bool {
        if theme == "dark" { return true }
        if theme == "light" { return false }
        return NSApp.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
    }
}

enum Log {
    static let file: URL = Settings.appSupport.appendingPathComponent("chipmunk.log")

    static func info(_ s: String) { write("INFO " + s) }
    static func error(_ s: String) { write("ERROR " + s) }

    private static func write(_ line: String) {
        let text = ISO8601DateFormatter().string(from: Date()) + " " + line + "\n"
        try? FileManager.default.createDirectory(at: Settings.appSupport, withIntermediateDirectories: true)
        if let h = try? FileHandle(forWritingTo: file) {
            h.seekToEndOfFile(); h.write(Data(text.utf8)); try? h.close()
        } else {
            try? Data(text.utf8).write(to: file)
        }
    }
}
