# CÔNG TY SẢN XUẤT & THƯƠNG MẠI VIỆT TIẾN
## ĐẶC TẢ NGHIỆP VỤ & YÊU CẦU HỆ THỐNG
### HỆ THỐNG BÁN HÀNG, NHẬP–XUẤT KHO VÀ VẬN HÀNH ĐƠN HÀNG
*Phiên bản 6.0 — CODE-READY MASTER SPECIFICATION*

---

## 1. THÔNG TIN CHUNG & KIỂM SOÁT TÀI LIỆU

### 1.1. Thuộc tính tài liệu
* **Loại tài liệu:** Business Requirements + Functional Specification + Screen/API Inventory.
* **Phạm vi hệ thống:** Bán hàng B2B/B2C, báo giá, thanh toán, nhập–xuất kho, giao hàng, hậu mãi và AI Marketing.
* **Công nghệ dự kiến:** ReactJS SPA, ASP.NET Core .NET 8 Web API, SQL Server, EF Core.
* **Ngày phát hành:** Tháng 7 năm 2026.
* **Trạng thái hành động:** Nguồn nghiệp vụ hiện hành để dev bắt đầu thiết kế và code.
* **Phạm vi bản v6.0:** Thay thế bản v5.4; đã xử lý triệt để các điểm chưa nhất quán về SLA giữ tồn, trạng thái thanh toán, tái phân bổ tiền, số dư mua hàng, đổi hàng, Quarantine, API thiếu, KPI, AI Marketing, OAuth, PDF xác nhận đơn và yêu cầu VAT.

### 1.2. Lịch sử thay đổi tài liệu
| Phiên bản | Thời điểm | Thay đổi chính | Trạng thái |
| :--- | :--- | :--- | :--- |
| **3.0** | 06/2026 | Tách CEO/Admin, bổ sung Sale Manager, ba kho và AI Marketing. | Tham khảo  |
| **4.0** | 06/2026 | Chuẩn hóa nhập–xuất kho, trạng thái và API ban đầu. | Đã thay thế  |
| **5.1** | 07/2026 | Chốt giá ba tầng và điều kiện đàm phán từ 100 triệu. | Đã thay thế  |
| **5.2** | 07/2026 | Bỏ hoàn tiền; bổ sung đơn thay thế và số dư mua hàng. | Đã thay thế  |
| **5.3** | 07/2026 | Bổ sung bảng tóm tắt duyệt nhanh. | Đã thay thế  |
| **5.4** | 07/2026 | Sửa bố cục và độ rộng bảng. | Đã thay thế  |
| **6.0** | 07/2026 | Hợp nhất nghiệp vụ, màn hình, API, dữ liệu, trạng thái, test và backlog để bắt đầu code. | Hiện hành  |

---

## 2. QUYẾT ĐỊNH NGHIỆP VỤ BẮT BUỘC (COMPULSORY DECISIONS)

* **CR-01 (Chính sách giá):** Đơn hàng < 10 triệu áp dụng giá niêm yết; từ 10 triệu đến < 100 triệu hệ thống tự động giảm phần trăm; chỉ đơn hàng >= 100 triệu mới được phép đàm phán.
* **CR-02 (Xử lý SePay):** Giao dịch đúng số tiền + dữ liệu hợp lệ + phân bổ đủ tồn kho => Chuyển tự động sang trạng thái *Paid* và *Confirmed*; mọi trường hợp ngoại lệ chuyển sang trạng thái `PAID_REVIEW_REQUIRED`.
* **CR-03 (Xử lý COD):** Nhân viên Sale phải xác nhận thủ công; hệ thống giữ tồn kho COD tối đa 35 phút, cảnh báo Sale ở phút 25, tiến hành leo thang (escalation) phút 30.
* **CR-04 (Phân bổ khách hàng):** Khách hàng cũ giữ Sale cũ; khách hàng mới có mã giới thiệu (referral) gán cho Sale giới thiệu; các trường hợp còn lại phân bổ theo thuật toán Round-robin.
* **CR-05 (Điều kiện kho):** Đơn hàng trạng thái *Confirmed* chỉ sinh các tác vụ Fulfillment, Allocation, và PickTask; chứng từ xuất bán chính thức (`GoodsIssue`) chỉ được tạo sau khi tập kết đủ hoặc được Multi-pick duyệt.
* **CR-06 (Chính sách không hoàn tiền):** Thanh toán qua ngân hàng đã xác nhận luôn giữ trạng thái *PAID*; việc hủy đơn hàng đã thanh toán chỉ được xử lý thông qua `ReplacementOrder` (Đơn thay thế), `PaymentReallocation` (Tái phân bổ tiền), và `CustomerOrderCredit` (Số dư tích lũy).
* **CR-07 (Đổi hàng & Cách ly):** Hỗ trợ đổi cùng SKU hoặc khác SKU; tất cả hàng trả về bị từ chối chất lượng bắt buộc phải đưa vào kho Quarantine/Damaged để kiểm định trước khi quyết định có xả về tồn bán hay không.
* **CR-08 (Purchase Order - PO):** Chỉ CEO mới có quyền tạo và phát hành PO; nhập PO từ Excel hoặc ảnh OCR ban đầu chỉ tạo bản nháp *Draft*; bộ phận kho bắt buộc phải đối chiếu thực nhận trước khi tăng tồn kho vật lý.
* **CR-09 (Vận chuyển nội bộ):** Hệ thống không có vai trò tài xế (Driver); nhân viên Sale phụ trách sẽ đi cùng xe để giao hàng và thực hiện thu tiền COD.
* **CR-10 (Sản xuất ngoài hệ thống):** Đại diện sản xuất ngoài ký giấy biên nhận; nhân viên kho tiến hành chụp ảnh biên bản đính kèm và Post phiếu xuất vật tư lên hệ thống.

---

## 3. TỔNG QUAN & PHẠM VI HỆ THỐNG (SCOPE)

### 3.1. Mục tiêu hệ thống
* Số hóa toàn bộ quy trình vận hành từ thu hút khách, gán quyền phụ trách cho Sale, quản lý giỏ hàng, đàm phán báo giá, tích hợp SePay, quản lý 3 kho bãi cho đến giao vận.
* Quản lý chặt chẽ 3 kho vật lý: Kho Sản xuất trung tâm (`WH-PROD`), Kho Thương mại (`WH-TRADE`), và Kho Màng PE & Xốp (`WH-PE`).
* Tạo môi trường cạnh tranh minh bạch cho đội ngũ Sale thông qua cơ chế theo dõi referral, Round-robin, doanh thu thực tế, và tỷ lệ khách quay lại.
* Tích hợp công cụ hỗ trợ AI Marketing tự động tạo và lên lịch đăng bài viết lên Facebook Page dưới sự kiểm duyệt của cấp quản lý.

### 3.2. Bảng phân định phạm vi
| Phân hệ / Nhóm | Nằm TRONG Phạm vi (In-Scope) | Nằm NGOÀI Phạm vi (Out-of-Scope) |
| :--- | :--- | :--- |
| **Kênh & Xác thực** | - Website Responsive cho khách và nội bộ .<br>- Xác thực qua Email Verification, Google OAuth, và SMS OTP. | - Hệ thống tính toán lương, khai báo thuế, và phát hành hóa đơn điện tử chính thức. |
| **Bán hàng & Giá** | - Giỏ hàng, bảng giá tự động, PDF đơn hàng .<br>- Luồng thương lượng báo giá cho đơn hàng >= 100 triệu kèm Live-chat. | - Hoàn trả tiền mặt hoặc chuyển khoản lại cho khách .<br>- Đấu giá trực tuyến. |
| **Quản trị Kho bãi** | - Quản lý 3 kho: Phân bổ hàng (Allocation), Pick, Packing, Transfer, kiểm kê, Quarantine/Damaged. | - Quy trình sản xuất chi tiết (MES): BOM, lệnh sản xuất chi tiết, hiệu suất máy. |
| **Mua & Giao hàng** | - CEO lập PO; Kho nhận đối chiếu bằng tay/Excel/OCR .<br>- Quản lý điều phối 5 xe, 3 ca vận hành giao nhận và thu COD. | - Tài khoản người dùng riêng dành cho Tài xế hoặc Đại diện sản xuất. |
| **Hậu mãi & Tiếp thị** | - Hủy đơn Paid đổi đơn thay thế hoặc tích số dư Credit .<br>- AI Studio tạo ảnh/caption và đặt lịch đăng bài Facebook. | - Tự động gán quyền sở hữu khách hàng cho Sale từ lượt click bài tiếp thị. |

---

## 4. MA TRẬN PHÂN QUYỀN VAI TRÒ (RBAC MATRIX)

### 4.1. Định nghĩa các vai trò hệ thống
* **GUEST:** Xem trang công khai, sản phẩm, đăng ký tài khoản và nhập referral. Không đặt hàng, không xem dữ liệu nội bộ.
* **CUSTOMER:** Xem hồ sơ cá nhân, sổ địa chỉ, quản lý giỏ hàng, thực hiện checkout COD/SePay, gửi yêu cầu báo giá/chat, yêu cầu đổi Sale, hủy/đổi hàng. Chỉ được xem dữ liệu của chính mình.
* **SALES_STAFF:** Chăm sóc khách được gán, xác nhận COD, lên đơn thủ công (manual), tạo đơn thay thế, lập đề xuất báo giá, tạo nội dung bài đăng AI, phối hợp đi xe giao hàng và thu COD. Bị giới hạn chỉ thao tác trên khách/đơn được gán, không được sửa tồn hoặc duyệt cuối.
* **SALES_MANAGER:** Quản lý cấu hình Round-robin, quyết định đổi Sale, giám sát SLA, điều phối xe/ca, duyệt báo giá cấp quản lý, xử lý SePay ngoại lệ, duyệt phương án hủy đơn Paid/đổi trả và duyệt bài đăng AI. Quyền hạn toàn đội Sale.
* **WAREHOUSE_STAFF:** Thực hiện các tác vụ Pick/Pack, nhập/xuất/điều chuyển kho, đối chiếu thực nhận PO, kiểm kê và quản lý hàng cách ly Quarantine theo phân kho được gán.
* **CEO:** Xem hệ thống Dashboard tổng thể, đưa ra phán quyết duyệt giá cuối cùng, lập và phát hành Purchase Order, xử lý chênh lệch kiểm kê/nhập kho. Không cập nhật trực tiếp số lượng tồn kho.
* **ADMIN:** Quản trị người dùng, phân quyền, cấu hình tích hợp API, quản lý master data, cấu hình khung discount, log hệ thống và audit log. Không can thiệp quyết định nghiệp vụ kinh doanh.

### 4.2. Ma trận quyền hạn chức năng
| Chức năng | Customer | Sale Staff | Sales Manager | Kho Staff | CEO | Admin |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **Đặt hàng / Checkout** | ✓  | Tạo thay  | Xem/Override  | | | Cấu hình  |
| **Đổi Sale phụ trách** | Yêu cầu  | Giải trình  | Quyết định  | | Báo cáo  | Kỹ thuật  |
| **Báo giá & Live-chat** | ✓  | ✓  | ✓  | | Duyệt cuối  | Kỹ thuật  |
| **Xác nhận đơn COD** | | ✓  | Override  | | | |
| **SePay Manual Confirm** | | | ✓  | | | Kỹ thuật  |
| **Nhập / Xuất kho bãi** | Xem  | | Xem  | ✓  | Duyệt đặc biệt  | Cấu hình  |
| **Purchase Order (PO)** | | | Xem  | Đối chiếu  | Tạo/Phát hành  | Master Data  |
| **Hủy Paid / Đổi hàng** | Yêu cầu  | Lập phương án  | Phê duyệt  | Nhận/Kiểm tra  | Báo cáo  | Kỹ thuật  |
| **AI Marketing** | | Tạo nháp  | Phê duyệt  | | Báo cáo  | Cấu hình  |

---

## 5. MÔ HÌNH TRẠNG THÁI HỆ THỐNG (STATUS MODELS)

### 5.1. Mô hình trạng thái Tồn kho (Inventory Model)
Hệ thống quản lý tồn kho nghiêm ngặt theo mô hình định vị: `Warehouse` + `WarehouseLocation` + `SkuId`. Hàng Quarantine được tính là một vị trí lưu trữ vật lý cách ly, hoàn toàn không phải là một cờ bật tắt (flag) trên sản phẩm.
* **OnHandQuantity:** Tổng số lượng vật lý thực tế đang nằm tại vị trí/kho.
* **ReservedQuantity:** Số lượng hàng hệ thống tạm giữ cho các phiên checkout chưa hoàn tất.
* **AllocatedQuantity:** Số lượng hàng đã được giữ chắc chắn cho các đơn hàng trạng thái *Confirmed*.
* **InTransitQuantity:** Số lượng hàng đang nằm trên xe điều chuyển chạy giữa các kho.
* **DamagedQuantity:** Số lượng hàng đã được kiểm định và xác định bị hỏng.
* **QuarantineQuantity:** Hàng đang nằm trong khu vực cách ly chờ kiểm tra chất lượng, cấm bán.
* **AvailableQuantity (Số lượng khả dụng mở bán):** Được tính toán tự động dựa theo công thức:
  $$\text{AvailableQuantity} = \text{OnHand} - \text{Reserved} - \text{Allocated} - \text{Damaged} - \text{Quarantine}$$ 

### 5.2. Các trục trạng thái chính
* **OrderStatus:** `DRAFT` => `PENDING_PAYMENT` (đơn SePay) / `PENDING_CONFIRMATION` (đơn COD) => `CONFIRMED` => `PROCESSING` => `COMPLETED`. Các trạng thái hủy: `CANCEL_REQUESTED` => `CANCELLED_REALLOCATED` (Đơn đã thanh toán, chuyển tiền sang đơn thay thế/credit) hoặc `CANCELLED` (Đơn chưa phát sinh dòng tiền cần phân bổ).
* **PaymentStatus:** `UNPAID` (mặc định cho COD chưa thu), `PENDING` (chờ SePay), `PAID` (ngân hàng xác nhận đủ tiền), `PARTIALLY_PAID` (thu thiếu COD hoặc đơn thay thế thiếu tiền), `FAILED`.
* **FulfillmentStatus:** `UNALLOCATED` => `RESERVED` => `ALLOCATED` => `PICKING` => `PARTIALLY_READY` => `READY` => `CONSOLIDATING` => `CONSOLIDATED` => `HANDED_OVER` => `FULFILLED`.
* **DeliveryStatus:** `NOT_SCHEDULED` => `SCHEDULED` => `IN_DELIVERY` => `DELIVERED` / `FAILED` / `PARTIALLY_DELIVERED` => `RESCHEDULED` / `CANCELLED`.
* **QuotationStatus:** `DRAFT` => `NEGOTIATING` => `PENDING_MANAGER` => `PENDING_CEO` => `APPROVED` => `CUSTOMER_ACCEPTED` / `CUSTOMER_REJECTED`.
* **PurchaseOrderStatus:** `DRAFT` => `ISSUED` => `SENT_TO_WAREHOUSE` => `PARTIALLY_RECEIVED` / `FULLY_RECEIVED` / `DISCREPANCY_REVIEW` => `CLOSED`.

---

## 6. QUY TRÌNH NGHIỆP VỤ CHI TIẾT (WORKFLOWS)

### WF-01. Đăng ký, xác minh email và phân bổ Sale
1. Khách hàng thực hiện nhập thông tin đăng ký hoặc lựa chọn Google OAuth. Hệ thống thực hiện gửi link verification đối với luồng đăng ký bằng mật khẩu. Google OAuth được coi là đã xác minh email.
2. Hệ thống quét tìm kiếm thông tin dựa trên SĐT, Email hoặc Mã số thuế để nhận diện xem là Khách cũ hay Khách mới.
3. **Quy tắc gán:** Khách cũ giữ nguyên Sale cũ. Khách mới điền đúng referral code sẽ được gán cho Sale sở hữu mã đó. Các trường hợp khách mới còn lại sẽ tự động phân bổ theo cơ chế Round-robin.
4. *Lưu ý kỹ thuật cho dev:* Quá trình gán Round-robin phải tiến hành khóa Transaction khi cập nhật con trỏ `RoundRobinCursor` để tránh tranh chấp dữ liệu.

### WF-02. SMS OTP xác thực
1. Bắt buộc thực hiện thành công SMS OTP trong 2 trường hợp: Thực hiện đặt đơn hàng đầu tiên (`FIRST_ORDER`) hoặc khi yêu cầu thay đổi số điện thoại trên hệ thống (`CHANGE_PHONE`).
2. Mã OTP gồm 6 số, tồn tại trong 5 phút. Hệ thống thực hiện lưu mã băm bảo mật, tuyệt đối không ghi nhận plain text OTP vào hệ thống log công khai.
3. Áp dụng giới hạn tần suất (Rate limit): Tối đa 5 lần gửi/30 phút và tối đa 10 lần gửi/ngày; thời gian giữa các lượt gửi lại (resend) tối thiểu là 60 giây.

### WF-04 & WF-05. Quy trình Tính giá và Đàm phán báo giá ($\ge$ 100M)
1. Toàn bộ logic tính tổng tiền, chiết khấu bắt buộc phải xử lý tập trung tại Server (`Nguồn sự thật`). Frontend không được gửi tổng tiền quyết định lên hệ thống.
2. Khi giỏ hàng có tổng giá trị trước VAT đạt mốc $\ge$ 100 triệu, hệ thống lập tức khóa chức năng Checkout thông thường, bắt buộc chuyển sang luồng tạo `QuotationRequest`.
3. Nhân viên Sale trao đổi qua Live-chat trực tuyến và lập các bản ghi đề xuất giá (`QuotationVersion`). Bản ghi này sau khi submit là bất biến (`Immutable`). Mọi sửa đổi phát sinh về SKU, số lượng bắt buộc phải sinh mã phiên bản mới hoàn toàn.
4. Quy trình phê duyệt: Sale Staff => Sales Manager xem xét điều chỉnh => CEO đưa ra quyết định duyệt cuối cùng => Khách hàng ấn nút Chấp nhận trên giao diện thì giá mới được mở khóa để thực hiện Checkout.

### WF-06. Quy trình đặt hàng và đối soát SePay tự động
1. Khi khách hàng nhấn nút Đặt hàng, hệ thống yêu cầu đính kèm mã Idempotency-key để chống trùng lặp đơn.
2. **Đơn hàng COD:** Hệ thống kích hoạt bộ đếm thời gian tạm giữ tồn kho vật lý (`Reservation`) trong vòng **35 phút**. Đến phút thứ 25 sẽ gửi thông báo nhắc nhở Sale, phút thứ 30 tiến hành cảnh báo lên cấp Quản lý, và quá phút 35 nếu Sale chưa duyệt thủ công, hệ thống tự động giải phóng tồn kho.
3. **Đơn hàng SePay:** Hệ thống kích hoạt bộ đếm giữ tồn kho vật lý trong vòng **15 phút**, hiển thị mã QR thanh toán. 
4. Khi nhận được tín hiệu Webhook từ SePay: Hệ thống kiểm tra tính hợp lệ của chữ ký giao dịch và tính trùng lặp đơn. Nếu trùng khớp số tiền và hệ thống phân bổ đủ tồn kho vật lý => Hệ thống tự động chuyển trạng thái đơn hàng sang `CONFIRMED` và chuyển lệnh xuống bộ phận kho chế biến PickTask chuẩn bị hàng mà không cần nhân viên duyệt.
5. **Ngoại lệ SePay:** Trường hợp SePay báo thanh toán thành công nhưng kiểm tra kho bị thiếu hàng (do thanh toán muộn sau khi hết 15 phút giữ tồn), hệ thống cấm không được ghi nhận âm kho Available. Trạng thái giao dịch ngân hàng bắt buộc giữ nguyên là `PAID`, đồng thời chuyển trạng thái đơn hàng sang mã kiểm duyệt `PAID_REVIEW_REQUIRED` để báo Manager can thiệp xử lý thủ công.

### WF-09. Quy trình chuẩn bị hàng và vận hành kho
1. Đơn hàng trạng thái *Confirmed* sinh lệnh PickTask cho nhân viên kho theo phân vùng quản lý.
2. Nhân viên tiến hành lấy hàng, thực hiện đếm số lượng đóng gói, dán nhãn và bắt buộc phải chụp ảnh kiện hàng hoàn thiện lưu lên hệ thống làm minh chứng.
3. **Quy tắc tập kết mặc định (Default consolidation):** Hệ thống tự động tạo phiếu điều chuyển hàng nội bộ (`StockTransfer`) từ các kho vệ tinh phụ trợ chạy về tập kết đầy đủ tại kho trung tâm `WH-PROD`. Sau khi hàng về đủ, kho trung tâm mới được thực hiện Post phiếu xuất bán chính thức (`GoodsIssue SALES`). Nghiêm cấm hành vi tạo GoodsIssue SALES quá sớm khi hàng chưa về điểm tập kết.
4. **Trường hợp Multi-pick:** Chỉ được thực hiện khi có sự phê duyệt trực tiếp từ Sales Manager, lúc này từng kho vệ tinh sẽ tự Post phiếu GoodsIssue xuất bán cho phần hàng nằm tại kho của mình.

### WF-14 & WF-15. Quy trình Hủy đơn hàng Paid và Đổi trả hàng chất lượng
1. Hệ thống áp dụng quy tắc nghiêm ngặt **KHÔNG HOÀN TIỀN MẶT HOẶC CHUYỂN KHOẢN LẠI** cho khách hàng đối với các đơn hàng đã ghi nhận thanh toán thành công.
2. Khi duyệt hủy một đơn hàng trạng thái `PAID`, trạng thái đơn cũ chuyển sang `CANCELLED_REALLOCATED`. Nhân viên Sale phối hợp lên phương án lập một đơn hàng thay thế mới (`ReplacementOrder`). Hệ thống sử dụng thực thể độc lập `PaymentReallocation` để điều chuyển giá trị dòng tiền sang đơn mới.
3. Nếu giá trị đơn hàng thay thế mới thấp hơn số tiền khách đã đóng, phần giá trị chênh lệch thừa ra sẽ được hệ thống chuyển đổi thành số dư mua hàng tích lũy (`CustomerOrderCredit`) trạng thái khả dụng `AVAILABLE` gắn với tài khoản của khách hàng đó. Ví Credit này không có thời hạn hết hạn, không được phép chuyển nhượng tài khoản và không thể quy đổi ngược ra tiền mặt. Khách hàng có quyền chủ động tick chọn trừ số dư ví Credit này trực tiếp khi tiến hành Checkout cho các đơn hàng tiếp theo.
4. **Quy trình đổi hàng lỗi:** Tất cả hàng hóa trả về do lỗi chất lượng từ phía khách hàng khi nhận, bộ phận kho tiếp nhận bắt buộc phải ghi nhận nhập kho vào vị trí cách ly biệt lập `Quarantine`. Nhân viên kho tiến hành chạy luồng kiểm định chất lượng (`Quality Inspection`), nếu hàng hỏng hẳn thì chuyển sang vị trí `Damaged`, nếu hàng vẫn đạt chuẩn thì phải có tài khoản cấp cao phê duyệt mới được xả ngược về vị trí `Available` mở bán thương mại.

---

## 7. DANH SÁCH THAM SỐ CẤU HÌNH HỆ THỐNG (SYSTEM PARAMETERS)

| Mã Key cấu hình | Giá trị mặc định | Đơn vị tính | Cấp sở hữu cấu hình |
| :--- | :---: | :---: | :---: |
| `PRICE_LOCK_HOURS` | 24 | Giờ | Admin  |
| `SEPAY_RESERVATION_MINUTES` | 15 | Phút | Admin  |
| `COD_RESERVATION_MINUTES` | 35 | Phút | Admin  |
| `COD_WARNING_MINUTES` | 25 | Phút | Admin  |
| `COD_ESCALATION_MINUTES` | 30 | Phút | Admin  |
| `OTP_EXPIRE_MINUTES` | 5 | Phút | Admin  |
| `OTP_RESEND_SECONDS` | 60 | Giây | Admin  |
| `OTP_MAX_ATTEMPTS` | 5 | Lần | Admin  |
| `QUOTATION_MIN_VALUE` | 100.000.000 | VND (Trước VAT) | Admin/CEO  |
| `LIST_PRICE_MAX_EXCLUSIVE` | 10.000.000 | VND | Admin/CEO  |
| `MAX_SCHEDULED_MARKETING_POSTS` | 30 | Bài viết | Admin  |
| `DELIVERY_FAILURE_MANAGER_THRESHOLD` | 3 | Lần thử giao | Admin/Manager  |

---

## 8. TIÊU CHUẨN HOÀN THÀNH CHỨC NĂNG (DEFINITION OF DONE - DOD)

Mọi tính năng và API trước khi được xác nhận hoàn thành để merge vào nhánh phát triển chính phải đáp ứng đầy đủ các tiêu chí kỹ thuật sau:
1. **Giao diện người dùng (UI State):** Phải thiết kế đầy đủ các trạng thái hiển thị của màn hình bao gồm: trạng thái đang tải dữ liệu (loading), màn hình trống (empty), lỗi kiểm tra dữ liệu (validation), trạng thái thao tác thành công (success), thất bại (failure), và màn hình chặn quyền truy cập (permission denied).
2. **Kiểm tra tầng Server:** Bắt buộc phải có bộ kiểm tra dữ liệu đầu vào phía Server (Server-side validation), kiểm tra phân quyền chặt chẽ theo vai trò (Authorization), và tích hợp mã Idempotency-key chống trùng lặp dữ liệu đối với các API nghiệp vụ quan trọng.
3. **Cơ sở dữ liệu & Kiểm toán:** Phải cập nhật đầy đủ file migration dữ liệu, thiết lập chỉ mục (index) tối ưu tìm kiếm, và cấu hình lưu trữ nhật ký kiểm toán (`Audit Log`) ghi nhận rõ giá trị trước và sau khi thay đổi (Before/After) đối với các thực thể quan trọng liên quan đến giá, tồn kho và dòng tiền.
4. **Bảo mật thông tin:** Tuyệt đối cấu hình bộ lọc không cho phép ghi nhận hiển thị các thông tin nhạy cảm bao gồm mật khẩu plain text, mã Token kích hoạt, dữ liệu mã OTP gửi về điện thoại vào hệ thống file log vận hành.
5. **Kiểm thử chất lượng:** Hoàn thành viết Unit test cho các quy tắc kiểm soát điều kiện nghiệp vụ (Business rules) và Integration test cho các luồng API; đảm bảo chạy vượt qua các kịch bản kiểm thử luồng đúng (happy path), luồng sai ngoại lệ (negative path), và kiểm tra các mốc giá trị ranh giới (boundary values) trước khi tiến hành bàn giao chứng năng.