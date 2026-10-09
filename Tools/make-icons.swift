// Generates Yolk's icon assets from code, so they can be regenerated and
// tweaked rather than living as opaque binaries.
//
//   swift Tools/make-icons.swift
//
// Writes into App/Yolk/Assets.xcassets.

import AppKit
import CoreGraphics
import Foundation

// MARK: - Geometry

/// The fried-egg white: an organic blob, not an ellipse. Radius is modulated
/// by angle so the outline has the irregular lobes a real fried egg has, while
/// staying perfectly reproducible.
func eggWhitePath(in rect: CGRect) -> CGPath {
    let path = CGMutablePath()
    let cx = rect.midX, cy = rect.midY
    let base = min(rect.width, rect.height) * 0.46
    let steps = 180
    for i in 0...steps {
        let t = Double(i) / Double(steps) * 2 * .pi
        // Two low-frequency harmonics: enough to read as organic, few enough
        // to stay smooth at 16pt.
        let wobble = 1.0 + 0.085 * sin(3 * t + 0.6) + 0.055 * cos(5 * t + 1.9)
        let r = base * wobble
        let p = CGPoint(x: cx + CGFloat(cos(t) * r), y: cy + CGFloat(sin(t) * r * 0.88))
        if i == 0 { path.move(to: p) } else { path.addLine(to: p) }
    }
    path.closeSubpath()
    return path
}

/// The yolk, set off-centre the way one actually sits.
func yolkRect(in rect: CGRect) -> CGRect {
    let d = min(rect.width, rect.height) * 0.37
    return CGRect(
        x: rect.midX - d / 2 + rect.width * 0.055,
        y: rect.midY - d / 2 + rect.height * 0.035,
        width: d, height: d)
}

// MARK: - Rendering

func makeContext(size: Int) -> CGContext {
    let ctx = CGContext(
        data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
        space: CGColorSpace(name: CGColorSpace.sRGB)!,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.setAllowsAntialiasing(true)
    ctx.interpolationQuality = .high
    return ctx
}

func write(_ ctx: CGContext, to url: URL) {
    let image = ctx.makeImage()!
    let rep = NSBitmapImageRep(cgImage: image)
    rep.size = NSSize(width: ctx.width, height: ctx.height)
    try! rep.representation(using: .png, properties: [:])!.write(to: url)
}

/// The full-colour app icon: the egg on the rounded-square canvas macOS
/// expects, with the standard ~10% transparent margin.
func drawAppIcon(size: Int) -> CGContext {
    let ctx = makeContext(size: size)
    let s = CGFloat(size)
    let inset = s * 0.0977
    let plate = CGRect(x: inset, y: inset, width: s - inset * 2, height: s - inset * 2)
    let squircle = CGPath(
        roundedRect: plate, cornerWidth: plate.width * 0.2237,
        cornerHeight: plate.height * 0.2237, transform: nil)

    // Warm amber plate so a white egg has something to sit against.
    ctx.saveGState()
    ctx.addPath(squircle)
    ctx.clip()
    let bg = CGGradient(
        colorsSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
        colors: [
            CGColor(red: 1.00, green: 0.82, blue: 0.36, alpha: 1),
            CGColor(red: 0.98, green: 0.60, blue: 0.11, alpha: 1),
        ] as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(
        bg, start: CGPoint(x: plate.minX, y: plate.maxY),
        end: CGPoint(x: plate.maxX, y: plate.minY), options: [])
    ctx.restoreGState()

    // The egg, inset within the plate.
    let eggBox = plate.insetBy(dx: plate.width * 0.10, dy: plate.height * 0.10)
    let white = eggWhitePath(in: eggBox)

    ctx.saveGState()
    ctx.setShadow(
        offset: CGSize(width: 0, height: -s * 0.012), blur: s * 0.03,
        color: CGColor(red: 0.45, green: 0.22, blue: 0.0, alpha: 0.33))
    ctx.addPath(white)
    ctx.setFillColor(CGColor(red: 1, green: 0.996, blue: 0.976, alpha: 1))
    ctx.fillPath()
    ctx.restoreGState()

    let yolk = yolkRect(in: eggBox)
    ctx.saveGState()
    ctx.addEllipse(in: yolk)
    ctx.clip()
    let yolkGradient = CGGradient(
        colorsSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
        colors: [
            CGColor(red: 1.00, green: 0.85, blue: 0.30, alpha: 1),
            CGColor(red: 0.91, green: 0.47, blue: 0.05, alpha: 1),
        ] as CFArray, locations: [0, 1])!
    ctx.drawRadialGradient(
        yolkGradient,
        startCenter: CGPoint(x: yolk.midX - yolk.width * 0.18, y: yolk.midY + yolk.height * 0.18),
        startRadius: 0, endCenter: CGPoint(x: yolk.midX, y: yolk.midY),
        endRadius: yolk.width * 0.62, options: [.drawsAfterEndLocation])
    ctx.restoreGState()
    return ctx
}

/// Menu bar icons are template images: pure black plus alpha, so macOS can
/// invert and tint them for light and dark menu bars. A full-colour egg cannot
/// serve this role.
func drawMenuBarIcon(size: Int, filled: Bool) -> CGContext {
    let ctx = makeContext(size: size)
    let s = CGFloat(size)
    let box = CGRect(x: 0, y: 0, width: s, height: s).insetBy(dx: s * 0.06, dy: s * 0.12)
    let stroke = max(1, s * 0.072)
    let black = CGColor(red: 0, green: 0, blue: 0, alpha: 1)

    // Egg outline.
    ctx.addPath(eggWhitePath(in: box))
    ctx.setStrokeColor(black)
    ctx.setLineWidth(stroke)
    ctx.setLineJoin(.round)
    ctx.strokePath()

    // The yolk carries the state: filled = active, hollow = idle. Legible at
    // 16pt in a way a colour or badge change would not be.
    let yolk = yolkRect(in: box)
    ctx.setFillColor(black)
    if filled {
        ctx.addEllipse(in: yolk)
        ctx.fillPath()
    } else {
        ctx.addEllipse(in: yolk.insetBy(dx: stroke / 2, dy: stroke / 2))
        ctx.setStrokeColor(black)
        ctx.setLineWidth(stroke)
        ctx.strokePath()
    }
    return ctx
}

// MARK: - Asset catalogue

let root = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : ".")
let catalog = root.appendingPathComponent("App/Yolk/Assets.xcassets")
let fm = FileManager.default

func writeJSON(_ text: String, to dir: URL) {
    try! fm.createDirectory(at: dir, withIntermediateDirectories: true)
    try! text.data(using: .utf8)!.write(to: dir.appendingPathComponent("Contents.json"))
}

writeJSON(#"{"info":{"author":"xcode","version":1}}"#, to: catalog)

// App icon: every size macOS asks for.
let appIconDir = catalog.appendingPathComponent("AppIcon.appiconset")
try! fm.createDirectory(at: appIconDir, withIntermediateDirectories: true)
var appIconEntries: [String] = []
for (points, scales) in [(16, [1, 2]), (32, [1, 2]), (128, [1, 2]), (256, [1, 2]), (512, [1, 2])] {
    for scale in scales {
        let px = points * scale
        let name = "icon_\(points)x\(points)\(scale == 2 ? "@2x" : "").png"
        write(drawAppIcon(size: px), to: appIconDir.appendingPathComponent(name))
        appIconEntries.append(
            """
            {"filename":"\(name)","idiom":"mac","scale":"\(scale)x","size":"\(points)x\(points)"}
            """)
    }
}
writeJSON(
    """
    {"images":[\(appIconEntries.joined(separator: ","))],"info":{"author":"xcode","version":1}}
    """, to: appIconDir)

// Menu bar template images.
for (name, filled) in [("MenuBarActive", true), ("MenuBarIdle", false)] {
    let dir = catalog.appendingPathComponent("\(name).imageset")
    try! fm.createDirectory(at: dir, withIntermediateDirectories: true)
    var entries: [String] = []
    for scale in [1, 2] {
        let file = "\(name)\(scale == 2 ? "@2x" : "").png"
        write(drawMenuBarIcon(size: 18 * scale, filled: filled), to: dir.appendingPathComponent(file))
        entries.append(#"{"filename":"\#(file)","idiom":"mac","scale":"\#(scale)x"}"#)
    }
    writeJSON(
        """
        {"images":[\(entries.joined(separator: ","))],\
        "info":{"author":"xcode","version":1},\
        "properties":{"template-rendering-intent":"template"}}
        """, to: dir)
}

print("wrote \(catalog.path)")
