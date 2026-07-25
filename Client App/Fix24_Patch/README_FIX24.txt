FIX24
1. Lỗi Invalid column name 'PatientName':
   Trong schema đã dùng trước đó, tên bệnh nhân lấy từ PatientReceive.PartientName
   (có chữ 'r' sau Pa), không phải PatientReceive.PatientName.
   Sửa:
       pr.PatientName AS HO_TEN
   thành:
       pr.PartientName AS HO_TEN

2. Menu chính mong muốn:
   - Liên thông KSK
   - Dữ liệu Xml

   Kiến trúc đề xuất:
   MainWindow giữ Menu ở trên cùng và ContentControl ở dưới.
   Liên thông KSK = màn hình chức năng GKSK hiện tại.
   Dữ liệu Xml = màn hình XML1/XML2/XML3 với bộ lọc Ngày/Khoảng ngày/Quý/Năm.

3. Không tạo hai app riêng; tích hợp Dữ liệu Xml vào app Liên thông KSK hiện tại.
