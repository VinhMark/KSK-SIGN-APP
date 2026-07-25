# Tool đẩy GKSK Lái xe v1.0

Windows desktop app (WPF, .NET 8) theo yêu cầu đã chốt.

## Chức năng đã có

- Cấu hình SQL đúng 5 trường: Server, Port, Database, Username, Password.
- Password SQL được mã hóa bằng Windows DPAPI theo user đang đăng nhập.
- Lấy dữ liệu từ SQL theo ngày.
- Checkbox "Chỉ hồ sơ đã thanh toán" dựa trên `SuggestedServiceReceipt.Paid = 1`.
- Chỉ lấy gói có `ServicePackage.ServicePackageName LIKE N'%KSK LÁI XE%'`.
- Liên kết chính:
  - `SuggestedServiceReceipt.RefID = PatientReceive.PatientReceiveID`
  - `PatientReceive.PatientCode = Patients.PatientCode`
  - `SuggestedServiceReceipt.ServicePackageCode = ServicePackage.ServicePackageCode`
- Thông tin cơ sở lấy `TOP 1` từ `ClinicInformation`.
- Import Excel.
- Export Excel.
- Sửa trực tiếp dữ liệu trong DataGrid; không UPDATE ngược SQL.
- Khi bấm Gửi API, app luôn validate lại dữ liệu hiện tại trên DataGrid.
- Hồ sơ lỗi vẫn giữ nguyên trên danh sách và không gửi.
- Hồ sơ hợp lệ được gửi API và lưu trạng thái/MSG_TEXT/UUID trên màn hình.

## Mapping SO và Hạng bằng lái

Từ `PatientReceive.Reason`, tách tại dấu `-` cuối và TRIM:

Ví dụ:

`99999/GKSKLX/75440/26-A`

- SO = `99999/GKSKLX/75440/26`
- HANGBANGLAI = `A`

SO tối đa 21 ký tự.

## Lưu ý mapping theo cấu trúc SQL thực tế

Trong file cấu trúc database:
- Tên đúng là `Patients.PatientName`, không phải `PatientReceive.PartientName`.
- Tên đúng là `Patients.PatientGender`.
- Tên đúng là `Patients.PatientBirthday`.
- Tên đúng là `ServicePackage`, không phải `ServicePakage`.

App dùng đúng tên cột/bảng thực tế trong schema.

## API

Lần chạy đầu, app tự tạo file cấu hình API tại:

`%APPDATA%\GKSKLaiXe\api.config.json`

Nội dung:

```json
{
  "Url": "https://egw.baohiemxahoi.gov.vn/api/hssk/gksk",
  "Username": "",
  "Password": ""
}
```

Điền tài khoản API trước khi gửi. Password được MD5 ngay khi tạo header request theo tài liệu API.

`SIGNDATA` hiện lấy từ dữ liệu đang có trên DataGrid/Excel. Module ký số XML bằng token/chứng thư số chưa được tự động hóa trong bản source này vì cần xác định phương thức ký số thực tế tại máy sử dụng.

## Build

Cài:
- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 hoặc mới hơn với workload ".NET desktop development"

Mở `GKSKLaiXe.csproj`, Restore NuGet, rồi Build/Publish.

Ví dụ publish:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

File chạy nằm trong thư mục `bin\Release\net8.0-windows\win-x64\publish`.


## Fix03

- Chuyển filter cột lên trực tiếp header của DataGrid.
- Có thể nhập nhiều filter ở nhiều header cùng lúc; các điều kiện được kết hợp theo AND.
- Ô tìm kiếm trên thanh công cụ vẫn tìm nhanh toàn bộ hồ sơ.
- `Chọn tất cả` và `Bỏ chọn tất cả` chỉ tác động lên các dòng đang hiển thị sau filter.
- Đổi kiến trúc SQL thành `PatientReceive -> SuggestedServiceReceipt -> ServicePackage`.
- Một `PatientReceive` được hiển thị thành một hồ sơ; dùng `CROSS APPLY TOP (1)` để tránh lặp dòng khi có nhiều dịch vụ.
- Ngày chọn trên giao diện lọc theo `PatientReceive.CreateDate`; `NGAYKETLUAN` vẫn lấy từ `SuggestedServiceReceipt.CreateDate`.


## Fix04

- Bỏ toàn bộ TextBox filter trên header DataGrid.
- Giữ ô tìm kiếm chung trên thanh công cụ.
- Thêm nút `Cấu hình liên thông`.
- Cấu hình liên thông gồm:
  - API URL
  - Username
  - Password
- Password liên thông được mã hóa cục bộ bằng Windows DPAPI.


## Fix05

- Nút cấu hình gộp thành một nút `Cấu hình`.
- Popup cấu hình có 2 tab:
  - `Kết nối SQL`
  - `Liên thông`
- Checkbox chọn/bỏ chọn tất cả được đưa trực tiếp lên header cột `Chọn`.
- Header checkbox chỉ tác động lên các dòng đang hiển thị sau khi tìm kiếm.
- Ô tìm kiếm được chuyển xuống một hàng riêng ngay trên DataGrid.
- Thêm checkbox `Ký số`:
  - Checked: payload gửi API có trường `SIGNDATA`.
  - Unchecked: payload không có trường `SIGNDATA`.

## Fix06

- KETLUAN mặc định đổi thành `A0-1`.
- KETLUAN chỉ được phép: `A0-1`, `A0-2`, `A0-3`.
- Nếu Ký số được bật: gửi `SIGNDATA` của hồ sơ.
- Nếu Ký số không được bật: vẫn gửi `SIGNDATA = "test"`.

## Fix07 - Ký số XML theo tài liệu PDF

- STATE mặc định = `ADD`.
- Khi bật `Ký số`:
  - App mở hộp chọn chứng thư số trong Windows Certificate Store.
  - Tạo XML dữ liệu GKSK.
  - Ký XML theo XMLDSig với:
    - Reference URI rỗng.
    - Enveloped Signature.
    - RSA-SHA1.
    - Digest SHA1.
    - Nhúng RSAKeyValue và X509Certificate.
  - Chuyển toàn bộ XML đã ký sang Base64.
  - Gán vào `SIGNDATA`.
- Khi tắt `Ký số`:
  - Gửi `SIGNDATA = "test"`.

Lưu ý: USB Token cần driver/CSP hoặc KSP của nhà cung cấp để chứng thư và private key xuất hiện trong Windows Certificate Store.

## Fix09 - Tạm khóa gửi API, chuyển sang xuất XML

- Tạm thời bỏ nút gửi trực tiếp lên cổng giám định.
- Nút `Gửi API hồ sơ đã chọn` đổi thành `Xuất XML hồ sơ đã chọn`.
- App vẫn validate hồ sơ trước khi xuất.
- Mỗi hồ sơ hợp lệ xuất thành một file XML riêng.
- Nếu bật `Ký số`: xuất XML đã ký bằng chứng thư số đã chọn.
- Nếu tắt `Ký số`: xuất XML chưa ký.
- Tên file ưu tiên dùng `SO`; nếu trùng tên sẽ tự thêm hậu tố `_1`, `_2`, ...

## Fix10 - Thêm tag SIGNDATA vào XML xuất ra

Cấu trúc XML xuất cuối cùng có thêm:

`<SIGNDATA>...</SIGNDATA>`

Quy tắc:
- Bật `Ký số`:
  - Tạo XML dữ liệu ký theo tài liệu.
  - Ký XML.
  - Base64 toàn bộ XML đã ký.
  - Gán chuỗi Base64 đó vào tag `SIGNDATA` của XML xuất cuối cùng.
- Tắt `Ký số`:
  - `SIGNDATA = test`.

Như vậy file XML xuất ra mô phỏng trực tiếp bộ dữ liệu cuối cùng sẽ dùng để gửi cổng.

## Fix11 - SIGNDATA Base64 của root

- Khi bật Ký số: `SIGNDATA = Base64(UTF8(XML <root> dữ liệu gốc))`.
- Không còn gán Base64 của XML đã nhúng thẻ `<Signature>` vào `SIGNDATA`.
- Khi tắt Ký số: `SIGNDATA = test`.

## Fix12 - SIGNDATA = Base64 của root đã ký

- Khi bật Ký số: tạo XML `<root>...</root>`, ký XML và nhúng `<Signature>` vào bên trong `<root>`.
- Sau đó `SIGNDATA = Base64(UTF8(toàn bộ <root> đã có <Signature>))`.
- Khi giải mã Base64 của SIGNDATA sẽ thấy `<root>` và thẻ `<Signature>`.
- Khi tắt Ký số: `SIGNDATA = test`.

## Fix13 - Xuất JSON

- Đổi chức năng `Xuất XML` thành `Xuất JSON`.
- Mỗi hồ sơ hợp lệ xuất thành một file `.json`.
- JSON giữ đúng các trường payload dự kiến gửi API.
- Khi bật Ký số:
  - Tạo XML `<root>` đã ký và có `<Signature>`.
  - Base64 toàn bộ XML root đã ký.
  - Gán vào trường JSON `SIGNDATA`.
- Khi tắt Ký số: `SIGNDATA = "test"`.

## Fix14 - Giao diện & Unicode JSON

- JSON dùng `UnsafeRelaxedJsonEscaping`, nên tiếng Việt hiển thị trực tiếp thay vì chuỗi `\uXXXX`.
- Giữ encoding UTF-8 không BOM khi ghi file JSON.
- Làm lại theme giao diện:
  - Segoe UI.
  - Thanh công cụ sạch hơn.
  - Nút Primary / Secondary.
  - DataGrid xen kẽ dòng, header rõ ràng.
  - Màu nền và border hiện đại hơn.
  - Cửa sổ có kích thước tối thiểu để tránh vỡ bố cục.

## Fix15 - Chốt giao diện hiện tại

- Giữ nguyên toàn bộ tính năng hiện tại.
- Quay lại giao diện đơn giản trước bản Professional UI.
- Bỏ các nút `Tìm` và `Xóa tìm kiếm`.
- Ô tìm kiếm lọc trực tiếp khi gõ.
- Chuyển `Ký số` và nút `Xuất JSON hồ sơ đã chọn` sang phía bên phải thanh công cụ.

## Fix16

- Thêm lại nút `Tìm kiếm`.
- Khi bấm `Tìm kiếm` hoặc nhấn Enter, dữ liệu trên bảng được lọc theo từ khóa.
- Không còn lọc ngay trong lúc gõ.
- Đảo mapping giới tính từ dữ liệu SQL:
  - SQL = `1` -> API/Table = `0` (Nam)
  - SQL = `0` -> API/Table = `1` (Nữ)

## Fix17 - Nhật ký đã gửi & ComboBox nghiệp vụ

### Nhật ký đã gửi bằng SQLite
- App tạo file SQLite cục bộ tại `%APPDATA%\GKSKLaiXe\sent_history.db`.
- Khóa nhận diện hồ sơ: `SO + HANGBANGLAI`.
- Khi load dữ liệu SQL của ngày đang chọn:
  - Nếu `SO + HANGBANGLAI` đã có trong SQLite của ngày đó -> `IsSent = true`.
  - Checkbox `Đã gửi` bật -> chỉ hiển thị hồ sơ đã gửi.
  - Checkbox `Đã gửi` tắt -> chỉ hiển thị hồ sơ chưa gửi.
- Đã chuẩn bị hàm `MarkSent(...)` để dùng khi bật lại chức năng gửi API; chỉ nên gọi sau khi API trả thành công.

### ComboBox trên TableView
- `STATE`: `ADD`, `EDIT`.
- `MATUY`:
  - `0 : Âm tính`
  - `1 : Dương tính`
  - Mặc định `0`.
- `KETLUAN`:
  - `A0-1`
  - `A0-2`
  - `A0-3`
- `HANGBANGLAI`:
  - `A`, `A.03`, `B`, `B1`, `B0.1`, `BE`,
  - `C`, `C1`, `CE`, `C1E`,
  - `D`, `D2`, `D2E`, `DE`.

## Fix18 - Chốt bố cục giao diện

- Checkbox chọn hồ sơ mặc định bỏ chọn.
- Click vào dòng sẽ highlight toàn bộ dòng.
- Ẩn cột `Paid` khỏi TableView.
- Nút `Tìm kiếm` thu nhỏ.
- Bỏ checkbox `Ký số`; app mặc định luôn ký số khi xuất JSON.
- Checkbox thanh toán đổi nhãn thành `Thanh toán`.
- Các nút `Cấu hình`, `Import Excel`, `Export Excel`, `Kiểm tra`, `Xuất JSON` chuyển sang nhóm bên phải.

## Fix19 - Chọn nhiều gói và lọc ngay khi Load SQL

- Thêm nút `Chọn gói`.
- Danh sách gói lấy trực tiếp từ `ServicePackage`.
- Chỉ liệt kê các gói có tên chứa `KSK LÁI XE`.
- Cho phép chọn nhiều gói, chọn tất cả, bỏ chọn tất cả.
- Khi bấm `Lấy dữ liệu SQL`, query chỉ lấy các gói đã chọn.
- Nếu không chọn gói nào, mặc định lấy tất cả gói có tên chứa `KSK LÁI XE`.

## Fix20 - Chọn gói là điều kiện bắt buộc

- Bỏ hoàn toàn điều kiện tên gói phải chứa `KSK LÁI XE`.
- Popup `Chọn gói` lấy tất cả gói trong `ServicePackage` có `IsHide = false`.
- Thêm ô `Tìm tên gói` ngay trên danh sách gói.
- Có thể lọc tên gói rồi chọn/bỏ chọn tất cả các gói đang hiển thị.
- Khi bấm `Lấy dữ liệu SQL`, app bắt buộc phải có ít nhất một gói được chọn.
- Query chỉ lấy các hồ sơ thuộc đúng các gói đã chọn.

## Fix21
- Sửa tên cột lọc gói từ `IsHide` thành đúng cột `Hide`.
- Popup chọn gói lấy các gói có `Hide = false`.
- Nút `Tìm kiếm` được thu gọn và đặt sát ngay bên cạnh ô nhập tìm kiếm.

## Fix22 - IntroName
- Hiển thị thêm cột `BS giới thiệu`, lấy trực tiếp từ `PatientReceive.IntroName`.
- Chỉ hiển thị trên bảng, chưa đưa vào JSON/API.

## Fix23 - SO lên đầu và nhớ gói đã chọn

- Chuyển cột `SO` lên ngay sau cột `Chọn`.
- Freeze 2 cột đầu: `Chọn` và `SO`, nên khi kéo ngang `SO` luôn được giữ cố định.
- Lưu danh sách gói đã chọn vào `%APPDATA%\GKSKLaiXe\packages.config.json`.
- Khi mở app lần sau, danh sách gói đã chọn được tự động khôi phục.
- Khi người dùng bấm `Áp dụng` trong popup Chọn gói, cấu hình được lưu ngay.

## Fix25 - Menu điều hướng màn hình

- Thanh menu trên cùng gồm:
  - `Liên thông KSK`
  - `Dữ liệu Xml`
  - `Cấu hình`
- Bấm `Liên thông KSK` -> hiển thị màn hình chức năng liên thông.
- Bấm `Dữ liệu Xml` -> hiển thị màn hình chức năng XML.
- `Cấu hình` được chuyển lên thanh menu.
- Hai màn hình dùng TabControl ẩn header để chuyển trang, không mở cửa sổ mới.

## Fix26 - Sửa lỗi MC3009 TabItem only one child

- Mỗi `TabItem` của màn hình chính được bọc trong một `Grid`.
- Khắc phục lỗi:
  `The object 'TabItem' already has a child and cannot add 'Border'.`
- Giữ nguyên menu điều hướng:
  - Liên thông KSK
  - Dữ liệu Xml
  - Cấu hình

## Fix27 - Tích hợp thật màn hình Dữ liệu XML

- Loại bỏ màn hình placeholder `Màn hình Dữ liệu XML`.
- `Dữ liệu Xml` hiện mở đầy đủ màn hình:
  - Bộ lọc Ngày
  - Từ ngày - Đến ngày
  - Quý
  - Năm
  - MA_LK
  - XML1 / XML2 / XML3
  - Xuất Excel
- Dùng chung cấu hình SQL của chức năng Liên thông KSK.
- Menu trên cùng:
  - Liên thông KSK
  - Dữ liệu Xml
  - Cấu hình
- Bỏ nút Cấu hình trùng trong màn hình Liên thông KSK.
- Sửa query XML1 dùng `PatientReceive.PartientName`.

## Fix28 - Khôi phục giao diện KSK và sửa cột Patients

- Màn hình `Liên thông KSK` dùng lại `DockPanel`, nên các thanh chức năng/tìm kiếm/status hiển thị đúng như giao diện cũ.
- Sửa XML1 theo đúng schema:
  - `Patients.PatientName`
  - `Patients.PatientBirthday`
  - `Patients.PatientGender`
- Không còn dùng các tên sai `PartientName`, `PartientBirtday`, `PartientGender`.

## Fix29 - Sửa XML2 Items_BHYT_PK

Lỗi:
- `ItemName_BYT_XML`
- `UsageCode_BYT_XML`

Nguyên nhân:
Hai cột này nằm trong `dbo.Items_BHYT_PK`, không nằm trong `dbo.Items`.

Đã sửa:
- JOIN `Items_BHYT_PK` bằng `ServiceCode_PK / ItemCode_PK`.
- `TEN_THUOC` ưu tiên `Items_BHYT_PK.ItemName_BYT_XML`.
- `DUONG_DUNG` ưu tiên `Items_BHYT_PK.UsageCode_BYT_XML`.
- `MA_THUOC` ưu tiên `Items_BHYT_PK.MaBYT_PK`.
- `SO_DANG_KY` ưu tiên `Items_BHYT_PK.SODKGP`.

## Fix30 - Mapping BHYT/ICD + cột đầu XML2/XML3 + chống duplicate

### XML1
- `BHYT = ReportBHYT.Serial`.
- `ICD10 = ReportBHYT.ICD10_Custom`.
- `ICD10KT = ReportBHYT.ICD10_KT`.
- Chống trùng `MA_LK` bằng `ROW_NUMBER()`, ưu tiên hồ sơ chưa hủy và bản ghi mới nhất.

### XML2 / XML3
Thêm các cột đầu:
- `MA_LK`
- `MA_BN`
- `HO_TEN`
- `NGAY_SINH`
- `BHYT`

### XML2 duplicate
- Khử trùng theo tổ hợp:
  `MA_LK + ServiceCode/ServiceCode_PK + SuggestedID + Quantity + BHYTPrice + NGAY_YL`.
- Giữ dòng có `Ordinal` nhỏ nhất.

## Fix31 - Liên kết ReportID, hiển thị MA_LK

- XML2/XML3 liên kết cha-con duy nhất bằng:
  `ReportBHYT.ReportID = ReportBHYTDetail.ReportID`.
- `MA_LK` vẫn là cột đầu để hiển thị, lọc và xuất dữ liệu.
- Bỏ khử duplicate thuốc theo tổ hợp nghiệp vụ vì có thể xóa nhầm dòng hợp lệ.
- XML2 dùng `OUTER APPLY TOP 1` với `Items_BHYT_PK` để tránh nhân bản dòng do nhiều mapping danh mục.

## Fix32 - Sửa XML2 bị nhân đôi số lượng thuốc

Nguyên nhân:
- Một `MA_LK` có thể có nhiều dòng `ReportBHYT` / nhiều `ReportID`.
- XML1 chỉ giữ một hồ sơ cha, nhưng XML2 trước đây lấy chi tiết của tất cả `ReportID`
  thuộc cùng `MA_LK`, làm một số thuốc xuất hiện gấp đôi.

Cách sửa:
- Chọn đúng một hồ sơ cha `ReportBHYT` cho mỗi `MA_LK` bằng `ROW_NUMBER()`.
- Sau đó XML2 chỉ JOIN `ReportBHYTDetail` theo `ReportID` của hồ sơ cha đã chọn.
- Không dùng DISTINCT hay SUM để che trùng, nên các dòng thuốc hợp lệ trong cùng một ReportID vẫn được giữ nguyên.

## Fix33 - XML2/XML3 lấy trực tiếp từ tập XML1

- Bộ lọc ngày và MA_LK chỉ áp dụng khi tạo tập hồ sơ cha `XML1_DATA`.
- XML1 chọn đúng một `ReportID` cho mỗi `MA_LK` trong `XML1_SELECTED`.
- XML2 và XML3 không lọc ngày lần nữa.
- XML2/XML3 chỉ JOIN:
  `XML1_SELECTED.ReportID = ReportBHYTDetail.ReportID`.
- `MA_LK` vẫn lấy từ tập XML1 và hiển thị ở cột đầu.

## Fix34 - Professional UI

- Giữ nguyên toàn bộ logic Fix33.
- Thanh menu tối, rõ 3 module chính.
- Nút primary/secondary thống nhất.
- DataGrid hiện đại hơn, header rõ, dòng xen kẽ.
- Nền ứng dụng sáng nhẹ, các khu vực chức năng dạng card.
- Màn hình XML và Liên thông KSK dùng cùng hệ thống giao diện.

## Fix35 - Sửa lỗi XAML MC3000

- Sửa ký tự `&` trong `MainWindow.xaml` thành `&amp;`.
- Khắc phục lỗi:
  `An error occurred while parsing EntityName. XML is not valid.`
- Giữ nguyên toàn bộ giao diện Professional UI và logic Fix33.

## Fix36 - Thống kê tổng dưới bảng XML

Màn hình `Dữ liệu Xml` hiển thị thêm thanh tổng hợp:
- Tổng HS
- Tổng Chi
- BHTT
- BNTT

Cách tính:
- Tổng HS = số dòng XML1.
- Tổng Chi = tổng `T_TONGCHI_BV` của XML1.
- BHTT = tổng `T_BHTT` của XML1.
- BNTT = tổng `T_BNTT` của XML1.

Dùng XML1 làm nguồn thống kê để tránh cộng trùng theo số dòng thuốc XML2 hoặc DVKT XML3.

## Fix37 - Bộ lọc Tháng & chỉnh Summary/UI

- BNTT lấy từ `T_BNCCC`.
- DataGrid giới hạn `MaxColumnWidth = 300`.
- `Từ ngày` và `Đến ngày` mặc định đều là ngày hiện tại.
- Nút `Xuất Excel` chuyển sang bên phải thanh bộ lọc.
- Thêm kiểu lọc `Tháng` với chọn Tháng + Năm.

## Fix38 - BNTT & căn chỉnh hiển thị

- `BNTT` lấy từ cột `tBNTraBH`.
- Excel xuất ra giới hạn độ rộng cột tối đa khoảng 300px.
- Toàn bộ text/control căn giữa theo chiều dọc.
- Các ô DataGrid căn giữa theo chiều dọc.
- Dữ liệu Excel căn giữa theo chiều dọc.

## Fix39 - TBNTT và căn giữa row

- XML1 thêm cột `TBNTT = ReportBHYT.BNTraBH`.
- Thanh tổng hợp `BNTT` lấy tổng từ cột `TBNTT`.
- Tất cả DataGrid/TableView:
  - nội dung từng row căn giữa theo chiều dọc;
  - TextBlock, CheckBox và ComboBox trong cell đều căn giữa theo chiều dọc.

## Fix40 - XML1 lọc hồ sơ Cancel = 0

- Thêm điều kiện `rb.Cancel = 0` trong bộ lọc tạo XML1.
- XML2/XML3 tiếp tục lấy `ReportID` từ tập XML1 đã chọn.
- Vì vậy các hồ sơ bị hủy sẽ tự động không xuất hiện trong XML2/XML3.

## Fix41 - Mở lại đẩy cổng giám định

- Thay nút `Xuất JSON hồ sơ đã chọn` bằng `Đẩy cổng giám định`.
- Trước khi gửi:
  - Commit dữ liệu đang sửa trên bảng.
  - Quét Validation từng hồ sơ.
  - Chỉ gửi hồ sơ hợp lệ.
  - Bắt buộc chọn chứng thư số.
- `SIGNDATA` là Base64 của XML root đã có thẻ `Signature`.
- Gửi qua `ApiService.SendAsync`.
- Chỉ khi API trả thành công mới ghi SQLite `SentHistory`.
- Lưu `UUID`, thông báo API và trạng thái gửi trên từng hồ sơ.
- Hồ sơ thành công tự chuyển sang trạng thái `Đã gửi`.

## Fix42 - Xuất XML kiểm tra

- Thêm nút `Xuất XML kiểm tra` cạnh nút `Đẩy cổng giám định`.
- Chỉ xuất các hồ sơ được chọn và hợp lệ.
- Dùng đúng cùng logic ký số như khi đẩy cổng.
- File XML xuất ra là XML root đã có thẻ `Signature`.
- Đồng thời cập nhật `SIGNDATA` = Base64 của XML đã ký để đối chiếu.
- Không ghi lịch sử `Đã gửi` khi chỉ xuất XML kiểm tra.

## Fix43-XML-Speed01 - Một SQL batch cho XML1/XML2/XML3

- Tab Dữ liệu XML chỉ mở 1 kết nối và chạy 1 command.
- SQL xác định tập XML1 một lần vào `#XML1_SELECTED`.
- ResultSet 1 trả XML1.
- ResultSet 2 lấy thuốc theo `ReportID` của `#XML1_SELECTED`.
- ResultSet 3 lấy DVKT theo `ReportID` của `#XML1_SELECTED`.
- Không JOIN thuốc và DVKT vào cùng một bảng nên tránh nhân chéo dữ liệu.
- Giữ lại các hàm query cũ trong service để dễ đối chiếu/rollback.

## Fix XML SingleBatch 02 - Reader closed

Sửa lỗi:
`Invalid attempt to call NextResultAsync when reader is closed.`

Nguyên nhân:
- `DataTable.Load(reader)` có thể tiêu thụ/đóng reader sau ResultSet đầu.

Cách sửa:
- Dùng `DataSet.Load(reader, ..., "XML1", "XML2", "XML3")`
  để nạp cả 3 ResultSet trong một lần.

## App Icon

- Đã dùng hình đại ca cung cấp làm icon ứng dụng.
- Icon áp dụng cho:
  - file `.exe`
  - thanh tiêu đề cửa sổ
  - Taskbar/Alt+Tab của Windows
- File icon: `Assets/app.ico`.

## Icon runtime fix

- `Assets/app.ico` được khai báo rõ là WPF `Resource`.
- `MainWindow` dùng đường dẫn resource `/Assets/app.ico`.
- Vẫn giữ `ApplicationIcon` để icon của file EXE không thay đổi.

## UI update - Vertical center / Loading state / Gender Combo / Button icons

- Căn giữa nội dung theo chiều dọc cho toàn bộ DataGrid:
  - bảng Liên thông KSK;
  - XML1;
  - XML2;
  - XML3.
- Các cột text dùng `CenteredDataGridText`.
- Các cột XML tự sinh cũng được áp style khi `AutoGeneratingColumn`.
- Nút `Lấy dữ liệu SQL` bị disable trong suốt thời gian đang tải.
- Nút `Lấy dữ liệu` XML bị disable trong suốt thời gian đang tải.
- Cột `Giới tính` của KSK đổi sang ComboBox:
  - `0 : Nam`
  - `1 : Nữ`
- Thêm hệ thống icon cho button bằng `Segoe MDL2 Assets` có sẵn trên Windows,
  tránh thêm NuGet ngoài và tránh lỗi thiếu dependency khi chạy app.

## UI XAML Fix

- Sửa lỗi `MC3000` trong `App.xaml`.
- Loại bỏ block `Style.Resources` bị dư/mất thẻ mở.
- Gộp style `ComboBox` thành một style duy nhất.
- Giữ nguyên các thay đổi:
  - căn giữa row theo chiều dọc;
  - khóa nút khi đang load;
  - giới tính ComboBox;
  - icon cho button.

## Bulk KSK Send Progress

- Có thể chọn nhiều hồ sơ và gửi trong một lượt.
- Gửi tuần tự từng hồ sơ để ổn định và dễ theo dõi.
- Hiển thị tiến trình `đã xử lý/tổng số`.
- Một hồ sơ lỗi không làm dừng cả lô.
- Cập nhật riêng trạng thái và phản hồi API cho từng hồ sơ.
- Tự loại trùng trong cùng một lượt gửi theo `SO + HANGBANGLAI`.
- Khóa các nút gửi/xuất kiểm tra trong lúc đang gửi.

## Remove KSK test export buttons

- Bỏ nút `Xuất XML kiểm tra`.
- Bỏ nút `Xuất JSON kiểm tra`.
- Giữ nguyên nút `Đẩy cổng giám định`.
- Giữ nguyên gửi hàng loạt và thanh tiến trình.

## KSK category tabs

Trong màn hình `Liên thông KSK` có 2 tab:
- `KSK LÁI XE`
  - popup Chọn gói chỉ hiển thị tên gói có chứa `KSK LÁI XE`.
- `KSK`
  - popup Chọn gói chỉ hiển thị tên gói không chứa `LÁI XE`.

Mỗi tab lưu danh sách gói đã chọn riêng.
Khi chuyển tab, dữ liệu bảng hiện tại được xóa để tránh nhầm hồ sơ giữa hai nhóm.

## Dropdown Liên thông KSK + màn hình KSK riêng

Menu `Liên thông KSK` có 2 chức năng:
- `KSK LÁI XE`
  - giữ nguyên TableView và chức năng đẩy cổng.
  - Chọn gói chỉ lấy gói chứa `KSK LÁI XE`.
- `KSK`
  - màn hình/TableView riêng.
  - không có chức năng đẩy cổng.
  - Chọn gói chỉ lấy gói không chứa `LÁI XE`.

Cột KSK:
- Chọn
- Số KSK
- Người giới thiệu
- Tên Gói Khám
- Ngày tạo
- Loại KSK = `KHÁM SỨC KHỎE`
- Loại Sức khỏe: LOẠI 1..5
- Họ tên
- Ngày sinh
- Giới tính
- Thanh Toán (`Paid = 1` -> `Đã thanh toán`)

Excel KSK xuất đúng các cột trên.

## KSK Reason parsing / menu/search UI

- KSK thường dùng `PatientReceive.Reason` đã được tách theo dấu `-` cuối.
- Ví dụ:
  - `Reason = 00000/GKSK-VHTP-1`
  - `Số KSK = 00000/GKSK-VHTP`
  - `Loại Sức khỏe = LOẠI 1`
- Hỗ trợ hậu tố `1..5` tương ứng `LOẠI 1..5`.
- Menu dropdown `KSK LÁI XE` và `KSK` hiển thị chữ màu đen.
- Nút tìm kiếm nằm sát bên phải ô nhập tìm kiếm.

## KSK Lái xe - TableView & Excel

TableView KSK Lái xe:
- `SO` đổi thành `Số KSK`.
- `BS giới thiệu` đổi thành `Người Giới Thiệu`.
- `ServiceName` đổi thành `Gói Khám`.
- Thêm `Loại KSK = KHÁM SỨC KHỎE LÁI XE`.
- Thêm `Thanh Toán`:
  - `Paid = 1` -> `Đã thanh toán`
  - trường hợp khác -> `Chưa thanh toán`.

Excel KSK Lái xe xuất bộ cột:
- Chọn
- Số KSK
- Người Giới Thiệu
- Gói Khám
- Ngày tạo
- Loại KSK
- Hạng GPLX
- Họ tên
- Ngày sinh
- Giới tính
- Thanh Toán
- CCCD/CMND/Hộ chiếu
- Ngày cấp
- Nơi cấp
- Địa chỉ

## Import Excel KSK Lái xe tối giản + giá trị mặc định

Import Excel KSK Lái xe chỉ cần các cột:
- Số KSK
- Hạng GPLX
- Họ tên
- Giới tính
- Ngày sinh
- CCCD
- Ngày cấp
- Nơi cấp
- Địa chỉ
- Mã tỉnh
- Mã xã
- Ngày kết luận

Có hỗ trợ alias tên cột kỹ thuật cũ như `SO`, `HOTEN`, `GIOITINHVAL`...

Tab cấu hình mới `Mặc định KSK Lái xe`:
- Mã CSKCB
- Cơ sở KCB
- Ma túy
- Bác sĩ kết luận
- Kết luận
- STATE

Khi import Excel, các giá trị mặc định này tự động được gán vào từng hồ sơ.

## KSK general grid vertical centering

- TableView KSK thường căn giữa theo chiều dọc toàn bộ nội dung row.
- Áp dụng cho:
  - TextBlock
  - CheckBox
  - ComboBox Loại Sức khỏe
  - DataGridCell
  - DataGridRow
- Chỉ áp riêng cho `GeneralGrid`, không ảnh hưởng logic KSK Lái xe.

## XML TableView display update

- Giới tính hiển thị `Nam/Nữ`.
- Ẩn `MA_HUYEN`.
- Ẩn `PatientReceiveID`.
- Hiển thị `Tên bác sĩ` thay mã bác sĩ.
- Hiển thị `Tên khoa` thay mã khoa.
- Thêm `Tháng QT` dạng `MM/YYYY`.
- Các cột tiền hiển thị và xuất Excel theo `#,###.00`.
- Lookup tên bác sĩ/khoa được dò động từ schema SQL; nếu không tìm thấy danh mục phù hợp thì fallback về mã hiện có.

## XML alias binding fix

Sửa lỗi:
- `The multi-part identifier 'dl.Name' could not be bound.`
- `The multi-part identifier 'dr.Name' could not be bound.`

Nguyên nhân:
- Alias `dl` và `dr` bị dùng nhầm trong `SELECT INTO #XML1_SELECTED` trước khi JOIN lookup.

Đã sửa:
- Bảng tạm giữ `MA_KHOA`, `MA_BAC_SI`.
- Chỉ đổi sang `TEN_KHOA`, `TEN_BAC_SI` ở ResultSet XML1 sau khi JOIN lookup.

## SQL Setup - gộp Server + Port

- Ô Server nhập trực tiếp dạng:
  `SERVERVH\SQL2017,49855`
- Bỏ ô Port riêng.
- Connection string dùng nguyên giá trị Server đã nhập.
- Cửa sổ tăng chiều cao và hiển thị đầy đủ Password.
- Nút Lưu cấu hình chỉ bật sau khi Kiểm tra kết nối thành công.

## Menu Liên thông giấy nghỉ BHXH

- Thêm mục menu `Liên thông giấy nghỉ BHXH`.
- Vị trí: sau `Dữ liệu Xml`, trước `Cấu hình`.
- Đã tạo màn hình riêng làm khung để phát triển chức năng tiếp theo.

## CT07 - TableView dữ liệu

Đã tạo màn hình dữ liệu cho `Liên thông giấy nghỉ BHXH`:
- Nguồn chính: `dbo.MedicalRecordOff_BHXH`.
- JOIN `Patients`, `Hospital_Staff`, `Employee`.
- Lọc từ ngày/đến ngày theo `PostingDate`.
- Tách hiển thị hồ sơ chưa gửi/đã gửi theo `Send_HS`.
- Có tìm kiếm và chọn nhiều hồ sơ.
- TableView hiển thị các trường CT07 chính:
  Số KCB, Mã BHXH, Mã thẻ BHYT, Họ tên, Ngày sinh, Giới tính,
  Đơn vị, Chẩn đoán/Điều trị, Từ ngày, Đến ngày,
  Cha, Mẹ, Thủ trưởng đơn vị, Mã CCHN, Người hành nghề,
  Ngày chứng từ, TEKT, Mẫu số, Trạng thái.

## CT07 TableView - bộ cột theo MedicalRecordOff_BHXH

TableView hiển thị:
- Chọn
- STT
- Mã CT = Mã CSKCB + 2 số cuối năm PostingDate + Serial_BHXH
- Số KCB = PatientCode
- Mã BV = ClinicInformation.DT_MaBH_CSKCB
- Mã BS = Hospital_Staff.MaCCHN
- Mã BHXH = SoXoBHXH
- Mã Thẻ = Serial
- Họ tên, ngày sinh, giới tính
- PP điều trị = DepartmentBHYT.PPDT theo DepartmentCode
- Mã đơn vị = CompanyInfo_Code
- Tên đơn vị = CompanyInfo
- Từ ngày, đến ngày, số ngày
- Họ tên cha, mẹ
- Ngày CT = PostingDate
- Người đại diện = NguoiDaiDien
- Bác sĩ khám = Hospital_Staff.StaffName
- Seri = Mã CT
- Mẫu số = Type_BHXH
- Loại giấy tờ = LoaiHS_ID
- Số CCCD, ngày cấp, nơi cấp
- Ngày khám chữa bệnh = NGAY_KB
- Bệnh ICD10 = ICD10_Ma
- Tên bệnh ICD10 = ICD10_Ten

## CT07 mapping update

- PP điều trị = `MedicalRecordOff_BHXH.DiagnosisCustom`.
- Mã BS = `Hospital_Staff.StaffCode`.
- Tên bác sĩ = `Hospital_Staff.StaffName`.
- Cột `Mã BS` được chuyển ngay kế bên cột `Bác Sỹ khám`.
- Bỏ JOIN `DepartmentBHYT` vì không còn dùng cho PP điều trị.

## CT07 doctor join chain

Đã sửa mapping bác sĩ theo đúng chuỗi:
- `MedicalRecordOff_BHXH.EmployeeCodeDoctor`
  -> `Employee.EmployeeCode`
- `Employee.StaffCode`
  -> `Hospital_Staff.StaffCode`

Kết quả:
- `Mã BS` = `Hospital_Staff.StaffCode`
- `Bác Sỹ khám` = `Hospital_Staff.StaffName`

Điều kiện lọc:
- chỉ lấy hồ sơ `MedicalRecordOff_BHXH.isCancel = 0`.

## CT07 SendTrial v4 - NOIDUNGFILE Base64

Đã sửa đúng luồng dữ liệu:
1. Tạo XML CT07 con.
2. Encode toàn bộ XML CT07 con thành Base64.
3. Ghi chuỗi Base64 vào `<NOIDUNGFILE>`.
4. Tạo XML tổng hợp `HSCHUNGTU`.
5. Chuyển toàn bộ file tổng hợp thành byte[] và JSON serialize vào `fileHs`.

Không cập nhật SQL và không thay đổi `Send_HS`.

## CT07 v4 - schema theo mẫu thực tế

Giữ nguyên:
- CT07 XML con -> Base64 -> NOIDUNGFILE
- HSCHUNGTU -> byte[] -> fileHs JSON
- Không cập nhật SQL, không đổi Send_HS

CT07 con dùng các thẻ:
MA_CT, SO_SERI, SO_KCB, MA_BHXH, MA_THE, HO_TEN, NGAY_SINH,
GIOI_TINH, DON_VI, CHANDOAN_DIEUTRI, TU_NGAY, DEN_NGAY,
HO_TEN_CHA, HO_TEN_ME, THU_TRUONG_DV, MA_CCHN,
TEN_NGUOI_HANH_NGHE, NGAY_CHUNG_TU, TEKT, MAU_SO,
LOAI_GIAYTO, SO_CCCD, NGAYCAP_CCCD, NOICAP_CCCD,
NGAY_KCB, BENH_ICD10_ID, BENH_ICD10_TEN.

SO_SERI = MA_CT.

## CT07 - file temp trước khi gửi

Luồng gửi:
1. Tạo XML tổng hợp cuối cùng.
2. Ký số nếu bật.
3. Ghi XML vào thư mục `temp` cạnh file chạy ứng dụng.
4. Đọc file bằng `File.ReadAllBytes()` để lấy đúng `byte[]` cho `fileHs`.
5. Gửi API.
6. Tự xóa file XML tạm trong `finally`, dù gửi thành công hay thất bại.

Không cập nhật SQL và không thay đổi `Send_HS`.

## CT07 - temp file luôn tạo + trạng thái cuối bảng

- Bật hay không bật ký số, XML cuối cùng vẫn luôn được tạo thành file trong thư mục `temp`.
- Sau đó app dùng `File.ReadAllBytes()` để lấy `byte[]` gửi API.
- File temp tự xóa trong `finally`.
- Cột `Trạng thái` được đặt ở cuối cùng của bảng:
  - `Send_HS = true` -> `Đã gửi`
  - ngược lại -> `Chưa gửi`
