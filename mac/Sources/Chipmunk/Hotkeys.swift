import Foundation
import Carbon.HIToolbox

/// Global shortcuts through Carbon's RegisterEventHotKey: works in every app and needs no
/// permission. Shortcuts are written like "Ctrl+Option+P" (Cmd, Ctrl, Option/Alt, Shift).
final class Hotkeys {
    static let names = ["toggle", "snip", "grab", "paste"]
    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var handler: EventHandlerRef?
    private let onFire: (String) -> Void
    var suspended = false

    init(onFire: @escaping (String) -> Void) {
        self.onFire = onFire
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let me = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(GetApplicationEventTarget(), { _, event, user in
            guard let event = event, let user = user else { return noErr }
            var id = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil,
                              MemoryLayout<EventHotKeyID>.size, nil, &id)
            let me = Unmanaged<Hotkeys>.fromOpaque(user).takeUnretainedValue()
            let index = Int(id.id) - 1
            if index >= 0 && index < Hotkeys.names.count {
                let name = Hotkeys.names[index]
                DispatchQueue.main.async { me.onFire(name) }
            }
            return noErr
        }, 1, &spec, me, &handler)
    }

    /// Registers every shortcut. Returns name -> error for the ones that couldn't be used.
    func register(_ combos: [String: String]) -> [String: String] {
        for (_, ref) in refs { UnregisterEventHotKey(ref) }
        refs.removeAll()
        var errors: [String: String] = [:]
        if suspended { return errors }
        var used = Set<String>()
        for (i, name) in Hotkeys.names.enumerated() {
            guard let combo = combos[name], let parsed = Hotkeys.parse(combo) else {
                errors[name] = "That isn't a shortcut the Mac can use."
                continue
            }
            if used.contains(combo.lowercased()) { errors[name] = "Already used for another Chipmunk action."; continue }
            var ref: EventHotKeyRef?
            let id = EventHotKeyID(signature: OSType(0x43484950), id: UInt32(i + 1)) // 'CHIP'
            let status = RegisterEventHotKey(parsed.key, parsed.mods, id, GetApplicationEventTarget(), 0, &ref)
            if status == noErr, let ref = ref {
                refs[UInt32(i + 1)] = ref
                used.insert(combo.lowercased())
            } else {
                errors[name] = "\(combo) is already taken by another app. Pick a different one."
            }
        }
        return errors
    }

    static func parse(_ text: String) -> (key: UInt32, mods: UInt32)? {
        var mods: UInt32 = 0
        var key: UInt32? = nil
        for raw in text.split(separator: "+") {
            let p = raw.trimmingCharacters(in: .whitespaces).lowercased()
            switch p {
            case "cmd", "command", "win": mods |= UInt32(cmdKey)
            case "ctrl", "control": mods |= UInt32(controlKey)
            case "option", "alt", "opt": mods |= UInt32(optionKey)
            case "shift": mods |= UInt32(shiftKey)
            default:
                guard let k = keyCodes[p] else { return nil }
                key = k
            }
        }
        guard let k = key, mods & UInt32(cmdKey | controlKey | optionKey) != 0 else { return nil }
        return (k, mods)
    }

    private static let keyCodes: [String: UInt32] = [
        "a": UInt32(kVK_ANSI_A), "b": UInt32(kVK_ANSI_B), "c": UInt32(kVK_ANSI_C), "d": UInt32(kVK_ANSI_D),
        "e": UInt32(kVK_ANSI_E), "f": UInt32(kVK_ANSI_F), "g": UInt32(kVK_ANSI_G), "h": UInt32(kVK_ANSI_H),
        "i": UInt32(kVK_ANSI_I), "j": UInt32(kVK_ANSI_J), "k": UInt32(kVK_ANSI_K), "l": UInt32(kVK_ANSI_L),
        "m": UInt32(kVK_ANSI_M), "n": UInt32(kVK_ANSI_N), "o": UInt32(kVK_ANSI_O), "p": UInt32(kVK_ANSI_P),
        "q": UInt32(kVK_ANSI_Q), "r": UInt32(kVK_ANSI_R), "s": UInt32(kVK_ANSI_S), "t": UInt32(kVK_ANSI_T),
        "u": UInt32(kVK_ANSI_U), "v": UInt32(kVK_ANSI_V), "w": UInt32(kVK_ANSI_W), "x": UInt32(kVK_ANSI_X),
        "y": UInt32(kVK_ANSI_Y), "z": UInt32(kVK_ANSI_Z),
        "0": UInt32(kVK_ANSI_0), "1": UInt32(kVK_ANSI_1), "2": UInt32(kVK_ANSI_2), "3": UInt32(kVK_ANSI_3),
        "4": UInt32(kVK_ANSI_4), "5": UInt32(kVK_ANSI_5), "6": UInt32(kVK_ANSI_6), "7": UInt32(kVK_ANSI_7),
        "8": UInt32(kVK_ANSI_8), "9": UInt32(kVK_ANSI_9),
        "space": UInt32(kVK_Space), "enter": UInt32(kVK_Return), "tab": UInt32(kVK_Tab),
        "f1": UInt32(kVK_F1), "f2": UInt32(kVK_F2), "f3": UInt32(kVK_F3), "f4": UInt32(kVK_F4), "f5": UInt32(kVK_F5),
        "f6": UInt32(kVK_F6), "f7": UInt32(kVK_F7), "f8": UInt32(kVK_F8), "f9": UInt32(kVK_F9), "f10": UInt32(kVK_F10),
        "f11": UInt32(kVK_F11), "f12": UInt32(kVK_F12),
        "comma": UInt32(kVK_ANSI_Comma), "period": UInt32(kVK_ANSI_Period), "slash": UInt32(kVK_ANSI_Slash),
        "semicolon": UInt32(kVK_ANSI_Semicolon), "quote": UInt32(kVK_ANSI_Quote), "backquote": UInt32(kVK_ANSI_Grave),
        "minus": UInt32(kVK_ANSI_Minus), "plus": UInt32(kVK_ANSI_Equal),
        "openbracket": UInt32(kVK_ANSI_LeftBracket), "closebracket": UInt32(kVK_ANSI_RightBracket), "backslash": UInt32(kVK_ANSI_Backslash),
        "up": UInt32(kVK_UpArrow), "down": UInt32(kVK_DownArrow), "left": UInt32(kVK_LeftArrow), "right": UInt32(kVK_RightArrow),
    ]
}
