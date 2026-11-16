import AppKit
import ApplicationServices

/// An app you can send to.
struct Target {
    let app: NSRunningApplication
    let label: String
    let ai: Bool
    let terminal: Bool

    var pid: pid_t { app.processIdentifier }
    var appName: String { app.localizedName ?? label }
}

/// Knows which open apps are AI apps and terminals, and pastes a draft into one of them.
enum Apps {
    static let aiApps: [String: String] = [
        "com.openai.chat": "ChatGPT", "com.anthropic.claudefordesktop": "Claude", "com.openai.codex": "Codex",
        "com.todesktop.230313mzl4w4u92": "Cursor", "com.exafunction.windsurf": "Windsurf", "com.google.antigravity": "Antigravity",
        "com.microsoft.VSCode": "VS Code", "dev.zed.Zed": "Zed", "ai.perplexity.mac": "Perplexity", "com.microsoft.copilot": "Copilot",
    ]
    static let aiNames = ["ChatGPT", "Claude", "Codex", "Cursor", "Windsurf", "Antigravity", "Zed", "Kiro", "Perplexity", "Copilot"]
    static let terminals: Set<String> = [
        "com.apple.Terminal", "com.googlecode.iterm2", "dev.warp.Warp-Stable", "com.mitchellh.ghostty", "net.kovidgoyal.kitty",
        "io.alacritty", "co.zeit.hyper", "com.github.wez.wezterm",
    ]
    static let dictationApps = ["Wispr Flow", "Superwhisper", "Granola", "Aqua", "Willow", "VoiceInk", "Spokenly", "MacWhisper"]

    static func describe(_ app: NSRunningApplication) -> Target {
        let bundle = app.bundleIdentifier ?? ""
        let name = app.localizedName ?? "App"
        let terminal = terminals.contains(bundle)
        if let known = aiApps[bundle] { return Target(app: app, label: known, ai: true, terminal: false) }
        if let hit = aiNames.first(where: { name.localizedCaseInsensitiveContains($0) }) { return Target(app: app, label: hit, ai: true, terminal: false) }
        // A terminal is where Claude Code and Codex run, so it counts as an AI target.
        return Target(app: app, label: name, ai: terminal, terminal: terminal)
    }

    static func runningDictationApp() -> String? {
        let names = NSWorkspace.shared.runningApplications.compactMap { $0.localizedName }
        return dictationApps.first { d in names.contains { $0.localizedCaseInsensitiveContains(d) } }
    }
}

/// Pastes a draft into another app: each image, then files, then the text. Needs the
/// Accessibility permission to press ⌘V there.
enum Paster {
    struct Job {
        var target: NSRunningApplication
        var images: [Data] = []
        var files: [URL] = []
        var text = ""
        var delayMs = 450
        var restoreClipboard = true
    }

    static var trusted: Bool { AXIsProcessTrusted() }

    static func askForAccessibility() {
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(opts)
    }

    /// Returns nil on success, or why it stopped.
    static func run(_ job: Job, clipboard: ClipboardWatcher) -> String? {
        guard trusted else {
            askForAccessibility()
            return "Chipmunk needs permission to paste. Turn on Chipmunk in System Settings > Privacy & Security > Accessibility, then send again."
        }
        let saved = job.restoreClipboard ? clipboard.snapshot() : nil
        clipboard.suppress = true
        defer {
            if let s = saved { clipboard.restore(s) }
            clipboard.suppress = false
            clipboard.markOwnChange()
        }
        if !bringToFront(job.target) { return "Couldn't switch to \(job.target.localizedName ?? "that app"). Click into it and try again." }
        wait(150)
        let pb = NSPasteboard.general
        for png in job.images {
            if NSWorkspace.shared.frontmostApplication?.processIdentifier != job.target.processIdentifier { return "Stopped: you switched apps while pasting." }
            pb.clearContents()
            pb.setData(png, forType: .png)
            if let tiff = NSImage(data: png)?.tiffRepresentation { pb.setData(tiff, forType: .tiff) }
            clipboard.markOwnChange()
            pressCommandV()
            wait(job.delayMs)
        }
        if !job.files.isEmpty {
            if NSWorkspace.shared.frontmostApplication?.processIdentifier != job.target.processIdentifier { return "Stopped: you switched apps while pasting." }
            pb.clearContents()
            pb.writeObjects(job.files as [NSURL])
            clipboard.markOwnChange()
            pressCommandV()
            wait(job.delayMs + 150)
        }
        if !job.text.isEmpty {
            if NSWorkspace.shared.frontmostApplication?.processIdentifier != job.target.processIdentifier { return "Stopped: you switched apps while pasting." }
            pb.clearContents()
            pb.setString(job.text, forType: .string)
            clipboard.markOwnChange()
            pressCommandV()
            wait(max(250, job.delayMs))
        }
        return nil
    }

    static func bringToFront(_ app: NSRunningApplication) -> Bool {
        if NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier { return true }
        app.activate(options: [.activateAllWindows])
        for _ in 0..<30 {
            wait(50)
            if NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier { return true }
        }
        return false
    }

    static func wait(_ ms: Int) {
        // Let the run loop breathe so the target app and our own UI keep working while we wait.
        RunLoop.current.run(until: Date().addingTimeInterval(Double(ms) / 1000))
    }

    static func pressCommandV() { press(key: 9 /* kVK_ANSI_V */, flags: .maskCommand) }
    static func pressCommandC() { press(key: 8 /* kVK_ANSI_C */, flags: .maskCommand) }

    static func press(key: CGKeyCode, flags: CGEventFlags) {
        let src = CGEventSource(stateID: .combinedSessionState)
        let down = CGEvent(keyboardEventSource: src, virtualKey: key, keyDown: true)
        let up = CGEvent(keyboardEventSource: src, virtualKey: key, keyDown: false)
        down?.flags = flags
        up?.flags = flags
        down?.post(tap: .cghidEventTap)
        up?.post(tap: .cghidEventTap)
    }

    /// Waits for the user to let go of the shortcut keys, so ⌘V isn't seen as ⌃⌥⌘V.
    static func waitModifiersUp(_ ms: Int) {
        let until = Date().addingTimeInterval(Double(ms) / 1000)
        while Date() < until {
            let f = CGEventSource.flagsState(.combinedSessionState)
            if !f.contains(.maskControl) && !f.contains(.maskAlternate) && !f.contains(.maskCommand) && !f.contains(.maskShift) { return }
            wait(20)
        }
    }
}
