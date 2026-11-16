import AppKit

/// What's on the clipboard, in the shapes Chipmunk can use.
struct ClipContent {
    var imagePng: Data?
    var files: [URL] = []
    var text: String?
    var sourceUrl: String?
}

/// Watches the clipboard. macOS has no change notification, so it checks the change count a
/// few times a second, which costs next to nothing.
final class ClipboardWatcher {
    private let pb = NSPasteboard.general
    private var lastCount: Int
    private var timer: Timer?
    var suppress = false
    var onChange: (() -> Void)?

    init() { lastCount = NSPasteboard.general.changeCount }

    func start() {
        timer = Timer.scheduledTimer(withTimeInterval: 0.4, repeats: true) { [weak self] _ in self?.tick() }
    }

    private func tick() {
        let c = pb.changeCount
        if c == lastCount { return }
        lastCount = c
        if suppress { return }
        // Password managers mark their entries; respect that.
        let types = pb.types ?? []
        if types.contains(NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType"))
            || types.contains(NSPasteboard.PasteboardType("org.nspasteboard.TransientType")) { return }
        onChange?()
    }

    /// Chipmunk wrote to the clipboard itself: don't treat that as something new to collect.
    func markOwnChange() { lastCount = pb.changeCount }

    static func read() -> ClipContent {
        let pb = NSPasteboard.general
        var c = ClipContent()
        if let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty {
            c.files = urls
            return c
        }
        if let png = pb.data(forType: .png) { c.imagePng = png }
        else if let tiff = pb.data(forType: .tiff), let rep = NSBitmapImageRep(data: tiff) { c.imagePng = rep.representation(using: .png, properties: [:]) }
        if c.imagePng == nil { c.text = pb.string(forType: .string) }
        return c
    }

    static var hasImage: Bool {
        let types = NSPasteboard.general.types ?? []
        return types.contains(.png) || types.contains(.tiff)
    }

    func snapshot() -> [NSPasteboardItem] {
        (pb.pasteboardItems ?? []).map { item in
            let copy = NSPasteboardItem()
            for t in item.types { if let d = item.data(forType: t) { copy.setData(d, forType: t) } }
            return copy
        }
    }

    func restore(_ items: [NSPasteboardItem]) {
        pb.clearContents()
        if !items.isEmpty { pb.writeObjects(items) }
        markOwnChange()
    }

    func setText(_ text: String) {
        pb.clearContents()
        pb.setString(text, forType: .string)
        markOwnChange()
    }

    func setFiles(_ urls: [URL]) {
        pb.clearContents()
        pb.writeObjects(urls as [NSURL])
        markOwnChange()
    }
}

/// On a Mac, screenshots (⌘⇧3, ⌘⇧4, ⌘⇧5) are saved as files, by default on the Desktop, not
/// put on the clipboard. Spotlight tags each one as a screen capture, so we listen for new ones.
final class ScreenshotWatcher {
    private let query = NSMetadataQuery()
    private var seen = Set<String>()
    private var ready = false
    private let started = Date()
    var onScreenshot: ((URL) -> Void)?

    func start() {
        query.predicate = NSPredicate(format: "kMDItemIsScreenCapture == 1")
        query.searchScopes = [NSMetadataQueryUserHomeScope]
        NotificationCenter.default.addObserver(forName: .NSMetadataQueryDidFinishGathering, object: query, queue: .main) { [weak self] _ in
            guard let self = self else { return }
            self.query.disableUpdates()
            for case let item as NSMetadataItem in self.query.results {
                if let p = item.value(forAttribute: NSMetadataItemPathKey) as? String { self.seen.insert(p) }
            }
            self.ready = true
            self.query.enableUpdates()
        }
        NotificationCenter.default.addObserver(forName: .NSMetadataQueryDidUpdate, object: query, queue: .main) { [weak self] note in
            guard let self = self, self.ready else { return }
            let added = (note.userInfo?[NSMetadataQueryUpdateAddedItemsKey] as? [NSMetadataItem]) ?? []
            for item in added {
                guard let p = item.value(forAttribute: NSMetadataItemPathKey) as? String, !self.seen.contains(p) else { continue }
                self.seen.insert(p)
                // Only brand-new captures; Spotlight also re-reports old files now and then.
                let created = (item.value(forAttribute: NSMetadataItemFSCreationDateKey) as? Date) ?? Date()
                if Date().timeIntervalSince(created) < 30 && created > self.started { self.onScreenshot?(URL(fileURLWithPath: p)) }
            }
        }
        query.start()
    }
}

/// "Snip into draft": the system's own interactive capture, straight to the clipboard.
enum Snipper {
    static func run(completion: @escaping (Data?) -> Void) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        p.arguments = ["-i", "-c"]
        let before = NSPasteboard.general.changeCount
        p.terminationHandler = { proc in
            DispatchQueue.main.async {
                guard proc.terminationStatus == 0, NSPasteboard.general.changeCount != before else { completion(nil); return }
                completion(ClipboardWatcher.read().imagePng)
            }
        }
        do { try p.run() } catch { Log.error("screencapture: \(error.localizedDescription)"); completion(nil) }
    }
}

enum ImageUtil {
    /// Big Retina screenshots are scaled down before pasting: AI apps shrink them anyway.
    static func pngForPaste(_ data: Data, shrink: Bool, maxEdge: CGFloat = 2000) -> Data {
        guard shrink, let rep = NSBitmapImageRep(data: data) else { return data }
        let w = CGFloat(rep.pixelsWide), h = CGFloat(rep.pixelsHigh)
        let longest = max(w, h)
        if longest <= maxEdge && data.first == 0x89 { return data }
        let s = min(1, maxEdge / longest)
        let nw = Int(w * s), nh = Int(h * s)
        guard let out = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: nw, pixelsHigh: nh, bitsPerSample: 8, samplesPerPixel: 4,
                                         hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return data }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: out)
        NSGraphicsContext.current?.imageInterpolation = .high
        rep.draw(in: NSRect(x: 0, y: 0, width: nw, height: nh))
        NSGraphicsContext.restoreGraphicsState()
        return out.representation(using: .png, properties: [:]) ?? data
    }
}
