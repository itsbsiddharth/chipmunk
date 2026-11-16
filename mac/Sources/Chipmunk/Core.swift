import AppKit
import UserNotifications

/// Everything Chipmunk does on a Mac. Mirrors the Windows AppCore: the panel sends the same
/// commands and gets the same shapes back, so one UI serves both.
final class Core: NSObject {
    static let version = "1.4.0"

    let settings: Settings
    let store: Store
    let clipboard = ClipboardWatcher()
    let screenshots = ScreenshotWatcher()
    var panel: PanelWindow!
    var hotkeys: Hotkeys!
    var onStateChange: (() -> Void)?

    private(set) var collect = false
    private var panelActiveAt = Date.distantPast
    private var recent: [pid_t] = []              // most recently used apps first
    private var lastTarget: pid_t?
    private var pendingOffer: (() -> Void)?
    private var pendingOfferId = 0
    private var offerSeq = 1
    private var lastImageOffer = Date.distantPast
    private var update: [String: Any]? = nil
    private let myPid = ProcessInfo.processInfo.processIdentifier

    init(uiDir: URL) {
        settings = Settings.load()
        store = Store(root: URL(fileURLWithPath: settings.dataDir!))
        super.init()
        if store.count == 0 { settings.currentDraft = store.create("").id }
        if !store.exists(settings.currentDraft) { settings.currentDraft = mostRecent().id }
        settings.save()

        panel = PanelWindow(core: self, uiDir: uiDir, filesRoot: store.root)
        hotkeys = Hotkeys { [weak self] name in self?.onHotkey(name) }
        let errors = hotkeys.register(settings.hotkeys)
        for (k, v) in errors { Log.error("Hotkey \(k): \(v)") }

        clipboard.onChange = { [weak self] in self?.onClipboard() }
        clipboard.start()
        screenshots.onScreenshot = { [weak self] url in self?.onScreenshotFile(url) }
        screenshots.start()

        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] note in
            guard let self = self, let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication else { return }
            if app.processIdentifier == self.myPid { return }
            self.noteRecent(app.processIdentifier)
            self.lastTarget = app.processIdentifier
            if self.panel.isVisible { self.panel.emit("target", self.targetDto(self.defaultTarget())) }
        }
        if settings.autoUpdate { DispatchQueue.main.asyncAfter(deadline: .now() + 20) { [weak self] in self?.checkForUpdate(nil) } }
    }

    var current: Draft { (try? store.get(settings.currentDraft)) ?? mostRecent() }

    private func mostRecent() -> Draft {
        store.all.sorted { $0.updated > $1.updated }.first ?? store.create("")
    }

    // MARK: panel

    var panelIsKey: Bool { panel.isVisible && panel.isKeyWindow && NSApp.isActive }

    func togglePanel() { if panelIsKey { hidePanel() } else { showPanel() } }

    func showPanel() {
        if let fg = NSWorkspace.shared.frontmostApplication, fg.processIdentifier != myPid { lastTarget = fg.processIdentifier; noteRecent(fg.processIdentifier) }
        if !panel.isVisible { startFreshIfSent() }
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        panelActiveAt = Date()
        panel.emit("target", targetDto(defaultTarget()))
        panel.focusPage()
    }

    func hidePanel() {
        panel.orderOut(nil)
        // Hand focus back to the app you were in.
        if let pid = lastTarget, let app = NSRunningApplication(processIdentifier: pid) { app.activate(options: []) }
    }

    func panelBecameKey() { panelActiveAt = Date() }

    func showView(_ v: String) { showPanel(); panel.emit("showView", v) }

    /// Like a chat box: once a draft was sent, opening the panel gives you a clean one.
    private func startFreshIfSent() {
        let cur = current
        guard cur.status == "sent", let last = cur.sends.last, let at = Store.parseDate(last.at), Date().timeIntervalSince(at) > 20 else { return }
        let blank = store.all.sorted { $0.updated > $1.updated }.prefix(12).first {
            $0.id != cur.id && $0.status != "sent" && $0.attachments.isEmpty && $0.checklist.isEmpty
                && $0.title.trimmingCharacters(in: .whitespaces).isEmpty && store.readText($0).trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }
        let d = blank ?? store.create("")
        settings.currentDraft = d.id
        settings.save()
        panel.emit("freshDraft", ["draft": draftDto(d), "previousId": cur.id, "previousTitle": cur.title])
        onStateChange?()
    }

    /// You're putting a draft together right now: the panel is on screen and you used it in the
    /// last few minutes. A panel left open behind other windows doesn't count.
    private func composing() -> Bool {
        guard panel.isVisible, !panel.isMiniaturized else { return false }
        return panelIsKey || Date().timeIntervalSince(panelActiveAt) < 180
    }

    // MARK: hotkeys

    private func onHotkey(_ name: String) {
        switch name {
        case "toggle": togglePanel()
        case "snip": startSnip()
        case "grab": startGrab()
        case "paste": pasteHotkey()
        default: break
        }
    }

    func startSnip() {
        let draftId = settings.currentDraft
        let wasVisible = panel.isVisible
        if wasVisible { panel.orderOut(nil) }
        Snipper.run { [weak self] png in
            guard let self = self else { return }
            if wasVisible { self.showPanel() }
            guard let png = png, let d = try? self.store.get(draftId) else { return }
            self.clipboard.markOwnChange()
            if let a = self.store.addImage(d, png, mediaType: "image/png", name: Store.screenshotName(), source: "Snip", linkItem: nil) {
                self.collected(d)
                self.announceCapture(d, a)
            } else { self.notify("That screenshot is already in this draft.") }
        }
    }

    private var grabUntil: Date?
    private var grabDraft: String?

    private func startGrab() {
        guard let fg = NSWorkspace.shared.frontmostApplication, fg.processIdentifier != myPid else {
            notify("Highlight something in another app, then press \(settings.hotkeys["grab"] ?? "the grab shortcut").")
            return
        }
        guard Paster.trusted else {
            Paster.askForAccessibility()
            notify("Chipmunk needs permission to copy your selection. Turn it on in System Settings > Privacy & Security > Accessibility.", error: true)
            return
        }
        Paster.waitModifiersUp(1500)
        grabDraft = settings.currentDraft
        grabUntil = Date().addingTimeInterval(1.2)
        let before = NSPasteboard.general.changeCount
        Paster.pressCommandC()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.3) { [weak self] in
            guard let self = self, self.grabUntil != nil else { return }
            if NSPasteboard.general.changeCount == before {
                self.grabUntil = nil
                self.notify("Nothing was selected. Highlight something first, then press \(self.settings.hotkeys["grab"] ?? "the grab shortcut").")
            }
        }
    }

    private func pasteHotkey() {
        guard let fg = NSWorkspace.shared.frontmostApplication else { return }
        if fg.processIdentifier == myPid { _ = try? sendFromPanel(current, chosen: nil); return }
        Paster.waitModifiersUp(1500)
        if let error = pasteDraft(current, into: fg) { notify(error, error: true) }
    }

    // MARK: capture

    private func onClipboard() {
        if let until = grabUntil, Date() < until, let id = grabDraft, let d = try? store.get(id) {
            grabUntil = nil
            let msg = addContent(d, ClipboardWatcher.read(), source: frontAppName())
            collected(d)
            notify(msg ?? "Nothing to add from that selection.")
            return
        }
        let auto = collect || composing()
        if !auto {
            if ClipboardWatcher.hasImage { offerImage(ClipboardWatcher.read().imagePng, shot: false) }
            return
        }
        let c = ClipboardWatcher.read()
        let d = current
        if let png = c.imagePng, c.files.isEmpty {
            guard let a = store.addImage(d, png, mediaType: "image/png", name: Store.screenshotName(), source: "Copied · " + frontAppName(), linkItem: nil) else { return }
            if !collect { panelActiveAt = Date() }
            collected(d)
            announceCapture(d, a)
        } else if collect && settings.collectText, let t = c.text, !t.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, c.files.isEmpty {
            let added: Attachment? = (try? store.addText(d, t, source: frontAppName(), url: nil, linkItem: nil)) ?? nil
            guard added != nil else { return }
            collected(d)
            notify("Collected copied text into \(quote(d.title))")
        }
    }

    /// A screenshot file appeared (⌘⇧3/4/5 saves to the Desktop by default). The file stays
    /// where macOS put it; Chipmunk keeps its own copy in the draft.
    private func onScreenshotFile(_ url: URL) {
        guard let data = try? Data(contentsOf: url) else { return }
        if collect || composing() {
            let d = current
            guard let a = store.addImage(d, data, mediaType: "image/png", name: Store.screenshotName(), source: "Screenshot · " + frontAppName(), linkItem: nil) else { return }
            if !collect { panelActiveAt = Date() }
            collected(d)
            announceCapture(d, a)
        } else {
            offerImage(data, shot: true)
        }
    }

    /// Nobody asked for this one to be collected: ask in the open panel, never assume.
    private func offerImage(_ png: Data?, shot: Bool) {
        guard let png = png, panel.isVisible, !panelIsKey, Date().timeIntervalSince(lastImageOffer) > 4 else { return }
        lastImageOffer = Date()
        let d = current
        let draftId = d.id
        let source = (shot ? "Screenshot" : "Copied") + " · " + frontAppName()
        pendingOffer = { [weak self] in
            guard let self = self else { return }
            let target = (try? self.store.get(draftId)) ?? self.current
            guard let a = self.store.addImage(target, png, mediaType: "image/png", name: Store.screenshotName(), source: source, linkItem: nil) else {
                self.notify("Already have that one."); return
            }
            self.panelActiveAt = Date()
            self.collected(target)
            self.announceCapture(target, a)
        }
        pendingOfferId = offerSeq
        offerSeq += 1
        panel.emit("offer", ["text": (shot ? "Add this screenshot" : "Add this image") + " to \(quote(d.title))?", "label": "Add it", "id": pendingOfferId])
    }

    private func announceCapture(_ d: Draft, _ a: Attachment) {
        let images = d.attachments.filter { $0.include && $0.kind == "image" }
        let n = (images.firstIndex { $0.id == a.id } ?? -1) + 1
        let line = (n > 0 ? "Image \(n)" : "Image") + " added to \(quote(d.title))."
        if panelIsKey || panel.isVisible { panel.emit("captured", ["text": line, "attId": a.id, "draftId": d.id]) }
        else { notify(line) }
    }

    private func addContent(_ d: Draft, _ c: ClipContent, source: String) -> String? {
        if !c.files.isEmpty {
            var added = 0, skipped = 0
            for f in c.files {
                var isDir: ObjCBool = false
                if FileManager.default.fileExists(atPath: f.path, isDirectory: &isDir), isDir.boolValue { skipped += 1; continue }
                if ((try? store.addFile(d, f, source: source, linkItem: nil)) ?? nil) != nil { added += 1 }
            }
            return added > 0 ? "Added \(plural(added, "file")) to \(quote(d.title))" + (skipped > 0 ? " (folders are skipped)" : "")
                : skipped > 0 ? "Folders can't be attached. Copy the files inside instead." : "Those files are already in this draft."
        }
        if let png = c.imagePng {
            return store.addImage(d, png, mediaType: "image/png", name: "Copied image", source: source, linkItem: nil) == nil
                ? "That image is already in this draft." : "Added the image to \(quote(d.title))"
        }
        if let t = c.text?.trimmingCharacters(in: .whitespacesAndNewlines), !t.isEmpty {
            let isUrl = !t.contains(" ") && !t.contains("\n") && t.hasPrefix("http") && URL(string: t) != nil
            let a: Attachment? = isUrl ? store.addLink(d, t, title: nil, linkItem: nil) : ((try? store.addText(d, t, source: source, url: nil, linkItem: nil)) ?? nil)
            return a == nil ? "That's already in this draft." : (isUrl ? "Added the link to " : "Added the quote to ") + quote(d.title)
        }
        return nil
    }

    func setCollect(_ on: Bool) {
        collect = on
        panel.emit("collect", on)
        onStateChange?()
    }

    private func collected(_ d: Draft) {
        pushDraft(d)
    }

    private func pushDraft(_ d: Draft, textChanged: Bool = false) {
        if d.id == settings.currentDraft { panel.emit("draftChanged", draftDto(d, textChanged: textChanged)) }
        onStateChange?()
    }

    // MARK: notifications

    func notify(_ message: String, error: Bool = false) {
        if panel.isVisible {
            panel.emit("toast", ["text": message, "kind": error ? "error" : NSNull()])
            return
        }
        let center = UNUserNotificationCenter.current()
        center.requestAuthorization(options: [.alert]) { granted, _ in
            guard granted else { return }
            let content = UNMutableNotificationContent()
            content.title = "Chipmunk"
            content.body = message
            center.add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
        }
    }

    // MARK: targets and sending

    private func noteRecent(_ pid: pid_t) {
        recent.removeAll { $0 == pid }
        recent.insert(pid, at: 0)
        if recent.count > 30 { recent.removeLast() }
    }

    private func frontAppName() -> String {
        if let pid = lastTarget, let app = NSRunningApplication(processIdentifier: pid) { return app.localizedName ?? "an app" }
        return "an app"
    }

    func targets() -> [Target] {
        let apps = NSWorkspace.shared.runningApplications.filter { $0.activationPolicy == .regular && $0.processIdentifier != myPid && !$0.isTerminated }
        let rank: (NSRunningApplication) -> Int = { app in self.recent.firstIndex(of: app.processIdentifier) ?? 1000 }
        let described = apps.map { Apps.describe($0) }
        let ai = described.filter { $0.ai }.sorted { rank($0.app) < rank($1.app) }
        let other = described.filter { !$0.ai && rank($0.app) < 1000 }.sorted { rank($0.app) < rank($1.app) }.prefix(6)
        return ai + other
    }

    private func defaultTarget() -> NSRunningApplication? {
        for pid in recent {
            if let app = NSRunningApplication(processIdentifier: pid), !app.isTerminated, Apps.describe(app).ai { return app }
        }
        if let pid = lastTarget { return NSRunningApplication(processIdentifier: pid) }
        return nil
    }

    private func targetDto(_ app: NSRunningApplication?) -> Any {
        guard let app = app, !app.isTerminated else { return NSNull() }
        return dto(Apps.describe(app))
    }

    private func dto(_ t: Target) -> [String: Any] {
        ["hwnd": String(t.pid), "label": t.label, "app": t.appName, "title": t.appName, "terminal": t.terminal, "ai": t.ai]
    }

    func sendFromPanel(_ d: Draft, chosen: NSRunningApplication?) throws -> [String: Any] {
        guard let target = chosen ?? defaultTarget(), !target.isTerminated else {
            throw ChipError("Open the AI app you want to send to, or click into it and press \(settings.hotkeys["paste"] ?? "the send shortcut").")
        }
        noteRecent(target.processIdentifier)
        panel.orderOut(nil)
        let error = pasteDraft(d, into: target)
        if let e = error { notify(e, error: true) }
        return ["draft": draftDto(d), "message": error ?? NSNull()]
    }

    func sendCurrent(to app: NSRunningApplication) {
        do { _ = try sendFromPanel(current, chosen: app) } catch { notify(error.localizedDescription, error: true) }
    }

    /// Pastes the draft into an app. Returns nil on success, or an error message.
    private func pasteDraft(_ d: Draft, into target: NSRunningApplication) -> String? {
        let text = store.readText(d)
        let t = Apps.describe(target)
        let asPaths = t.terminal && settings.terminalPaths
        let atts = d.attachments.filter { $0.include }
        var job = Paster.Job(target: target)
        job.delayMs = settings.pasteDelayMs
        job.restoreClipboard = settings.restoreClipboard
        job.text = Composer.build(prompt: text, attachments: atts, pathOf: { self.store.filePath(d, $0) }, withPaths: asPaths)
        if !asPaths {
            for a in atts where a.kind == "image" {
                if let p = store.filePath(d, a), let data = try? Data(contentsOf: p) { job.images.append(ImageUtil.pngForPaste(data, shrink: settings.shrinkImages)) }
            }
            for a in atts where a.kind == "file" {
                if let p = store.filePath(d, a), FileManager.default.fileExists(atPath: p.path) { job.files.append(p) }
            }
        }
        if job.images.isEmpty && job.files.isEmpty && job.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return "This draft is empty, so there's nothing to send."
        }
        if let error = Paster.run(job, clipboard: clipboard) { return error }

        let stopCollect = collect
        if stopCollect { setCollect(false) }
        d.status = "sent"
        d.sends.append(SendRecord(at: Store.now(), target: t.appName, items: job.images.count + job.files.count))
        store.touch(d)
        pushDraft(d)
        var parts: [String] = []
        if !job.images.isEmpty { parts.append(plural(job.images.count, "image")) }
        if !job.files.isEmpty { parts.append(plural(job.files.count, "file")) }
        if !job.text.isEmpty { parts.append(asPaths && atts.contains { $0.kind == "image" || $0.kind == "file" } ? "your text with file paths" : "your text") }
        notify("Sent " + parts.joined(separator: " + ") + " to \(t.appName)." + (stopCollect ? " Stopped collecting screenshots." : ""))
        return nil
    }

    // MARK: DTOs

    private func fileUrl(_ d: Draft, _ a: Attachment) -> String {
        let parts = ["drafts", d.id] + (a.file ?? "").split(separator: "/").map(String.init)
        return PanelWindow.filesHost + parts.map { $0.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/"))) ?? $0 }.joined(separator: "/")
    }

    private func attDto(_ d: Draft, _ a: Attachment) -> [String: Any] {
        var x: [String: Any] = [
            "id": a.id, "kind": a.kind, "name": a.name, "source": a.source ?? NSNull(), "include": a.include, "note": a.note ?? NSNull(),
            "w": a.w, "h": a.h, "bytes": NSNumber(value: a.bytes), "ext": a.ext ?? NSNull(), "text": a.text ?? NSNull(), "linked": a.linkPath != nil,
        ]
        let url: Any
        if a.kind == "link" || a.kind == "text" { url = a.url as Any? ?? NSNull() }
        else if a.file != nil { url = fileUrl(d, a) }
        else { url = NSNull() }
        x["url"] = url
        if let p = store.filePath(d, a) {
            x["path"] = p.path
            if a.linkPath != nil && !FileManager.default.fileExists(atPath: p.path) { x["missing"] = true }
        }
        return x
    }

    func draftDto(_ d: Draft, textChanged: Bool = false) -> [String: Any] {
        [
            "id": d.id, "title": d.title, "status": d.status, "pinned": d.pinned, "imported": d.imported,
            "created": d.created, "updated": d.updated, "armedItem": d.armedItem ?? NSNull(), "text": store.readText(d),
            "folder": store.dir(d).path, "textChanged": textChanged,
            "checklist": d.checklist.map { ["id": $0.id, "text": $0.text, "done": $0.done, "attId": $0.attId ?? NSNull()] as [String: Any] },
            "attachments": d.attachments.map { attDto(d, $0) },
            "sends": d.sends.map { ["at": $0.at, "target": $0.target] },
        ]
    }

    private func draftList() -> [[String: Any]] {
        store.all.sorted { $0.updated > $1.updated }.map { d in
            let images = d.attachments.filter { $0.kind == "image" && $0.file != nil }
            return [
                "id": d.id, "title": d.title, "status": d.status, "pinned": d.pinned, "updated": d.updated,
                "preview": Store.firstLine(store.readText(d), 120), "imageCount": images.count,
                "attachmentCount": d.attachments.count, "imported": d.imported,
                "thumbs": images.prefix(3).map { fileUrl(d, $0) },
            ]
        }
    }

    private func settingsDto(_ hotkeyErrors: [String: String] = [:]) -> [String: Any] {
        let dictation = Apps.runningDictationApp()
        return [
            "platform": "mac",
            "features": ["history": false, "pet": false, "extension": false, "agents": false],
            "hotkeys": settings.hotkeys, "pasteDelayMs": settings.pasteDelayMs, "shrinkImages": settings.shrinkImages,
            "terminalPaths": settings.terminalPaths, "restoreClipboard": settings.restoreClipboard, "collectText": settings.collectText,
            "alwaysOnTop": settings.alwaysOnTop, "onboarded": settings.onboarded, "theme": settings.theme,
            "showPet": false, "petHints": false,
            "dictation": (dictation != nil ? ["app": dictation!, "hint": "use its shortcut"] : ["app": NSNull(), "hint": "Fn Fn"]) as [String: Any],
            "dictationApp": dictation ?? NSNull(), "usedExtension": false,
            "startWithWindows": settings.openAtLogin, "dataDir": store.root.path, "version": Core.version,
            "autoUpdate": settings.autoUpdate, "installed": true, "update": update ?? NSNull(),
            "hotkeyErrors": hotkeyErrors,
        ]
    }

    // MARK: commands from the panel

    func handle(_ cmd: String, _ a: [String: Any], reply: @escaping (Any?, String?) -> Void) {
        do {
            if cmd == "checkUpdate" { checkForUpdate { r in reply(r, nil) }; return }
            if cmd == "applyUpdate" {
                if let u = update?["url"] as? String, let url = URL(string: u) { NSWorkspace.shared.open(url) }
                reply(false, nil); return
            }
            let r = try handleCore(cmd, a)
            reply(r, nil)
            onStateChange?()
        } catch {
            Log.error("\(cmd): \(error.localizedDescription)")
            reply(nil, error.localizedDescription)
        }
    }

    private func handleCore(_ cmd: String, _ a: [String: Any]) throws -> Any? {
        let d: Draft? = Json.has(a, "id") && cmd != "undeleteDraft" ? try store.get(Json.str(a, "id")) : nil
        func need() throws -> Draft { guard let d = d else { throw ChipError("That draft no longer exists.") }; return d }
        switch cmd {
        case "init":
            Log.info("Panel loaded the UI")
            return ["settings": settingsDto(), "drafts": draftList(), "current": draftDto(current), "target": targetDto(defaultTarget()), "collect": collect]
        case "listDrafts": return draftList()
        case "getDraft": return draftDto(try need())
        case "setCurrent":
            settings.currentDraft = try need().id; settings.save(); return true
        case "saveText":
            let dr = try need()
            store.saveText(dr, Json.str(a, "text") ?? "")
            return ["updated": dr.updated]
        case "updateDraft": return updateDraft(try need(), Json.obj(a, "patch"))
        case "armItem":
            let dr = try need(); dr.armedItem = Json.str(a, "itemId"); store.saveMeta(dr); return draftDto(dr)
        case "updateAttachment":
            let dr = try need()
            let patch = Json.obj(a, "patch")
            if let att = dr.attachments.first(where: { $0.id == Json.str(a, "attId") }) {
                if Json.has(patch, "include") { att.include = Json.bool(patch, "include") }
                if Json.has(patch, "name") { att.name = Json.str(patch, "name") ?? att.name }
                if Json.has(patch, "note") {
                    let n = (Json.str(patch, "note") ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                    att.note = n.isEmpty ? nil : n
                }
            }
            store.saveMeta(dr)
            return draftDto(dr)
        case "removeAttachment":
            let dr = try need(); store.removeAttachment(dr, Json.str(a, "attId") ?? ""); return draftDto(dr)
        case "restoreAttachment":
            let dr = try need(); store.restoreAttachment(dr, Json.str(a, "attId") ?? ""); return draftDto(dr)
        case "openAttachment":
            let dr = try need()
            guard let att = dr.attachments.first(where: { $0.id == Json.str(a, "attId") }) else { return false }
            if att.kind == "link", let u = att.url, let url = URL(string: u) { NSWorkspace.shared.open(url); return true }
            guard let p = store.filePath(dr, att), FileManager.default.fileExists(atPath: p.path) else { throw ChipError("That file isn't there any more.") }
            NSWorkspace.shared.open(p)
            return true
        case "revealDraft": NSWorkspace.shared.activateFileViewerSelecting([store.dir(try need())]); return true
        case "openDataFolder": NSWorkspace.shared.open(store.root); return true
        case "snip": startSnip(); return true
        case "targets": return targets().map { dto($0) }
        case "dictation": return settingsDto()["dictation"]
        case "historySources": return [["id": "none", "name": "Past chats", "count": 0, "local": true]]
        case "historySessions": return [Any]()
        case "pickFiles":
            let dr = try need()
            let open = NSOpenPanel()
            open.allowsMultipleSelection = true
            open.canChooseDirectories = false
            open.title = "Add files to " + (dr.title.isEmpty ? "this draft" : dr.title)
            NSApp.activate(ignoringOtherApps: true)
            guard open.runModal() == .OK else { return NSNull() }
            for u in open.urls { _ = try? store.addFile(dr, u, source: "Chosen", linkItem: nil) }
            return draftDto(dr)
        case "addClipboard":
            let dr = try need()
            let msg = addContent(dr, ClipboardWatcher.read(), source: "Clipboard")
            return ["draft": draftDto(dr), "message": msg ?? "Your clipboard is empty."]
        case "addLink":
            let dr = try need()
            if store.addLink(dr, Json.str(a, "url") ?? "", title: Json.str(a, "title"), linkItem: Json.str(a, "linkItem")) == nil { notify("That link is already in this draft.") }
            return draftDto(dr)
        case "addText":
            let dr = try need()
            if try store.addText(dr, Json.str(a, "text") ?? "", source: Json.str(a, "source"), url: nil, linkItem: Json.str(a, "linkItem")) == nil { notify("That text is already in this draft.") }
            return draftDto(dr)
        case "addFiles":
            // Files dropped on the page have no path in WKWebView, so the page sends their bytes next (addData).
            let names = (a["names"] as? [Any]) ?? []
            return ["draft": draftDto(try need()), "missing": Array(0..<names.count)]
        case "addData": return try addData(try need(), Json.str(a, "name"), Json.str(a, "data"), Json.str(a, "linkItem"))
        case "newDraft":
            let n = Json.str(a, "duplicateOf").flatMap { try? store.get($0) }.map { store.duplicate($0) } ?? store.create("")
            settings.currentDraft = n.id; settings.save()
            return draftDto(n)
        case "deleteDraft":
            try store.delete(try need())
            if store.count == 0 { store.create("") }
            if !store.exists(settings.currentDraft) { settings.currentDraft = mostRecent().id }
            settings.save()
            return ["currentId": settings.currentDraft ?? ""]
        case "undeleteDraft":
            let r = try store.undelete(Json.str(a, "id") ?? "")
            settings.currentDraft = r.id; settings.save()
            return draftDto(r)
        case "versions": return store.versions(try need())
        case "getVersion": return try store.readVersion(try need(), Json.str(a, "name") ?? "")
        case "restoreVersion":
            let dr = try need(); try store.restoreVersion(dr, Json.str(a, "name") ?? ""); return draftDto(dr, textChanged: true)
        case "setSettings": return applySettings(Json.obj(a, "patch"))
        case "setCollect": setCollect(Json.bool(a, "on")); return true
        case "acceptOffer":
            guard let act = pendingOffer, Json.int(a, "offerId") == pendingOfferId else { return false }
            pendingOffer = nil
            act()
            return true
        case "send": return try send(try need(), Json.str(a, "how") ?? "paste", Json.str(a, "hwnd"))
        case "storageInfo": return storageInfo()
        case "agentInfo", "browserInfo": return [String: Any]()
        case "copyToClipboard": clipboard.setText(Json.str(a, "text") ?? ""); return true
        case "suspendHotkeys":
            hotkeys.suspended = Json.bool(a, "on")
            _ = hotkeys.register(settings.hotkeys)
            return true
        case "hide": hidePanel(); return true
        case "flushed": flushed = true; return true
        default: throw ChipError("Unknown command: \(cmd)")
        }
    }

    var flushed = false

    private func updateDraft(_ d: Draft, _ p: [String: Any]) -> [String: Any] {
        if Json.has(p, "title") { d.title = (Json.str(p, "title") ?? "").trimmingCharacters(in: .whitespaces) }
        if Json.has(p, "pinned") { d.pinned = Json.bool(p, "pinned") }
        if Json.has(p, "status") { d.status = Json.str(p, "status") == "sent" ? "sent" : "draft" }
        if let list = p["checklist"] as? [[String: Any]] {
            d.checklist = list.map { CheckItem(id: Json.str($0, "id") ?? Store.shortId(), text: Json.str($0, "text") ?? "", done: Json.bool($0, "done"), attId: Json.str($0, "attId")) }
            if let armed = d.armedItem, !d.checklist.contains(where: { $0.id == armed && !$0.done }) { d.armedItem = nil }
        }
        if let order = p["order"] as? [String] {
            d.attachments.sort { (order.firstIndex(of: $0.id) ?? Int.max) < (order.firstIndex(of: $1.id) ?? Int.max) }
        }
        store.touch(d)
        return draftDto(d)
    }

    private func addData(_ d: Draft, _ name: String?, _ dataUrl: String?, _ linkItem: String?) throws -> [String: Any] {
        guard let dataUrl = dataUrl, let comma = dataUrl.firstIndex(of: ",") else { throw ChipError("Couldn't read that dropped item.") }
        let header = String(dataUrl[..<comma])
        guard let bytes = Data(base64Encoded: String(dataUrl[dataUrl.index(after: comma)...])) else { throw ChipError("Couldn't read that dropped item.") }
        let mediaType = String(header.dropFirst(5).split(separator: ";").first ?? "")
        if mediaType.hasPrefix("image/") {
            _ = store.addImage(d, bytes, mediaType: mediaType, name: (name ?? "").isEmpty ? "Dropped image" : name, source: "Dropped", linkItem: linkItem)
        } else {
            let tmp = FileManager.default.temporaryDirectory.appendingPathComponent("Chipmunk-" + UUID().uuidString)
            try FileManager.default.createDirectory(at: tmp, withIntermediateDirectories: true)
            let f = tmp.appendingPathComponent(((name ?? "") as NSString).lastPathComponent.isEmpty ? "dropped-file" : ((name ?? "") as NSString).lastPathComponent)
            try bytes.write(to: f)
            _ = try store.addFile(d, f, source: "Dropped", linkItem: linkItem)
            try? FileManager.default.removeItem(at: tmp)
        }
        return draftDto(d)
    }

    private func send(_ d: Draft, _ how: String, _ hwnd: String?) throws -> Any {
        let atts = d.attachments.filter { $0.include }
        switch how {
        case "paste":
            let chosen = hwnd.flatMap { pid_t($0) }.flatMap { NSRunningApplication(processIdentifier: $0) }
            return try sendFromPanel(d, chosen: chosen)
        case "copyText":
            let t = Composer.build(prompt: store.readText(d), attachments: atts, pathOf: { self.store.filePath(d, $0) }, withPaths: false)
            if t.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { throw ChipError("There's no text to copy yet.") }
            clipboard.setText(t)
            return ["draft": draftDto(d), "message": "Copied the text."]
        case "copyFiles":
            let paths = atts.filter { $0.kind == "image" || $0.kind == "file" }.compactMap { store.filePath(d, $0) }.filter { FileManager.default.fileExists(atPath: $0.path) }
            if paths.isEmpty { throw ChipError("There are no images or files to copy.") }
            clipboard.setFiles(paths)
            return ["draft": draftDto(d), "message": "Copied \(plural(paths.count, "file")). Paste them into a chat or a folder."]
        default: throw ChipError("Unknown send option.")
        }
    }

    private func applySettings(_ p: [String: Any]) -> [String: Any] {
        var errors: [String: String] = [:]
        if let hk = p["hotkeys"] as? [String: Any] {
            let before = settings.hotkeys
            for name in Hotkeys.names { if let v = hk[name] as? String, !v.isEmpty { settings.hotkeys[name] = v } }
            errors = hotkeys.register(settings.hotkeys)
            for name in errors.keys { settings.hotkeys[name] = before[name] }
            if !errors.isEmpty { _ = hotkeys.register(settings.hotkeys) }
        }
        if Json.has(p, "pasteDelayMs") { settings.pasteDelayMs = max(120, min(3000, Json.int(p, "pasteDelayMs", 450))) }
        if Json.has(p, "shrinkImages") { settings.shrinkImages = Json.bool(p, "shrinkImages") }
        if Json.has(p, "terminalPaths") { settings.terminalPaths = Json.bool(p, "terminalPaths") }
        if Json.has(p, "restoreClipboard") { settings.restoreClipboard = Json.bool(p, "restoreClipboard") }
        if Json.has(p, "collectText") { settings.collectText = Json.bool(p, "collectText") }
        if Json.has(p, "onboarded") { settings.onboarded = Json.bool(p, "onboarded") }
        if Json.has(p, "alwaysOnTop") { settings.alwaysOnTop = Json.bool(p, "alwaysOnTop") }
        if Json.has(p, "theme") { settings.theme = Json.str(p, "theme") ?? "system" }
        if Json.has(p, "autoUpdate") { settings.autoUpdate = Json.bool(p, "autoUpdate") }
        if Json.has(p, "startWithWindows") { settings.openAtLogin = Json.bool(p, "startWithWindows") }
        settings.save()
        panel.applyTheme()
        return settingsDto(errors)
    }

    private func storageInfo() -> [String: Any] {
        var bytes: Int64 = 0, files = 0
        if let e = FileManager.default.enumerator(at: store.root, includingPropertiesForKeys: [.fileSizeKey]) {
            for case let u as URL in e {
                if let s = try? u.resourceValues(forKeys: [.fileSizeKey]).fileSize { bytes += Int64(s); files += 1 }
            }
        }
        let linked = store.all.reduce(0) { $0 + $1.attachments.filter { $0.linkPath != nil }.count }
        return ["bytes": NSNumber(value: bytes), "files": files, "drafts": store.count, "linked": linked]
    }

    // MARK: updates

    func checkForUpdate(_ done: ((Any?) -> Void)?) {
        var req = URLRequest(url: URL(string: "https://api.github.com/repos/itsbsiddharth/chipmunk/releases/latest")!)
        req.setValue("Chipmunk-Mac", forHTTPHeaderField: "User-Agent")
        URLSession.shared.dataTask(with: req) { [weak self] data, _, _ in
            DispatchQueue.main.async {
                guard let self = self else { return }
                var found = false
                if let data = data, let j = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
                   let tag = j["tag_name"] as? String, let page = j["html_url"] as? String {
                    let latest = tag.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
                    if Core.isNewer(latest, than: Core.version) {
                        self.update = ["version": latest, "canInstall": false, "url": page]
                        self.panel.emit("update", self.update)
                        found = true
                    }
                }
                let u: Any = found ? (self.update as Any? ?? NSNull()) : NSNull()
                done?(["update": u, "version": Core.version])
            }
        }.resume()
    }

    static func isNewer(_ a: String, than b: String) -> Bool {
        let pa = a.split(separator: ".").map { Int($0) ?? 0 }, pb = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(pa.count, pb.count) {
            let x = i < pa.count ? pa[i] : 0, y = i < pb.count ? pb[i] : 0
            if x != y { return x > y }
        }
        return false
    }

    var updateAvailable: [String: Any]? { update }

    // MARK: helpers

    func quote(_ title: String) -> String {
        let t = title.trimmingCharacters(in: .whitespaces)
        if t.isEmpty { return "your draft" }
        return "“" + (t.count > 40 ? String(t.prefix(38)) + "…" : t) + "”"
    }

    func plural(_ n: Int, _ word: String) -> String { "\(n) \(word)\(n == 1 ? "" : "s")" }

    func newDraftFromOutside() {
        let d = store.create("")
        settings.currentDraft = d.id
        settings.save()
        panel.emit("openDraft", draftDto(d))
        onStateChange?()
    }

    func quit() {
        if panel.ready {
            flushed = false
            panel.emit("flush", nil)
            let until = Date().addingTimeInterval(1)
            while !flushed && Date() < until { RunLoop.current.run(until: Date().addingTimeInterval(0.02)) }
        }
        NSApp.terminate(nil)
    }
}
