# ScienceWord Overlay Pro (C# WPF - Toàn màn hình trong suốt)

Ứng dụng vẽ hình học phẳng, dựng hình, tô miền, đánh dấu và chèn khối 3D trực tiếp trong **lớp trong suốt toàn màn hình** đè lên Microsoft Word, PDF, PowerPoint hoặc Trình duyệt Web.

## Tính năng chính
- **Lớp trong suốt toàn màn hình + Chế độ Xuyên chuột (`Xuyên chuột`):** Bật để cuộn trang PDF/gõ Word bình thường, tắt để vẽ & tô trực tiếp lên màn hình.
- **Công cụ hình học & Dụng cụ ảo:** Đoạn thẳng (kèm thước ảo đo cm), Nét đứt 3D, Vectơ, Tam giác, HCN, Đường tròn (kèm compa ảo đo R), Trung điểm, Hạ đường cao `⊥` tự gắn góc vuông.
- **Tô màu chuyên nghiệp:** Tô miền đa giác (`Tô đặc`, `Gạch chéo ///`, `Ca rô ###`, `Chấm bi •••`), Tô góc, Bút viết tay mượt, Bút dạ quang, chỉnh độ mờ Opacity.
- **Chèn nhanh khối 3D SGK:** Chóp tứ giác `S.ABCD`, Lập phương `ABCD.A'B'C'D'`.
- **Xuất sang Microsoft Word (1-Click):** Nút `Copy vào Word` (hoặc `Ctrl + C`) tự cắt sát vùng vẽ ở độ phân giải 2x (hỗ trợ nền trong suốt hoặc nền trắng) để dán trực tiếp vào Word bằng `Ctrl + V`.

## Cách biên dịch tự động ra file `.EXE` trên GitHub (Không cần cài Visual Studio)
1. Tạo một Repository mới trên GitHub (ví dụ: `ScienceWord-Overlay-Pro`).
2. Giải nén file ZIP và đẩy toàn bộ thư mục (bao gồm cả thư mục ẩn `.github/workflows/build.yml`) lên nhánh `main`.
3. Vào tab **Actions** trên GitHub -> chọn workflow **Build ScienceWord Overlay Pro (C# WPF)** (đợi khoảng 1–2 phút).
4. Kéo xuống mục **Artifacts** để tải về file `.exe`:
   - `ScienceWord_Overlay_Pro_Portable_win-x64`: File `.exe` độc lập hoàn toàn, chạy trên mọi máy Windows 10/11 không cần cài .NET.
   - `ScienceWord_Overlay_Pro_Lite_win-x64`: File `.exe` siêu nhẹ (~200 KB) cho máy đã có sẵn .NET 8 Desktop Runtime.

## Biên dịch trực tiếp trên máy tính cá nhân
Cài [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) và nháy đúp file **`build_local.bat`**.
