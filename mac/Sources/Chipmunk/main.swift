import AppKit

/// Chipmunk lives in the menu bar. Its icon shows how much is in the draft and a dot while
/// every screenshot is being collected, the same jobs Chip does on Windows.
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    var core: Core!
    var status: NSStatusItem!
    let menu = NSMenu()

    func applicationDidFinishLaunching(_ note: Notification) {
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--profile"), i + 1 < args.count { Settings.profileDir = URL(fileURLWithPath: args[i + 1]) }
        Log.info("Starting Chipmunk \(Core.version) for Mac")
        buildMainMenu()

        let uiDir = Bundle.main.resourceURL!.appendingPathComponent("ui")
        core = Core(uiDir: uiDir)
        core.onStateChange = { [weak self] in self?.refreshIcon() }

        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = status.button {
            button.image = AppDelegate.menuIcon()
            button.imagePosition = .imageLeading
            button.target = self
            button.action = #selector(statusClicked(_:))
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
            button.toolTip = "Chipmunk"
        }
        menu.delegate = self
        refreshIcon()
        if !args.contains("--background") || !core.settings.onboarded { core.showPanel() }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        core.showPanel()
        return true
    }

    @objc func statusClicked(_ sender: NSStatusBarButton) {
        // Click opens your draft; right-click (or Control-click) shows the menu.
        let e = NSApp.currentEvent
        if e?.type == .rightMouseUp || e?.modifierFlags.contains(.control) == true {
            status.menu = menu
            status.button?.performClick(nil)
            status.menu = nil
        } else {
            core.togglePanel()
        }
    }

    static func menuIcon() -> NSImage {
        if let url = Bundle.main.url(forResource: "MenuIcon", withExtension: "png"), let img = NSImage(contentsOf: url) {
            img.size = NSSize(width: 18, height: 18)
            img.isTemplate = true
            return img
        }
        let img = NSImage(systemSymbolName: "tray.and.arrow.down.fill", accessibilityDescription: "Chipmunk") ?? NSImage()
        img.isTemplate = true
        return img
    }

    func refreshIcon() {
        guard let button = status?.button else { return }
        let n = core.current.attachments.count
        var title = n > 0 ? " \(n)" : ""
        if core.collect { title += " ●" }
        button.title = title
        button.toolTip = core.collect ? "Chipmunk · collecting every screenshot" : "Chipmunk"
    }

    // MARK: the menu

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let d = core.current
        let words = Store.wordCount(core.store.readText(d))
        let title = d.title.isEmpty ? (words > 0 ? Store.firstLine(core.store.readText(d), 40) : "New draft") : d.title
        let card = NSMenuItem(title: title, action: #selector(openPanel), keyEquivalent: "")
        card.target = self
        let images = d.attachments.filter { $0.kind == "image" }.count
        var parts: [String] = []
        if images > 0 { parts.append(core.plural(images, "image")) }
        if d.attachments.count - images > 0 { parts.append(core.plural(d.attachments.count - images, "item")) }
        if words > 0 { parts.append(core.plural(words, "word")) }
        if #available(macOS 14.4, *) { card.subtitle = parts.isEmpty ? "Nothing in it yet" : parts.joined(separator: " · ") }
        menu.addItem(card)

        if let u = core.updateAvailable, let v = u["version"] as? String {
            let item = NSMenuItem(title: "Chipmunk \(v) is out", action: #selector(openUpdate), keyEquivalent: "")
            item.target = self
            menu.addItem(item)
        }
        menu.addItem(.separator())
        let head = NSMenuItem(title: "Send to", action: nil, keyEquivalent: "")
        head.isEnabled = false
        menu.addItem(head)
        let targets = core.targets()
        if targets.isEmpty {
            let none = NSMenuItem(title: "Open ChatGPT, Claude or a terminal", action: nil, keyEquivalent: "")
            none.isEnabled = false
            menu.addItem(none)
        }
        for t in targets.prefix(8) {
            let item = NSMenuItem(title: t.label + (t.label != t.appName ? " (\(t.appName))" : ""), action: #selector(sendTo(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = t.app
            if let icon = t.app.icon { icon.size = NSSize(width: 16, height: 16); item.image = icon }
            menu.addItem(item)
        }
        menu.addItem(.separator())
        add("Open Chipmunk", #selector(openPanel), hotkey: "toggle")
        add("Snip into draft", #selector(snip), hotkey: "snip")
        add("New draft", #selector(newDraft))
        let collect = add("Collect every screenshot", #selector(toggleCollect))
        collect.state = core.collect ? .on : .off
        menu.addItem(.separator())
        add("Settings…", #selector(openSettings))
        add("Quit Chipmunk", #selector(quit))
    }

    @discardableResult
    private func add(_ title: String, _ action: Selector, hotkey: String? = nil) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        if let h = hotkey, let combo = core.settings.hotkeys[h] { item.toolTip = combo }
        menu.addItem(item)
        return item
    }

    @objc func openPanel() { core.showPanel() }
    @objc func snip() { core.startSnip() }
    @objc func newDraft() { core.showPanel(); core.newDraftFromOutside() }
    @objc func toggleCollect() { core.setCollect(!core.collect) }
    @objc func openSettings() { core.showView("settings") }
    @objc func quit() { core.quit() }
    @objc func sendTo(_ sender: NSMenuItem) { if let app = sender.representedObject as? NSRunningApplication { core.sendCurrent(to: app) } }
    @objc func openUpdate() {
        if let u = core.updateAvailable?["url"] as? String, let url = URL(string: u) { NSWorkspace.shared.open(url) }
    }

    /// A menu bar app still needs an Edit menu, or ⌘C, ⌘V, ⌘X, ⌘A and ⌘Z don't work in the panel.
    private func buildMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        main.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Hide Chipmunk", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(withTitle: "Quit Chipmunk", action: #selector(quit), keyEquivalent: "q").target = self
        appItem.submenu = appMenu

        let editItem = NSMenuItem()
        main.addItem(editItem)
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        let redo = edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        NSApp.mainMenu = main
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
