import Foundation
import CryptoKit
import ImageIO

// Drafts are plain folders, in the same format as the Windows app:
// drafts/<id>/prompt.md, draft.json, files/, .versions/. Every change is written straight to disk.

final class CheckItem: Codable {
    var id: String
    var text: String
    var done: Bool
    var attId: String?

    init(id: String, text: String, done: Bool, attId: String?) {
        self.id = id; self.text = text; self.done = done; self.attId = attId
    }

    required init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(String.self, forKey: .id) ?? Store.shortId()
        text = try c.decodeIfPresent(String.self, forKey: .text) ?? ""
        done = try c.decodeIfPresent(Bool.self, forKey: .done) ?? false
        attId = try c.decodeIfPresent(String.self, forKey: .attId)
    }
}

final class Attachment: Codable {
    var id = ""
    var kind = "file"          // image | file | text | link
    var name = ""
    var file: String?          // path relative to the draft folder
    var linkPath: String?      // big files are linked where they are
    var text: String?
    var url: String?
    var source: String?
    var note: String?          // what the user wants the AI to notice in this item
    var ext: String?
    var added: String?
    var hash: String?
    var include = true
    var w = 0
    var h = 0
    var bytes: Int64 = 0
    var removedIndex = -1

    init() {}

    required init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(String.self, forKey: .id) ?? ""
        kind = try c.decodeIfPresent(String.self, forKey: .kind) ?? "file"
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? ""
        file = try c.decodeIfPresent(String.self, forKey: .file)
        linkPath = try c.decodeIfPresent(String.self, forKey: .linkPath)
        text = try c.decodeIfPresent(String.self, forKey: .text)
        url = try c.decodeIfPresent(String.self, forKey: .url)
        source = try c.decodeIfPresent(String.self, forKey: .source)
        note = try c.decodeIfPresent(String.self, forKey: .note)
        ext = try c.decodeIfPresent(String.self, forKey: .ext)
        added = try c.decodeIfPresent(String.self, forKey: .added)
        hash = try c.decodeIfPresent(String.self, forKey: .hash)
        include = try c.decodeIfPresent(Bool.self, forKey: .include) ?? true
        w = try c.decodeIfPresent(Int.self, forKey: .w) ?? 0
        h = try c.decodeIfPresent(Int.self, forKey: .h) ?? 0
        bytes = try c.decodeIfPresent(Int64.self, forKey: .bytes) ?? 0
        removedIndex = try c.decodeIfPresent(Int.self, forKey: .removedIndex) ?? -1
    }

    func copy() -> Attachment {
        let data = (try? JSONEncoder().encode(self)) ?? Data()
        return (try? JSONDecoder().decode(Attachment.self, from: data)) ?? Attachment()
    }
}

final class SendRecord: Codable {
    var at: String
    var target: String
    var items: Int
    init(at: String, target: String, items: Int) { self.at = at; self.target = target; self.items = items }
}

final class Draft: Codable {
    var id = ""
    var title = ""
    var status = "draft"
    var pinned = false
    var imported = false
    var created = ""
    var updated = ""
    var armedItem: String?
    var checklist: [CheckItem] = []
    var attachments: [Attachment] = []
    var removed: [Attachment] = []
    var sends: [SendRecord] = []

    init() {}

    required init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(String.self, forKey: .id) ?? ""
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? ""
        status = try c.decodeIfPresent(String.self, forKey: .status) ?? "draft"
        pinned = try c.decodeIfPresent(Bool.self, forKey: .pinned) ?? false
        imported = try c.decodeIfPresent(Bool.self, forKey: .imported) ?? false
        created = try c.decodeIfPresent(String.self, forKey: .created) ?? ""
        updated = try c.decodeIfPresent(String.self, forKey: .updated) ?? ""
        armedItem = try c.decodeIfPresent(String.self, forKey: .armedItem)
        checklist = try c.decodeIfPresent([CheckItem].self, forKey: .checklist) ?? []
        attachments = try c.decodeIfPresent([Attachment].self, forKey: .attachments) ?? []
        removed = try c.decodeIfPresent([Attachment].self, forKey: .removed) ?? []
        sends = try c.decodeIfPresent([SendRecord].self, forKey: .sends) ?? []
    }
}

struct ChipError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}

final class Store {
    let root: URL
    let draftsDir: URL
    let trashDir: URL
    private(set) var drafts: [String: Draft] = [:]
    private var lastVersionAt: [String: Date] = [:]
    static let linkThreshold: Int64 = 25 * 1024 * 1024
    private let fm = FileManager.default

    init(root: URL) {
        self.root = root
        draftsDir = root.appendingPathComponent("drafts")
        trashDir = root.appendingPathComponent(".trash")
        try? fm.createDirectory(at: draftsDir, withIntermediateDirectories: true)
        writeReadme()
        let dirs = (try? fm.contentsOfDirectory(at: draftsDir, includingPropertiesForKeys: nil)) ?? []
        for dir in dirs where dir.hasDirectoryPath || isDir(dir) {
            if let d = loadDraft(dir) { drafts[d.id] = d }
        }
    }

    private func isDir(_ url: URL) -> Bool {
        var b: ObjCBool = false
        return fm.fileExists(atPath: url.path, isDirectory: &b) && b.boolValue
    }

    private func writeReadme() {
        let p = root.appendingPathComponent("README.txt")
        if fm.fileExists(atPath: p.path) { return }
        let text = """
        These are your Chipmunk drafts.

        Each folder in drafts/ is one draft:
          prompt.md    the prompt text (always the latest)
          files/       screenshots and files you added
          .versions/   earlier versions of the text, one per minute of editing
          draft.json   title, reminders and attachment details

        Deleted drafts go to .trash/ and can be restored by moving them back.

        """
        try? text.write(to: p, atomically: true, encoding: .utf8)
    }

    static func now() -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f.string(from: Date())
    }

    static func parseDate(_ s: String?) -> Date? {
        guard let s = s, !s.isEmpty else { return nil }
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        if let d = f.date(from: s) { return d }
        // .NET writes 7 fractional digits ("2026-11-16T07:48:10.9612488Z"); trim them to 3.
        let trimmed = s.replacingOccurrences(of: #"(\.\d{3})\d+"#, with: "$1", options: .regularExpression)
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f.date(from: trimmed)
    }

    static func shortId() -> String { String(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased().prefix(8)) }

    private func loadDraft(_ dir: URL) -> Draft? {
        let meta = dir.appendingPathComponent("draft.json")
        var d: Draft? = nil
        if let data = try? Data(contentsOf: meta) { d = try? JSONDecoder().decode(Draft.self, from: data) }
        if d == nil {
            guard fm.fileExists(atPath: dir.appendingPathComponent("prompt.md").path) || !fm.fileExists(atPath: meta.path) else { return nil }
            let fresh = Draft()
            fresh.created = Store.now(); fresh.updated = fresh.created
            d = fresh
        }
        d!.id = dir.lastPathComponent
        if d!.updated.isEmpty { d!.updated = d!.created.isEmpty ? Store.now() : d!.created }
        return d
    }

    var all: [Draft] { Array(drafts.values) }
    var count: Int { drafts.count }
    func exists(_ id: String?) -> Bool { id != nil && drafts[id!] != nil }

    func get(_ id: String?) throws -> Draft {
        if let id = id, let d = drafts[id] { return d }
        throw ChipError("That draft no longer exists.")
    }

    func dir(_ d: Draft) -> URL { draftsDir.appendingPathComponent(d.id) }
    private func textPath(_ d: Draft) -> URL { dir(d).appendingPathComponent("prompt.md") }
    private func versionsDir(_ d: Draft) -> URL { dir(d).appendingPathComponent(".versions") }

    func filePath(_ d: Draft, _ a: Attachment) -> URL? {
        if let l = a.linkPath { return URL(fileURLWithPath: l) }
        guard let f = a.file else { return nil }
        return dir(d).appendingPathComponent(f)
    }

    private static func stamp(_ format: String, utc: Bool = false) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        if utc { f.timeZone = TimeZone(identifier: "UTC") }
        f.dateFormat = format
        return f.string(from: Date())
    }

    private static func newId() -> String { "d" + stamp("yyyyMMdd-HHmmss") + "-" + String(shortId().prefix(4)) }
    private static func newAttId() -> String { "a" + String(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased().prefix(10)) }

    @discardableResult
    func create(_ title: String) -> Draft {
        let d = Draft()
        d.id = Store.newId()
        d.title = title
        d.created = Store.now()
        d.updated = d.created
        try? fm.createDirectory(at: dir(d).appendingPathComponent("files"), withIntermediateDirectories: true)
        try? "".write(to: textPath(d), atomically: true, encoding: .utf8)
        drafts[d.id] = d
        saveMeta(d)
        return d
    }

    func duplicate(_ src: Draft) -> Draft {
        let d = create(src.title.trimmingCharacters(in: .whitespaces).isEmpty ? "Copy of draft" : src.title + " (copy)")
        try? readText(src).write(to: textPath(d), atomically: true, encoding: .utf8)
        let from = dir(src).appendingPathComponent("files"), to = dir(d).appendingPathComponent("files")
        for f in (try? fm.contentsOfDirectory(at: from, includingPropertiesForKeys: nil)) ?? [] {
            try? fm.copyItem(at: f, to: to.appendingPathComponent(f.lastPathComponent))
        }
        d.attachments = src.attachments.map { $0.copy() }
        d.checklist = src.checklist.map { CheckItem(id: $0.id, text: $0.text, done: $0.done, attId: $0.attId) }
        saveMeta(d)
        return d
    }

    func readText(_ d: Draft) -> String { (try? String(contentsOf: textPath(d), encoding: .utf8)) ?? "" }

    func saveText(_ d: Draft, _ text: String) {
        let old = readText(d)
        if old == text { return }
        maybeSnapshot(d, old)
        try? text.write(to: textPath(d), atomically: true, encoding: .utf8)
        touch(d)
    }

    private func maybeSnapshot(_ d: Draft, _ old: String) {
        if old.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return }
        if let last = lastVersionAt[d.id], Date().timeIntervalSince(last) < 60 { return }
        writeVersion(d, old)
    }

    private func writeVersion(_ d: Draft, _ text: String) {
        let vdir = versionsDir(d)
        try? fm.createDirectory(at: vdir, withIntermediateDirectories: true)
        let existing = ((try? fm.contentsOfDirectory(atPath: vdir.path)) ?? []).filter { $0.hasSuffix(".md") }.sorted(by: >)
        if let latest = existing.first, (try? String(contentsOf: vdir.appendingPathComponent(latest), encoding: .utf8)) == text {
            lastVersionAt[d.id] = Date(); return
        }
        let name = Store.stamp("yyyyMMdd-HHmmss", utc: true) + ".md"
        try? text.write(to: vdir.appendingPathComponent(name), atomically: true, encoding: .utf8)
        lastVersionAt[d.id] = Date()
        for extra in (existing + [name]).sorted(by: >).dropFirst(150) { try? fm.removeItem(at: vdir.appendingPathComponent(extra)) }
    }

    func versions(_ d: Draft) -> [[String: Any]] {
        let vdir = versionsDir(d)
        let names = ((try? fm.contentsOfDirectory(atPath: vdir.path)) ?? []).filter { $0.hasSuffix(".md") }.sorted(by: >)
        let parse = DateFormatter()
        parse.locale = Locale(identifier: "en_US_POSIX"); parse.timeZone = TimeZone(identifier: "UTC"); parse.dateFormat = "yyyyMMdd-HHmmss"
        let iso = ISO8601DateFormatter()
        return names.map { n in
            let text = (try? String(contentsOf: vdir.appendingPathComponent(n), encoding: .utf8)) ?? ""
            let t = parse.date(from: String(n.dropLast(3))) ?? Date()
            return ["name": n, "time": iso.string(from: t), "preview": Store.firstLine(text, 90), "words": Store.wordCount(text)]
        }
    }

    func readVersion(_ d: Draft, _ name: String) throws -> String {
        let p = versionsDir(d).appendingPathComponent((name as NSString).lastPathComponent)
        guard let t = try? String(contentsOf: p, encoding: .utf8) else { throw ChipError("That version is gone.") }
        return t
    }

    func restoreVersion(_ d: Draft, _ name: String) throws {
        let text = try readVersion(d, name)
        let current = readText(d)
        if !current.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { writeVersion(d, current) }
        try? text.write(to: textPath(d), atomically: true, encoding: .utf8)
        touch(d)
    }

    func touch(_ d: Draft) {
        d.updated = Store.now()
        saveMeta(d)
    }

    func saveMeta(_ d: Draft) {
        let enc = JSONEncoder()
        guard let data = try? enc.encode(d) else { return }
        try? data.write(to: dir(d).appendingPathComponent("draft.json"), options: .atomic)
    }

    func delete(_ d: Draft) throws {
        try? fm.createDirectory(at: trashDir, withIntermediateDirectories: true)
        let dest = trashDir.appendingPathComponent(d.id)
        if fm.fileExists(atPath: dest.path) { try? fm.removeItem(at: dest) }
        try fm.moveItem(at: dir(d), to: dest)
        drafts[d.id] = nil
    }

    func undelete(_ id: String) throws -> Draft {
        let name = (id as NSString).lastPathComponent
        let src = trashDir.appendingPathComponent(name)
        guard fm.fileExists(atPath: src.path) else { throw ChipError("That draft is no longer in the trash.") }
        let dest = draftsDir.appendingPathComponent(name)
        try fm.moveItem(at: src, to: dest)
        guard let d = loadDraft(dest) else { throw ChipError("Couldn't read that draft.") }
        drafts[d.id] = d
        touch(d)
        return d
    }

    // MARK: attachments

    static func sha1(_ data: Data) -> String {
        Insecure.SHA1.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    private func uniqueFileName(_ d: Draft, _ raw: String) -> String {
        let folder = dir(d).appendingPathComponent("files")
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        var name = raw.components(separatedBy: CharacterSet(charactersIn: "/:\\")).joined(separator: "_").trimmingCharacters(in: .whitespaces)
        if name.isEmpty { name = "file" }
        let ext = (name as NSString).pathExtension
        if name.count > 100 { name = String(name.prefix(80)) + (ext.isEmpty ? "" : "." + ext) }
        let stem = (name as NSString).deletingPathExtension
        var candidate = name
        var i = 2
        while fm.fileExists(atPath: folder.appendingPathComponent(candidate).path) {
            candidate = stem + " (\(i))" + (ext.isEmpty ? "" : "." + ext)
            i += 1
        }
        return candidate
    }

    private func findByHash(_ d: Draft, _ hash: String) -> Attachment? { d.attachments.first { $0.hash == hash } }

    static func imageSize(_ data: Data) -> (Int, Int) {
        guard let src = CGImageSourceCreateWithData(data as CFData, nil),
              let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any] else { return (0, 0) }
        let w = props[kCGImagePropertyPixelWidth] as? Int ?? 0
        let h = props[kCGImagePropertyPixelHeight] as? Int ?? 0
        return (w, h)
    }

    static func imageExtension(_ data: Data, mediaType: String) -> String {
        let b = [UInt8](data.prefix(12))
        if b.count >= 4 && b[0] == 0x89 && b[1] == 0x50 { return ".png" }
        if b.count >= 3 && b[0] == 0xFF && b[1] == 0xD8 { return ".jpg" }
        if b.count >= 4 && b[0] == 0x47 && b[1] == 0x49 { return ".gif" }
        if b.count >= 12 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50 { return ".webp" }
        switch mediaType {
        case "image/jpeg": return ".jpg"
        case "image/gif": return ".gif"
        case "image/webp": return ".webp"
        default: return ".png"
        }
    }

    static let imageExtensions: Set<String> = ["png", "jpg", "jpeg", "gif", "webp", "bmp", "heic", "tiff", "tif"]

    static func screenshotName() -> String {
        let f = DateFormatter()
        f.dateFormat = "h:mm a"
        return "Screenshot " + f.string(from: Date())
    }

    /// Adds image bytes. Returns nil if the same image is already in the draft.
    func addImage(_ d: Draft, _ data: Data, mediaType: String, name: String?, source: String?, linkItem: String?) -> Attachment? {
        let hash = Store.sha1(data)
        if findByHash(d, hash) != nil { return nil }
        let ext = Store.imageExtension(data, mediaType: mediaType)
        let fileName = uniqueFileName(d, "shot-" + Store.stamp("yyyyMMdd-HHmmss-SSS") + ext)
        do { try data.write(to: dir(d).appendingPathComponent("files").appendingPathComponent(fileName)) } catch { return nil }
        let size = Store.imageSize(data)
        let a = Attachment()
        a.id = Store.newAttId(); a.kind = "image"; a.name = name ?? Store.screenshotName(); a.file = "files/" + fileName
        a.source = source; a.ext = String(ext.dropFirst()); a.hash = hash; a.w = size.0; a.h = size.1
        a.bytes = Int64(data.count); a.added = Store.now()
        return insert(d, a, linkItem)
    }

    func addFile(_ d: Draft, _ url: URL, source: String?, linkItem: String?) throws -> Attachment? {
        let attrs = try? fm.attributesOfItem(atPath: url.path)
        guard let size = (attrs?[.size] as? NSNumber)?.int64Value else { throw ChipError("Can't find \(url.path)") }
        let ext = url.pathExtension.lowercased()
        if size > Store.linkThreshold {
            let mod = (attrs?[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0
            let key = "link:" + url.path.lowercased() + "|\(size)|\(Int64(mod))"
            if findByHash(d, key) != nil { return nil }
            let a = Attachment()
            a.id = Store.newAttId(); a.kind = "file"; a.name = url.lastPathComponent; a.linkPath = url.path
            a.source = source; a.ext = ext; a.hash = key; a.bytes = size; a.added = Store.now()
            return insert(d, a, linkItem)
        }
        let data = try Data(contentsOf: url)
        let hash = Store.sha1(data)
        if findByHash(d, hash) != nil { return nil }
        let fileName = uniqueFileName(d, url.lastPathComponent)
        try data.write(to: dir(d).appendingPathComponent("files").appendingPathComponent(fileName))
        let isImage = Store.imageExtensions.contains(ext)
        let a = Attachment()
        a.id = Store.newAttId(); a.kind = isImage ? "image" : "file"; a.name = url.lastPathComponent; a.file = "files/" + fileName
        a.source = source; a.ext = ext; a.hash = hash; a.bytes = Int64(data.count); a.added = Store.now()
        if isImage { let s = Store.imageSize(data); a.w = s.0; a.h = s.1 }
        return insert(d, a, linkItem)
    }

    func addText(_ d: Draft, _ raw: String, source: String?, url: String?, linkItem: String?) throws -> Attachment? {
        let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty { throw ChipError("There was no text to add.") }
        let hash = Store.sha1(Data(text.utf8))
        if findByHash(d, hash) != nil { return nil }
        let a = Attachment()
        a.id = Store.newAttId(); a.kind = "text"; a.text = text; a.source = source; a.url = url; a.hash = hash
        a.name = Store.firstLine(text, 60); a.added = Store.now()
        return insert(d, a, linkItem)
    }

    func addLink(_ d: Draft, _ raw: String, title: String?, linkItem: String?) -> Attachment? {
        let url = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let hash = Store.sha1(Data(url.utf8))
        if findByHash(d, hash) != nil { return nil }
        var name = (title ?? "").trimmingCharacters(in: .whitespaces)
        if name.isEmpty { name = url.replacingOccurrences(of: #"^https?://(www\.)?"#, with: "", options: .regularExpression) }
        if name.count > 80 { name = String(name.prefix(80)) + "…" }
        let a = Attachment()
        a.id = Store.newAttId(); a.kind = "link"; a.url = url; a.name = name; a.hash = hash; a.added = Store.now()
        return insert(d, a, linkItem)
    }

    private func insert(_ d: Draft, _ a: Attachment, _ linkItem: String?) -> Attachment {
        d.attachments.append(a)
        var target: CheckItem? = nil
        if let li = linkItem { target = d.checklist.first { $0.id == li } }
        if target == nil, let armed = d.armedItem { target = d.checklist.first { $0.id == armed && !$0.done } }
        if target == nil { target = d.checklist.first { !$0.done && $0.attId == nil && Store.inferKind($0.text) == a.kind } }
        if let t = target { t.done = true; t.attId = a.id }
        d.armedItem = nil
        touch(d)
        return a
    }

    static func inferKind(_ text: String) -> String? {
        let t = text.lowercased()
        func m(_ p: String) -> Bool { t.range(of: p, options: .regularExpression) != nil }
        if m(#"screen ?shot|image|photo|picture|snip|\bui\b|\bpic\b"#) { return "image" }
        if m(#"\.(csv|pdf|docx?|txt|json|ipynb|py|js|ts|log|zip|xlsx?|md)\b|\bfile\b|notebook|\blogs?\b"#) { return "file" }
        if m(#"\blink\b|\burl\b|\bdocs?\b|source|article|\bpage\b"#) { return "link" }
        if m(#"quote|snippet|message|paragraph|\btext\b"#) { return "text" }
        return nil
    }

    func removeAttachment(_ d: Draft, _ attId: String) {
        guard let i = d.attachments.firstIndex(where: { $0.id == attId }) else { return }
        let att = d.attachments.remove(at: i)
        att.removedIndex = i
        d.removed.removeAll { $0.id == attId }
        d.removed.append(att)
        if d.removed.count > 50 { d.removed.removeFirst() }
        for it in d.checklist where it.attId == attId { it.attId = nil; it.done = false }
        touch(d)
    }

    func restoreAttachment(_ d: Draft, _ attId: String) {
        guard let i = d.removed.firstIndex(where: { $0.id == attId }) else { return }
        let att = d.removed.remove(at: i)
        let at = max(0, min(att.removedIndex, d.attachments.count))
        att.removedIndex = -1
        d.attachments.insert(att, at: at)
        touch(d)
    }

    static func firstLine(_ text: String, _ max: Int) -> String {
        for line in text.components(separatedBy: "\n") {
            let t = line.trimmingCharacters(in: .whitespaces)
            if t.isEmpty { continue }
            return t.count > max ? String(t.prefix(max)).trimmingCharacters(in: .whitespaces) + "…" : t
        }
        return ""
    }

    static func wordCount(_ text: String) -> Int {
        text.split(whereSeparator: { $0.isWhitespace }).count
    }
}
