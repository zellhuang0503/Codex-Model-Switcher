#!/bin/bash
set -euo pipefail

# 取得專案路徑
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
APP_NAME="Codex Model Switcher"
APP_BUNDLE="$SCRIPT_DIR/$APP_NAME.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
EXECUTABLE_NAME="CodexModelSwitcher"

echo "=========================================="
echo " 開始編譯與打包 $APP_NAME.app"
echo "=========================================="

# 清理舊建置
rm -rf "$APP_BUNDLE"
mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

# 編譯 Swift 原始碼
echo "▶ 正在編譯 Swift 原始碼..."
swiftc -O -parse-as-library \
    "$SCRIPT_DIR/Sources/KeychainVault.swift" \
    "$SCRIPT_DIR/Sources/ProviderCatalog.swift" \
    "$SCRIPT_DIR/Sources/CodexConfigManager.swift" \
    "$SCRIPT_DIR/Sources/ConnectionTester.swift" \
    "$SCRIPT_DIR/Sources/AppView.swift" \
    "$SCRIPT_DIR/Sources/main.swift" \
    -o "$MACOS_DIR/$EXECUTABLE_NAME" \
    -framework Foundation \
    -framework SwiftUI \
    -framework AppKit \
    -framework Security

chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"
echo "✔ 二進位執行檔編譯完成！"

# 建立 PkgInfo
echo "APPL????" > "$CONTENTS_DIR/PkgInfo"

# 建立 Info.plist
cat << 'EOF' > "$CONTENTS_DIR/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>CodexModelSwitcher</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>com.codex.model-switcher</string>
    <key>CFBundleName</key>
    <string>Codex Model Switcher</string>
    <key>CFBundleDisplayName</key>
    <string>Codex 多模型切換器</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.1</string>
    <key>CFBundleVersion</key>
    <string>2</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.developer-tools</string>
</dict>
</plist>
EOF

# 產生應用程式圖示 (AppIcon.icns)
echo "▶ 正在生成應用程式圖示..."
ICONSET_DIR="/tmp/CodexModelSwitcher.iconset"
rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"

# 透過 Swift / CoreGraphics 即時繪製現代感 App 圖示
cat << 'EOF' > /tmp/generate_icon.swift
import Cocoa

let size = CGSize(width: 1024, height: 1024)
let image = NSImage(size: size)
image.lockFocus()

guard let ctx = NSGraphicsContext.current?.cgContext else { exit(1) }

// 背景漸層圓角矩形 (Squircle)
let rect = CGRect(origin: .zero, size: size)
let roundedPath = NSBezierPath(roundedRect: rect.insetBy(dx: 60, dy: 60), xRadius: 210, yRadius: 210)
roundedPath.addClip()

let colorSpace = CGColorSpaceCreateDeviceRGB()
let colors = [
    NSColor(calibratedRed: 0.10, green: 0.14, blue: 0.22, alpha: 1.0).cgColor,
    NSColor(calibratedRed: 0.16, green: 0.28, blue: 0.58, alpha: 1.0).cgColor
] as CFArray
if let gradient = CGGradient(colorsSpace: colorSpace, colors: colors, locations: [0.0, 1.0]) {
    ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: 1024), end: CGPoint(x: 1024, y: 0), options: [])
}

// 繪製中心圖形：模型切換雙向旋轉箭頭與晶片節點
ctx.saveGState()
ctx.setLineWidth(44)
ctx.setLineCap(.round)
ctx.setLineJoin(.round)
NSColor(calibratedRed: 0.35, green: 0.65, blue: 1.0, alpha: 1.0).setStroke()

let center = CGPoint(x: 512, y: 512)
let radius: CGFloat = 220

// 弧線 1
let arc1 = NSBezierPath()
arc1.appendArc(withCenter: center, radius: radius, startAngle: 40, endAngle: 140, clockwise: false)
arc1.lineWidth = 42
arc1.lineCapStyle = .round
arc1.stroke()

// 箭頭 1
let arrow1 = NSBezierPath()
arrow1.move(to: CGPoint(x: 512 + radius * cos(140 * .pi / 180) - 20, y: 512 + radius * sin(140 * .pi / 180) + 40))
arrow1.line(to: CGPoint(x: 512 + radius * cos(140 * .pi / 180), y: 512 + radius * sin(140 * .pi / 180)))
arrow1.line(to: CGPoint(x: 512 + radius * cos(140 * .pi / 180) + 45, y: 512 + radius * sin(140 * .pi / 180) + 15))
arrow1.lineWidth = 42
arrow1.lineCapStyle = .round
arrow1.lineJoinStyle = .round
arrow1.stroke()

// 弧線 2
let arc2 = NSBezierPath()
arc2.appendArc(withCenter: center, radius: radius, startAngle: 220, endAngle: 320, clockwise: false)
arc2.lineWidth = 42
arc2.lineCapStyle = .round
arc2.stroke()

// 箭頭 2
let arrow2 = NSBezierPath()
arrow2.move(to: CGPoint(x: 512 + radius * cos(320 * .pi / 180) + 20, y: 512 + radius * sin(320 * .pi / 180) - 40))
arrow2.line(to: CGPoint(x: 512 + radius * cos(320 * .pi / 180), y: 512 + radius * sin(320 * .pi / 180)))
arrow2.line(to: CGPoint(x: 512 + radius * cos(320 * .pi / 180) - 45, y: 512 + radius * sin(320 * .pi / 180) - 15))
arrow2.lineWidth = 42
arrow2.lineCapStyle = .round
arrow2.lineJoinStyle = .round
arrow2.stroke()

// 中心發光點
NSColor.white.setFill()
let centerDot = NSBezierPath(ovalIn: CGRect(x: 472, y: 472, width: 80, height: 80))
centerDot.fill()

image.unlockFocus()

guard let tiffData = image.tiffRepresentation,
      let bitmap = NSBitmapImageRep(data: tiffData),
      let pngData = bitmap.representation(using: .png, properties: [:]) else {
    exit(1)
}
try? pngData.write(to: URL(fileURLWithPath: "/tmp/app_icon_1024.png"))
EOF

swift /tmp/generate_icon.swift

if [ -f "/tmp/app_icon_1024.png" ]; then
    sips -z 16 16     /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_16x16.png" >/dev/null 2>&1 || true
    sips -z 32 32     /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_16x16@2x.png" >/dev/null 2>&1 || true
    sips -z 32 32     /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_32x32.png" >/dev/null 2>&1 || true
    sips -z 64 64     /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_32x32@2x.png" >/dev/null 2>&1 || true
    sips -z 128 128   /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_128x128.png" >/dev/null 2>&1 || true
    sips -z 256 256   /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null 2>&1 || true
    sips -z 256 256   /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_256x256.png" >/dev/null 2>&1 || true
    sips -z 512 512   /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null 2>&1 || true
    sips -z 512 512   /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_512x512.png" >/dev/null 2>&1 || true
    sips -z 1024 1024 /tmp/app_icon_1024.png --out "$ICONSET_DIR/icon_512x512@2x.png" >/dev/null 2>&1 || true

    iconutil -c icns "$ICONSET_DIR" -o "$RESOURCES_DIR/AppIcon.icns" >/dev/null 2>&1 || true
    rm -rf "$ICONSET_DIR" /tmp/generate_icon.swift /tmp/app_icon_1024.png
fi

# 進行本機 ad-hoc 簽章
echo "▶ 正在套用應用程式簽章..."
codesign --force --deep --sign - "$APP_BUNDLE" >/dev/null 2>&1 || true

# 移除隔離屬性（若有），確保本機能直接雙擊
xattr -cr "$APP_BUNDLE" 2>/dev/null || true

echo "=========================================="
echo " 打包完成！"
echo " 應用程式路徑：$APP_BUNDLE"
echo " 您可以直接在 Finder 中雙擊「$APP_NAME.app」開啟圖形化介面。"
echo "=========================================="
