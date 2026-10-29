// Writes a Windows .ico from a large PNG. Sizes up to 64 px are stored as 32-bit DIBs, which every
// Windows API and System.Drawing.Icon can read; 256 px is stored as PNG to keep the file small.
// Usage: swift make-ico.swift <source.png> <out.ico>
import AppKit

let args = CommandLine.arguments
guard args.count == 3, let source = NSImage(contentsOfFile: args[1]) else {
    FileHandle.standardError.write("usage: swift make-ico.swift <source.png> <out.ico>\n".data(using: .utf8)!)
    exit(1)
}

func render(_ size: Int) -> NSBitmapImageRep {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bitmapFormat: [], bytesPerRow: size * 4, bitsPerPixel: 32)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    source.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
    NSGraphicsContext.restoreGraphicsState()
    return rep
}

extension Data {
    mutating func le16(_ v: Int) { var x = UInt16(v).littleEndian; Swift.withUnsafeBytes(of: &x) { append(contentsOf: $0) } }
    mutating func le32(_ v: Int) { var x = UInt32(v).littleEndian; Swift.withUnsafeBytes(of: &x) { append(contentsOf: $0) } }
}

/// BITMAPINFOHEADER, bottom-up BGRA rows, then a 1-bit AND mask that is all zero (alpha does the work).
func dib(_ rep: NSBitmapImageRep, _ size: Int) -> Data {
    var d = Data()
    d.le32(40); d.le32(size); d.le32(size * 2); d.le16(1); d.le16(32); d.le32(0)
    d.le32(size * size * 4); d.le32(0); d.le32(0); d.le32(0); d.le32(0)
    let px = rep.bitmapData!
    for y in stride(from: size - 1, through: 0, by: -1) {
        for x in 0..<size {
            let i = y * rep.bytesPerRow + x * 4
            let a = px[i + 3]
            // NSBitmapImageRep stores premultiplied RGBA; ICO wants straight BGRA.
            func un(_ c: UInt8) -> UInt8 { a == 0 ? 0 : UInt8(min(255, Int(c) * 255 / Int(a))) }
            d.append(contentsOf: [un(px[i + 2]), un(px[i + 1]), un(px[i]), a])
        }
    }
    let maskRow = ((size + 31) / 32) * 4
    d.append(Data(count: maskRow * size))
    return d
}

let sizes = [16, 20, 24, 32, 40, 48, 64, 256]
let images: [Data] = sizes.map { size in
    let rep = render(size)
    return size == 256 ? rep.representation(using: .png, properties: [:])! : dib(rep, size)
}

var out = Data()
out.le16(0); out.le16(1); out.le16(sizes.count)
var offset = 6 + 16 * sizes.count
for (size, image) in zip(sizes, images) {
    out.append(UInt8(size == 256 ? 0 : size)); out.append(UInt8(size == 256 ? 0 : size))
    out.append(0); out.append(0)
    out.le16(1); out.le16(32)
    out.le32(image.count); out.le32(offset)
    offset += image.count
}
images.forEach { out.append($0) }
try! out.write(to: URL(fileURLWithPath: args[2]))
