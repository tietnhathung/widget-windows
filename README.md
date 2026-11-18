# AI Quota — widget Windows 11 x64

Card trong Win + W hiển thị **phần trăm còn lại** theo từng cửa sổ quota. Mở lại bảng hoặc bấm **Làm mới** để lấy dữ liệu. Mỗi lần mở bảng có thể dùng lại kết quả trong vòng một phút.

## Ghim card

Cần Windows 11 x64, Windows Web Experience Pack, Windows App Runtime 1.8 x64 và .NET 8 Runtime x64. Bản dành cho phát triển dùng thư mục package đã giải nén. Bật **Developer Mode** trong Windows Settings trước khi đăng ký.

Chạy trong PowerShell, tại thư mục project. Nếu đã có bản build trong `dist`, chỉ cần chạy hai dòng cuối.

```powershell
dotnet publish QuotaWidget.csproj -c Release --no-self-contained -o dist
New-Item -ItemType Directory -Force dist/Public | Out-Null
Add-AppxPackage -Register (Resolve-Path dist/AppxManifest.xml)
```

Mở **Win + W → Thêm widget → AI Quota → Ghim**. Giữ thư mục `dist` tại vị trí đã đăng ký. Để tự build, dùng .NET 8 SDK.

## Nguồn số liệu

- **ChatGPT · Work/Codex** dùng Codex CLI đã đăng nhập, qua `app-server` và `account/rateLimits/read`. Widget hiện hạn mức chính tài khoản trả về. Các hạn mức riêng của ChatGPT như tạo ảnh và tải file có phạm vi khác.
- **Antigravity** gọi `agy -p /usage --output-format json` và hiển thị các nhóm model do CLI trả về. Cần cài và đăng nhập Antigravity CLI.
- **Claude** dùng hai trường quota từ status line của Claude Code. Mốc thời gian sau phần trăm là lúc nhận dữ liệu. Widget bỏ qua cửa sổ quota đã qua giờ reset. Hạn mức Claude trong Antigravity thuộc tài khoản Google và nằm ở mục Antigravity.

### Kết nối Claude Code

Trong `~/.claude/settings.json`, thêm mục sau vào object cấu hình đang có, thay đường dẫn bằng đường dẫn tuyệt đối tới bản build. Claude Code gọi lệnh này khi cập nhật status line. Nếu đang dùng status line riêng, chuyển cùng payload stdin cho lệnh này trong script hiện tại và giữ phần hiển thị cũ.

```json
{
  "statusLine": {
    "type": "command",
    "command": "dotnet \"C:/Users/hungtn/WorkSpaces/widget-windows/dist/QuotaWidget.dll\" --claude-status"
  }
}
```

Mở Claude Code bằng tài khoản có usage limits. Sau khi Claude Code gửi `rate_limits`, bấm **Làm mới** trên card. Chỉ các trường phần trăm và thời gian reset được lưu ở `~/.claude/quota-widget.json`.

## Kiểm tra

```powershell
dotnet dist/QuotaWidget.dll --self-test
dotnet dist/QuotaWidget.dll --probe
```

Lệnh đầu kiểm tra dữ liệu trống, quota hết và cửa sổ đã reset. Lệnh sau đọc số liệu từ tài khoản hiện tại giống widget.

[Tài liệu Codex App Server](https://developers.openai.com/codex/app-server/) · [Antigravity /usage](https://antigravity.google/docs/cli/commands/usage/) · [Claude Code status line](https://code.claude.com/docs/en/statusline)
