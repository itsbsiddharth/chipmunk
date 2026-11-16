import XCTest
@testable import Chipmunk

/// Checks the parts that don't need a screen: drafts on disk, notes and what gets pasted.
final class StoreTests: XCTestCase {
    var root: URL!

    override func setUp() {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("chipmunk-test-" + UUID().uuidString)
    }

    override func tearDown() { try? FileManager.default.removeItem(at: root) }

    func png() -> Data {
        // 1x1 transparent PNG
        Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=")!
    }

    func testDraftRoundTripAndNotes() throws {
        let store = Store(root: root)
        let d = store.create("Bug report")
        store.saveText(d, "Fix the error in image 2")
        let a1 = store.addImage(d, png(), mediaType: "image/png", name: "Screenshot 1", source: "Snip", linkItem: nil)
        XCTAssertNotNil(a1)
        XCTAssertNil(store.addImage(d, png(), mediaType: "image/png", name: "dup", source: nil, linkItem: nil), "same image twice is ignored")
        var other = png(); other.append(0)
        let a2 = store.addImage(d, other, mediaType: "image/png", name: "Screenshot 2", source: "Snip", linkItem: nil)!
        a2.note = "The red banner is the bug"
        store.saveMeta(d)

        let again = Store(root: root)
        let r = try again.get(d.id)
        XCTAssertEqual(r.title, "Bug report")
        XCTAssertEqual(again.readText(r), "Fix the error in image 2")
        XCTAssertEqual(r.attachments.count, 2)
        XCTAssertEqual(r.attachments[1].note, "The red banner is the bug")
        XCTAssertEqual(r.attachments[0].w, 1)

        let text = Composer.build(prompt: again.readText(r), attachments: r.attachments, pathOf: { again.filePath(r, $0) }, withPaths: false)
        XCTAssertEqual(text, "Fix the error in image 2\n\n## About the attachments\nImage 2: The red banner is the bug")

        let paths = Composer.build(prompt: "x", attachments: r.attachments, pathOf: { again.filePath(r, $0) }, withPaths: true)
        XCTAssertTrue(paths.contains("Image 1: @\""))
        XCTAssertTrue(paths.contains("(The red banner is the bug)"))
    }

    func testReadsWindowsDraftJson() throws {
        // draft.json as the Windows app writes it (.NET dates, every field present, nulls included)
        let id = "d20261116-120809-6f84"
        let dir = root.appendingPathComponent("drafts").appendingPathComponent(id)
        try FileManager.default.createDirectory(at: dir.appendingPathComponent("files"), withIntermediateDirectories: true)
        let json = """
        {"id":"\(id)","title":"fixing bugs.","status":"draft","pinned":false,"imported":false,"created":"2026-11-16T06:38:09.1234567Z","updated":"2026-11-16T07:58:57.1171226Z","armedItem":null,"checklist":[],"attachments":[{"id":"adc75712f50","kind":"image","name":"Screenshot 1:18 PM","file":"files/shot.png","linkPath":null,"text":null,"url":null,"source":"Screenshot · Chrome","note":null,"ext":"png","added":"2026-11-16T07:48:10.9612488Z","hash":"abc","include":true,"w":1920,"h":1080,"bytes":1129597,"removedIndex":-1}],"removed":[],"sends":[]}
        """
        try json.write(to: dir.appendingPathComponent("draft.json"), atomically: true, encoding: .utf8)
        try "hello".write(to: dir.appendingPathComponent("prompt.md"), atomically: true, encoding: .utf8)
        let store = Store(root: root)
        let d = try store.get(id)
        XCTAssertEqual(d.title, "fixing bugs.")
        XCTAssertEqual(d.attachments.first?.w, 1920)
        XCTAssertNotNil(Store.parseDate(d.updated))
    }

    func testRemoveAndRestoreKeepsOrder() throws {
        let store = Store(root: root)
        let d = store.create("")
        let a = try store.addText(d, "first", source: nil, url: nil, linkItem: nil)!
        _ = try store.addText(d, "second", source: nil, url: nil, linkItem: nil)
        store.removeAttachment(d, a.id)
        XCTAssertEqual(d.attachments.count, 1)
        store.restoreAttachment(d, a.id)
        XCTAssertEqual(d.attachments.first?.text, "first")
    }

    func testHotkeyParsing() {
        XCTAssertNotNil(Hotkeys.parse("Ctrl+Option+P"))
        XCTAssertNotNil(Hotkeys.parse("Cmd+Shift+F5"))
        XCTAssertNil(Hotkeys.parse("Shift+P"), "needs Cmd, Ctrl or Option")
        XCTAssertNil(Hotkeys.parse("Ctrl+Banana"))
    }

    func testVersionCompare() {
        XCTAssertTrue(Core.isNewer("1.4.1", than: "1.4.0"))
        XCTAssertFalse(Core.isNewer("1.4.0", than: "1.4.0"))
        XCTAssertFalse(Core.isNewer("1.3.9", than: "1.4.0"))
    }
}
