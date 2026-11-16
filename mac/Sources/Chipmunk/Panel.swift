import AppKit
import WebKit
import UniformTypeIdentifiers

/// Serves the panel's HTML/CSS/JS from the app bundle (chipmunk://app/...) and draft files
/// from the drafts folder (chipmunk://files/...), so the page never touches the network.
final class SchemeHandler: NSObject, WKURLSchemeHandler {
    let uiDir: URL
    let filesRoot: URL

    init(uiDir: URL, filesRoot: URL) { self.uiDir = uiDir; self.filesRoot = filesRoot }

    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        guard let url = task.request.url else { return }
        let base = url.host == "files" ? filesRoot : uiDir
        let rel = url.path.removingPercentEncoding ?? url.path
        let file = base.appendingPathComponent(String(rel.drop(while: { $0 == "/" }))).standardizedFileURL
        // Never serve anything outside the two folders.
        guard file.path.hasPrefix(base.standardizedFileURL.path), let data = try? Data(contentsOf: file) else {
            task.didFailWithError(NSError(domain: NSURLErrorDomain, code: NSURLErrorFileDoesNotExist))
            return
        }
        let mime = SchemeHandler.mime(file.pathExtension)
        let resp = HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1",
                                   headerFields: ["Content-Type": mime, "Content-Length": "\(data.count)", "Cache-Control": "no-store"])!
        task.didReceive(resp)
        task.didReceive(data)
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {}

    static func mime(_ ext: String) -> String {
        switch ext.lowercased() {
        case "html": return "text/html; charset=utf-8"
        case "js", "mjs": return "text/javascript; charset=utf-8"
        case "css": return "text/css; charset=utf-8"
        case "svg": return "image/svg+xml"
        case "json": return "application/json"
        default: return UTType(filenameExtension: ext)?.preferredMIMEType ?? "application/octet-stream"
        }
    }
}

/// The panel window: hosts the shared UI in WKWebView and relays its messages to the core.
final class PanelWindow: NSWindow, WKScriptMessageHandler, WKNavigationDelegate, NSWindowDelegate {
    static let appHost = "chipmunk://app/"
    static let filesHost = "chipmunk://files/"

    private(set) var web: WKWebView!
    weak var core: Core?
    private(set) var ready = false

    init(core: Core, uiDir: URL, filesRoot: URL) {
        self.core = core
        let frame = PanelWindow.initialFrame(core.settings.bounds)
        super.init(contentRect: frame, styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                   backing: .buffered, defer: false)
        title = "Chipmunk"
        titlebarAppearsTransparent = true
        isReleasedWhenClosed = false
        minSize = NSSize(width: 380, height: 480)
        delegate = self
        collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary]

        let config = WKWebViewConfiguration()
        config.setURLSchemeHandler(SchemeHandler(uiDir: uiDir, filesRoot: filesRoot), forURLScheme: "chipmunk")
        let bridge = """
        window.chipmunkMac = {
          _l: [],
          addEventListener(type, fn) { if (type === 'message') this._l.push(fn); },
          postMessage(msg) { window.webkit.messageHandlers.chipmunk.postMessage(JSON.stringify(msg)); },
          _dispatch(json) { const data = JSON.parse(json); this._l.forEach(fn => fn({ data })); },
        };
        """
        config.userContentController.addUserScript(WKUserScript(source: bridge, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        config.userContentController.add(WeakHandler(self), name: "chipmunk")
        web = WKWebView(frame: .zero, configuration: config)
        web.navigationDelegate = self
        web.setValue(false, forKey: "drawsBackground")
        web.autoresizingMask = [.width, .height]
        // Leave room for the traffic-light buttons above the page.
        let container = NSView(frame: frame)
        container.wantsLayer = true
        contentView = container
        web.frame = NSRect(x: 0, y: 0, width: frame.width, height: frame.height - 28)
        container.addSubview(web)
        applyTheme()
        web.load(URLRequest(url: URL(string: PanelWindow.appHost + "index.html")!))
    }

    static func initialFrame(_ saved: [Double]?) -> NSRect {
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        if let b = saved, b.count == 4 {
            let r = NSRect(x: b[0], y: b[1], width: b[2], height: b[3])
            if NSScreen.screens.contains(where: { $0.visibleFrame.intersects(r) }) && r.width >= 380 && r.height >= 480 { return r }
        }
        let w: CGFloat = 470, h = min(800, screen.height - 48)
        return NSRect(x: screen.maxX - w - 24, y: screen.minY + (screen.height - h) / 2, width: w, height: h)
    }

    func applyTheme() {
        guard let core = core else { return }
        switch core.settings.theme {
        case "dark": appearance = NSAppearance(named: .darkAqua)
        case "light": appearance = NSAppearance(named: .aqua)
        default: appearance = nil
        }
        backgroundColor = core.settings.darkMode ? NSColor(red: 0.086, green: 0.082, blue: 0.075, alpha: 1) : NSColor(red: 0.969, green: 0.965, blue: 0.949, alpha: 1)
        level = core.settings.alwaysOnTop ? .floating : .normal
    }

    // MARK: messages

    func userContentController(_ ucc: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let text = message.body as? String, let data = text.data(using: .utf8),
              let msg = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return }
        let id = msg["id"]
        let cmd = msg["cmd"] as? String ?? ""
        let args = msg["args"] as? [String: Any] ?? [:]
        core?.handle(cmd, args) { [weak self] result, error in
            var reply: [String: Any] = ["replyTo": id ?? NSNull(), "ok": error == nil]
            reply["result"] = result ?? NSNull()
            reply["error"] = error ?? NSNull()
            self?.post(reply)
        }
    }

    func emit(_ event: String, _ data: Any?) {
        guard ready else { return }
        post(["event": event, "data": data ?? NSNull()])
    }

    private func post(_ payload: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: Json.clean(payload)),
              let json = String(data: data, encoding: .utf8),
              let literal = try? JSONSerialization.data(withJSONObject: [json]),
              var arg = String(data: literal, encoding: .utf8) else { return }
        arg = String(arg.dropFirst().dropLast()) // the JSON string literal itself, safely escaped
        web.evaluateJavaScript("window.chipmunkMac && window.chipmunkMac._dispatch(\(arg))", completionHandler: nil)
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        ready = true
        if isVisible { focusPage() }
    }

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = action.request.url else { decisionHandler(.cancel); return }
        if url.scheme == "chipmunk" { decisionHandler(.allow); return }
        if url.scheme == "http" || url.scheme == "https" { NSWorkspace.shared.open(url) }
        decisionHandler(.cancel)
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        Log.error("Web content process ended; reloading")
        ready = false
        webView.reload()
    }

    func focusPage() {
        makeFirstResponder(web)
        emit("shown", nil)
    }

    // MARK: window

    override var canBecomeKey: Bool { true }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        core?.hidePanel()
        return false
    }

    func windowDidResize(_ notification: Notification) { saveBounds() }
    func windowDidMove(_ notification: Notification) { saveBounds() }
    func windowDidBecomeKey(_ notification: Notification) { core?.panelBecameKey() }

    private var boundsWork: DispatchWorkItem?
    private func saveBounds() {
        boundsWork?.cancel()
        let w = DispatchWorkItem { [weak self] in
            guard let self = self, let core = self.core else { return }
            let f = self.frame
            core.settings.bounds = [f.minX, f.minY, f.width, f.height]
            core.settings.save()
        }
        boundsWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6, execute: w)
    }

    // ⌘W hides, ⌘Q quits: route standard shortcuts even though this is a menu bar app.
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if event.modifierFlags.contains(.command), let c = event.charactersIgnoringModifiers?.lowercased() {
            if c == "w" { core?.hidePanel(); return true }
        }
        return super.performKeyEquivalent(with: event)
    }
}

/// WKUserContentController keeps its handler alive forever; this avoids a retain cycle.
final class WeakHandler: NSObject, WKScriptMessageHandler {
    weak var target: WKScriptMessageHandler?
    init(_ target: WKScriptMessageHandler) { self.target = target }
    func userContentController(_ ucc: WKUserContentController, didReceive message: WKScriptMessage) {
        target?.userContentController(ucc, didReceive: message)
    }
}

enum Json {
    /// JSONSerialization only takes plain Foundation values; normalize everything (nil becomes null).
    static func clean(_ v: Any?) -> Any {
        guard let v = v else { return NSNull() }
        let mirror = Mirror(reflecting: v)
        if mirror.displayStyle == .optional {
            guard let inner = mirror.children.first?.value else { return NSNull() }
            return clean(inner)
        }
        switch v {
        case let d as [String: Any]: return d.mapValues { clean($0) }
        case let a as [Any]: return a.map { clean($0) }
        case let s as String: return s
        case let n as NSNumber: return n
        case is NSNull: return NSNull()
        default: return String(describing: v)
        }
    }

    static func str(_ a: [String: Any], _ k: String) -> String? {
        if let s = a[k] as? String { return s }
        if let n = a[k] as? NSNumber { return n.stringValue }
        return nil
    }
    static func bool(_ a: [String: Any], _ k: String) -> Bool { (a[k] as? Bool) ?? ((a[k] as? NSNumber)?.boolValue ?? false) }
    static func int(_ a: [String: Any], _ k: String, _ fallback: Int = 0) -> Int { (a[k] as? NSNumber)?.intValue ?? fallback }
    static func obj(_ a: [String: Any], _ k: String) -> [String: Any] { a[k] as? [String: Any] ?? [:] }
    static func has(_ a: [String: Any], _ k: String) -> Bool { a[k] != nil && !(a[k] is NSNull) }
}
