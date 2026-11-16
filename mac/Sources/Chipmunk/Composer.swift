import Foundation

/// Builds the text that gets pasted: the prompt, notes about each screenshot, then quotes,
/// links and (for Claude Code in a terminal) file paths. Same rules as the Windows app.
enum Composer {
    /// Images are pasted first, in draft order, so the AI app numbers them Image 1, 2, 3.
    /// Notes are labeled with the same numbers, the way Anthropic's vision guide recommends.
    static func build(prompt: String, attachments: [Attachment], pathOf: @escaping (Attachment) -> URL?, withPaths: Bool) -> String {
        let list = attachments.filter { $0.include }
        let exists: (Attachment) -> Bool = { a in
            guard let p = pathOf(a) else { return false }
            return FileManager.default.fileExists(atPath: p.path)
        }
        var out = prompt.replacingOccurrences(of: #"\s+$"#, with: "", options: .regularExpression)
        let quotes = list.filter { $0.kind == "text" }
        let links = list.filter { $0.kind == "link" }
        let images = list.filter { $0.kind == "image" && exists($0) }
        let docs = list.filter { $0.kind == "file" && exists($0) }
        let hasNote: (Attachment) -> Bool = { !($0.note ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

        if withPaths {
            if !(images.isEmpty && docs.isEmpty) {
                out += "\n\n## Attached files\n"
                var n = 0
                for f in images + docs {
                    let label: String
                    if f.kind == "image" { n += 1; label = "Image \(n)" } else { label = f.name }
                    out += "\(label): @\"\(pathOf(f)!.path)\""
                    if hasNote(f) { out += " (\(oneLine(f.note!)))" }
                    out += "\n"
                }
            }
        } else if (images + docs).contains(where: hasNote) {
            out += "\n\n## About the attachments\n"
            for (i, img) in images.enumerated() where hasNote(img) { out += "Image \(i + 1): \(oneLine(img.note!))\n" }
            for f in docs where hasNote(f) { out += "\(f.name): \(oneLine(f.note!))\n" }
        }

        if !quotes.isEmpty {
            out += "\n\n## Quotes\n"
            for q in quotes {
                let src = (q.source ?? "").isEmpty ? "a copied snippet" : "\"\(q.source!)\""
                out += "\nFrom \(src)"
                if let u = q.url, !u.isEmpty { out += " (\(u))" }
                out += ":\n\"\"\"\n\((q.text ?? "").trimmingCharacters(in: .whitespacesAndNewlines))\n\"\"\"\n"
            }
            out.removeLast()
        }
        if !links.isEmpty {
            out += "\n\n## Links\n"
            for l in links {
                let url = l.url ?? ""
                let trimmedName = l.name.hasSuffix("…") ? String(l.name.dropLast()) : l.name
                let named = !l.name.isEmpty && !url.contains(trimmedName)
                out += "- " + (named ? l.name + ": " : "") + url + "\n"
            }
        }
        return out.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    static func oneLine(_ s: String) -> String {
        s.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: #"\s*\r?\n\s*"#, with: " ", options: .regularExpression)
    }
}
