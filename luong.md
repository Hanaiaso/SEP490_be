**PHỤ LỤC A. CÁC LUỒNG NGHIỆP VỤ CỐT LÕI**

## **👥 LUỒNG 1: ĐĂNG KÝ, XÁC THỰC VÀ TỰ ĐỘNG PHÂN BỔ SALE**

Luồng này đảm bảo mọi khách hàng vào hệ thống đều được định danh sạch và có một nhân viên Sale chịu trách nhiệm ngay lập tức theo quy tắc cạnh tranh minh bạch.

* **Bước 1: Khách để lại thông tin hoặc đăng ký:** Người dùng nhập Họ tên, Email, SĐT, Mật khẩu, Mã số thuế và mã giới thiệu (Referral Code) nếu có. Nếu chọn Google OAuth, hệ thống tự động bỏ qua bước xác minh email tiếp theo.  
* **Bước 2: Hệ thống gửi Token xác minh:** Với luồng đăng ký thường, hệ thống bắn một email chứa mã xác thực một lần (One-time token). Khách hàng phải click xác minh để tài khoản được kích hoạt quyền đặt hàng hoặc yêu cầu báo giá.  
* **Bước 3: Quét nhận diện khách cũ/mới:** Hệ thống kiểm tra trong cơ sở dữ liệu dựa trên SĐT, Email, hoặc Mã số thuế (MST).  
* **Bước 4: Thực hiện chính sách phân bổ gán Sale (Quy tắc CR-04):**  
  * *Nếu là Khách cũ:* Giữ nguyên nhân viên Sale cũ phụ trách lịch sử.  
  * *Nếu là Khách mới \+ Có Referral Code hợp lệ:* Gán trực tiếp cho nhân viên Sale sở hữu mã giới thiệu đó.  
  * *Nếu là Khách mới \+ Không có Referral Code:* Hệ thống chạy thuật toán gán tự động xoay vòng Round-robin. Con trỏ RoundRobinCursor sẽ bị khóa transaction trong DB khi cập nhật để đảm bảo không gán trùng khách cho 2 Sale cùng lúc.  
* **Bước 5: Kích hoạt SMS OTP cho hành động trọng yếu:** Khi khách hàng tiến hành thay đổi số điện thoại hoặc thực hiện đặt đơn hàng đầu tiên (FIRST\_ORDER), hệ thống bắt buộc bắt gửi mã SMS OTP 6 số để xác thực số điện thoại thật. Mã này được băm bảo mật khi lưu trữ và hết hạn sau 5 phút.

## **💰 LUỒNG 2: MUA HÀNG VÀ ĐÀM PHÁN BÁO GIÁ ĐƠN HÀNG LỚN (\>= 100 TRIỆU)**

Luồng này xử lý việc phân cấp tính giá từ tự động đến đàm phán trực tiếp bằng Live-chat đa cấp phê duyệt.

* **Bước 1: Thêm hàng và Giỏ hàng tính giá (Quy tắc CR-01):** Khách hàng hoặc Sale lên đơn thay sẽ thêm các mặt hàng vào giỏ. Toàn bộ tổng tiền được tính toán trực tiếp trên Server (Nguồn sự thật). Hệ thống tự phân tầng:  
  * Đơn \< 10 triệu: Áp dụng giá niêm yết cố định.  
  * Đơn từ 10 triệu đến \< 100 triệu: Hệ thống tự động áp dụng mức giảm phần trăm theo cấu hình (DiscountTier).  
  * Đơn \>= 100 triệu: Hệ thống chặn cứng nút Checkout thông thường, bắt buộc phải mở luồng Báo giá.  
* **Bước 2: Khởi tạo Phòng đàm phán (ChatRoom):** Hệ thống tạo một yêu cầu báo giá (QuotationRequest) kèm một phòng chat realtime kết nối Khách hàng, Sale phụ trách, Manager và CEO.  
* **Bước 3: Lập phiên bản giá (Quotation Version):** Nhân viên Sale thương lượng với khách qua Live-chat và tạo ra một đề xuất chiết khấu (QuotationVersion). Bản đề xuất này sau khi submit là bất biến (Immutable). Mọi chỉnh sửa về số lượng hay thay đổi SKU sau đó bắt buộc phải sinh một QuotationVersion mới hoàn toàn để ký duyệt lại.  
* **Bước 4: Chuỗi phê duyệt đa cấp:**  
  * *Cấp 1:* Sales Manager rà soát biên lợi nhuận và nhấn duyệt cấp quản lý.  
  * *Cấp 2:* CEO rà soát tối cao và thực hiện nhấn nút duyệt cuối cùng.  
* **Bước 5: Khách hàng Chấp nhận đơn giá:** Bản giá sau khi CEO duyệt sẽ hiển thị nút cho Khách hàng chọn. Khách nhấn Chấp nhận (CUSTOMER\_ACCEPTED), hệ thống khóa chặt phiên bản giá đó và mở khóa màn hình Checkout cho phép đặt đơn.

## **🛒 LUỒNG 3: ĐẶT HÀNG, CHECKOUT VÀ ĐỐI SOÁT SEPAY TỰ ĐỘNG**

Luồng này xử lý việc đặt mua, giữ hàng tạm thời trên kho vật lý và tự động hóa khâu xác nhận dòng tiền qua ngân hàng.

* **Bước 1: Gửi lệnh Checkout kèm Idempotency-key:** Khách hàng tiến hành chọn địa chỉ nhận và tick chọn yêu cầu xuất hóa đơn VAT (nếu có). Hệ thống yêu cầu đính kèm một Idempotency-Key ở Header để phòng trường hợp khách bấm đúp nút thanh toán không bị tạo thành 2 đơn trùng nhau.  
* **Bước 2: Tạo đơn và Giữ kho vật lý tạm thời (Reservation):** Đơn hàng được sinh ra ở trạng thái DRAFT và rẽ làm 2 nhánh thanh toán dựa trên lựa chọn của khách:  
  * *Nhánh COD:* Hệ thống chuyển trạng thái đơn sang PENDING\_CONFIRMATION và tạo bản ghi giữ hàng tạm thời trên kho (InventoryReservation) trong **35 phút**. Trong vòng 35 phút này, Sale phải liên hệ xác nhận thủ công để duyệt đơn sang CONFIRMED. Nếu quá thời gian (phút 25 cảnh báo Sale, phút 30 báo Manager, phút 35 hết giờ), hệ thống tự động giải phóng tồn kho về trạng thái mở bán.  
  * *Nhánh SePay:* Đơn chuyển trạng thái sang PENDING\_PAYMENT, sinh mã QR và nội dung chuyển khoản chuẩn, đồng thời tạo lệnh giữ kho vật lý tạm thời trong **15 phút**.  
* **Bước 3: Nhận tín hiệu Webhook từ SePay:** Khi khách quét QR chuyển khoản thành công, hệ thống SePay bắn một tín hiệu Webhook về API hệ thống. API tiến hành giải mã, kiểm tra tính hợp pháp của chữ ký bảo mật và đối soát trùng giao dịch.  
* **Bước 4: Xử lý chuyển đổi tồn kho chắc chắn (Allocation):**  
  * *Trường hợp Happy Path (Đủ hàng \+ Đúng tiền):* Hệ thống cập nhật trạng thái thanh toán đơn hàng thành PAID (Trạng thái thanh toán qua ngân hàng một khi đã ghi nhận thành công tuyệt đối không được phép chuyển ngược về chưa thanh toán). Hệ thống chuyển đổi số lượng tồn kho từ giữ tạm sang giữ chắc chắn (RESERVED \=\> ALLOCATED), đơn hàng tự động chuyển sang CONFIRMED và đẩy lệnh xuống bộ phận kho làm fulfillment mà không cần con người duyệt.  
  * *Trường hợp Ngoại lệ (Thanh toán muộn hết 15 phút giữ tồn \-\> Kho bị thiếu hàng):* Hệ thống nghiêm cấm hành vi tự ý ghi âm số lượng tồn kho Available của bãi. Trạng thái dòng tiền vẫn giữ nguyên là PAID, nhưng trạng thái đơn hàng lập tức bị đẩy sang mã kiểm duyệt PAID\_REVIEW\_REQUIRED để báo động cho Sales Manager xử lý can thiệp bằng tay, tuyệt đối không tự động hoàn tiền.

## **📦 LUỒNG 4: THỰC THI FULFILLMENT, TRUNG CHUYỂN VÀ XUẤT KHO BÁN HÀNG**

Luồng này điều hành toàn bộ việc bốc xếp, đóng gói và kiểm soát dòng dịch chuyển vật lý của hàng hóa tại 3 kho bãi.

* **Bước 1: Sinh lệnh chuẩn bị hàng (PickTask):** Đơn hàng sau khi đạt trạng thái CONFIRMED sẽ kích hoạt hệ thống sinh mã tác vụ chuẩn bị hàng (FulfillmentOrder) chia về cho nhân viên tại kho đang chứa SKU đó.  
* **Bước 2: Nhân viên Kho bốc dỡ và Đóng gói (Pick & Pack):** Nhân viên kho đến vị trí kệ bốc dỡ, cập nhật số lượng lấy thực tế, đếm số kiện hàng đóng gói, dán nhãn vận chuyển và bắt buộc phải dùng thiết bị cầm tay chụp ảnh gói hàng hoàn thiện đính kèm lên hệ thống để làm minh chứng.  
* **Bước 3: Vận hành quy trình tập kết hàng mặc định (Consolidation):** Hệ thống kiểm tra, nếu đơn hàng chứa các SKU nằm rải rác ở các kho vệ tinh khác nhau, hệ thống tự động sinh lệnh điều chuyển nội bộ (StockTransfer) yêu cầu gom hàng về kho trung tâm WH-PROD. Kho nguồn thực hiện Post xuất điều chuyển (Hàng trừ ở kho nguồn, chuyển sang trạng thái đi đường InTransitQuantity). Khi xe trung chuyển đến kho trung tâm WH-PROD, kho trung tâm đếm nhận để ghi tăng số tồn khả dụng tập kết.  
* **Bước 4: Quy trình Xác nhận kép (Dual Confirmation) bàn giao xe:** Khi xe bốc dỡ chuẩn bị xuất phát đi giao cho khách, nhân viên kho trung tâm và nhân viên Sale đi cùng xe phải cùng có mặt thực hiện đối chiếu. Sale kiểm tra đúng số kiện, kho kiểm tra đúng chứng từ, hai bên thực hiện đồng ký biên bản xác nhận kép (HandoverRecord).  
* **Bước 5: Post phiếu xuất kho bán hàng chính thức:** Sau khi có xác nhận kép, nhân viên kho tiến hành bấm **Post phiếu xuất kho (**GoodsIssue SALES**)**. Hệ thống thực hiện trừ tồn kho vật lý khả dụng vĩnh viễn và khóa chứng từ. Quy trình nghiêm cấm hành vi tạo GoodsIssue SALES quá sớm khi hàng chưa về đủ điểm tập kết trung tâm.

## **🔄 LUỒNG 5: VẬN CHUYỂN, THU COD VÀ HẬU MÃI NÂNG CAO (HỦY ĐƠN PAID / ĐỔI TRẢ QUARANTINE)**

Luồng này xử lý khâu cuối cùng của chuyến xe giao nhận và giải quyết rủi ro dòng tiền/hàng hóa khi khách đòi hủy đơn hoặc đổi trả.

* **Bước 1: Lập lịch xe và Thực hiện giao hàng:** Nhân viên Sale lập chuyến xe, chọn ca chạy trên hệ thống (Hệ thống tự quét chặn nếu xe bị trùng ca hoặc đang bảo trì). Sale đi cùng xe, gọi điện cho khách, giao hàng vật lý. Khi giao xong, Sale bắt buộc chụp ảnh hiện trường, xin chữ ký số của khách để ghi nhận bằng chứng giao hàng thành công (Proof of Delivery).  
* **Bước 2: Đối soát dòng tiền thu COD tại chỗ:**  
  * *Nếu khách trả đủ tiền:* Sale nhập số tiền thu thực tế, hệ thống đổi trạng thái đơn sang COMPLETED.  
  * *Nếu khách trả thiếu:* Hệ thống chuyển trạng thái đơn hàng sang PARTIALLY\_PAID đồng thời tự động sinh một sổ ghi nợ công nợ khách hàng (DebtRecord).  
  * *Nếu đơn hàng thất bại quá 3 lần:* Hệ thống chặn luồng giao tự động, đẩy hồ sơ lên Sales Manager xử lý thủ công.  
* **Bước 3: Tiếp nhận và xử lý yêu cầu hủy đơn hàng đã thanh toán (PAID):** Trường hợp khách đòi hủy đơn khi tiền đã vào tài khoản công ty, theo quy tắc **CR-06**, hệ thống áp dụng chính sách **KHÔNG HOÀN TIỀN MẶT HOẶC CHUYỂN KHOẢN TRẢ LẠI**. Khi Sales Manager bấm phê duyệt yêu cầu hủy đơn, hệ thống lập tức ra lệnh xuống kho hủy bỏ toàn bộ các tác vụ lấy hàng đang chạy để hoàn lại chứng từ.  
* **Bước 4: Tạo đơn thay thế và Phân bổ giá trị ví số dư Credit:**  
  * Nhân viên Sale lập phương án tạo một đơn hàng thay thế mới (ReplacementOrder) cho khách. Hệ thống sử dụng một thực thể riêng là PaymentReallocation để cắt chuyển giá trị dòng tiền của đơn cũ sang bảo lưu cho đơn mới.  
  * Nếu đơn hàng thay thế mới có giá trị thấp hơn đơn cũ, phần tiền thừa còn lại sẽ được hệ thống tự động hạch toán chuyển đổi thành số dư mua hàng tích lũy (CustomerOrderCredit) trạng thái AVAILABLE gắn chặt vào ID tài khoản của khách hàng đó. Ví Credit này không có ngày hết hạn, không được phép chuyển nhượng giữa các tài khoản khách hàng khác nhau và không thể quy đổi ra tiền mặt. Khách hàng có quyền chủ động tick chọn trừ số dư ví Credit này khi thực hiện Checkout các đơn hàng tiếp theo.  
* **Bước 5: Tiếp nhận đổi trả hàng lỗi và Cưỡng chế cách ly (Quarantine):** Trường hợp khách hàng tạo yêu cầu đổi trả do lỗi chất lượng hàng hóa, khi kiện hàng lỗi được xe mang quay trở lại cổng kho, nhân viên kho tiếp nhận bắt buộc phải thực hiện hạch toán nhập số lượng này vào vị trí bãi biệt lập là kho cách ly QuarantineQuantity. Hàng tại vị trí Quarantine bị hệ thống chặn hoàn toàn, không được tính vào tồn kho Available để mở bán thương mại. Nhân viên kiểm định chất lượng sẽ rà soát: nếu hỏng hẳn thì chuyển sang vị trí kho phế phẩm Damaged, nếu hàng vẫn đạt chuẩn thì phải có tài khoản cấp quản lý ký duyệt điện tử mới được xả ngược về bãi Available để tái mở bán.

**🔒 Ràng buộc kỹ thuật xuyên suốt cho đội ngũ lập trình (Dev):** Mọi hành động làm tăng/giảm số lượng tồn kho vật lý tại luồng 4 và luồng 5, hoặc thay đổi số dư ví Credit của khách hàng tại luồng 5 đều bắt buộc phải triển khai cơ chế **Optimistic Concurrency / Row Versioning** ở tầng Database để ngăn chặn triệt để hiện tượng ghi đè hoặc âm kho dữ liệu khi có nhiều tác vụ bất đồng bộ diễn ra cùng một thời điểm.

**PHỤ LỤC B. CÁC LUỒNG NGHIỆP VỤ BỔ SUNG NGOÀI 5 LUỒNG CỐT LÕI**

Năm luồng cốt lõi đã mô tả đầy đủ vòng đời bán hàng từ đăng ký, báo giá, checkout, fulfillment đến giao hàng và hậu mãi. Phần phụ lục này bổ sung các quy trình hỗ trợ còn lại để hệ thống vận hành khép kín, đồng thời liên kết trực tiếp với các Workflow, Screen và Business Rule trong đặc tả v6.0.

| Mã luồng | Phạm vi nghiệp vụ bổ sung | Liên kết đặc tả |
| ----- | ----- | ----- |
| Luồng 6 | Hồ sơ khách hàng, địa chỉ giao hàng và yêu cầu VAT | WF-03; CUS-01, CUS-02, CUS-06 |
| Luồng 7 | Yêu cầu đổi nhân viên Sale phụ trách | WF-07; CUS-11, MGR-06 |
| Luồng 8 | Sale xác nhận COD và tạo đơn thủ công | WF-08; SAL-04, SAL-05 |
| Luồng 9 | Purchase Order, nhập kho và đối chiếu nhà cung cấp | WF-10, WF-11; CEO-03→07, WH-09→11 |
| Luồng 10 | Điều chuyển kho, kiểm kê, điều chỉnh và cảnh báo tồn | WF-12, WF-16; WH-06→08, WH-13, CEO-08 |
| Luồng 11 | Xuất nguyên liệu cho sản xuất ngoài hệ thống | WF-17; WH-14 |
| Luồng 12 | AI Marketing, duyệt và đăng Facebook Page | WF-18; SAL-10, MGR-10, ADM-09 |
| Luồng 13 | Quản trị, audit, scheduled jobs, dashboard và KPI | WF-19; ADM-01→08, SAL-01, MGR-01, CEO-01 |

**LUỒNG 6: QUẢN LÝ HỒ SƠ KHÁCH HÀNG, ĐỊA CHỈ GIAO HÀNG VÀ YÊU CẦU VAT**

Luồng này đảm bảo dữ liệu khách hàng được chuẩn hóa, địa chỉ giao hàng được quản lý có lịch sử và thông tin yêu cầu VAT được chụp snapshot theo từng đơn, tránh việc thay đổi hồ sơ làm sai dữ liệu của đơn đã phát sinh.

**Bước 1:** Khách hàng mở hồ sơ và cập nhật thông tin cá nhân hoặc doanh nghiệp gồm họ tên, tên công ty, mã số thuế, email và số điện thoại. Hệ thống kiểm tra định dạng, trùng dữ liệu và trạng thái xác minh trước khi lưu.

**Bước 2:** Khách hàng tạo, sửa hoặc đặt địa chỉ mặc định trong sổ địa chỉ. Mỗi tài khoản chỉ có một địa chỉ mặc định tại một thời điểm; địa chỉ đã được dùng trong đơn lịch sử không bị xóa cứng mà chỉ chuyển sang trạng thái không hoạt động.

**Bước 3:** Tại checkout, khách chọn địa chỉ giao hàng và có thể bật yêu cầu VAT. Hệ thống yêu cầu nhập đầy đủ tên đơn vị, mã số thuế, địa chỉ xuất hóa đơn và email nhận thông tin.

**Bước 4:** Khi đơn được tạo, hệ thống chụp AddressSnapshot và VatInvoiceRequest gắn với Order. Các bản snapshot không tự thay đổi khi khách chỉnh sửa hồ sơ hoặc sổ địa chỉ sau đó.

**Bước 5:** Sales Staff theo dõi yêu cầu VAT và chuyển dữ liệu sang quy trình kế toán bên ngoài phạm vi hệ thống. Hệ thống chỉ ghi nhận yêu cầu và trạng thái xử lý, không phát hành hóa đơn điện tử chính thức.

**Bước 6:** Nếu đơn đã CONFIRMED mà khách yêu cầu đổi địa chỉ hoặc thông tin VAT, Sale phải tạo thao tác điều chỉnh có lý do và audit; hệ thống không ghi đè im lặng dữ liệu snapshot cũ.

**Điểm kiểm soát và ngoại lệ**

·   Email, số điện thoại hoặc mã số thuế trùng với tài khoản khác phải bị từ chối hoặc chuyển sang quy trình xác minh khách cũ.

·   Không cho xóa cứng địa chỉ đã có trong đơn lịch sử; dữ liệu đơn cũ luôn giữ nguyên.

·   Yêu cầu VAT là dữ liệu nghiệp vụ hỗ trợ, không đồng nghĩa hóa đơn VAT đã được phát hành.

**LUỒNG 7: KHÁCH HÀNG YÊU CẦU ĐỔI NHÂN VIÊN SALE PHỤ TRÁCH**

Luồng này cho phép khách hàng đề nghị thay đổi người phụ trách nhưng vẫn bảo toàn lịch sử sở hữu khách, trách nhiệm của Sale cũ và trạng thái các đơn đang xử lý.

**Bước 1:** Khách hàng tạo SalesChangeRequest, nhập lý do, mô tả vấn đề, Sale mong muốn nếu có và đính kèm bằng chứng. Hệ thống chỉ cho phép một yêu cầu đang mở cho cùng một khách hàng.

**Bước 2:** Hệ thống chuyển yêu cầu sang PENDING, thông báo cho Sales Manager và Sale hiện tại, đồng thời ghi thời điểm bắt đầu SLA xử lý.

**Bước 3:** Sale hiện tại gửi phần giải trình và thông tin các đơn đang chạy. Sale không được tự xóa, tự từ chối hoặc thay đổi nội dung yêu cầu của khách.

**Bước 4:** Sales Manager rà soát lịch sử chăm sóc, KPI, khối lượng khách của Sale mong muốn, các đơn chưa hoàn thành và mức độ nghiêm trọng của lý do đổi Sale.

**Bước 5:** Manager có thể yêu cầu bổ sung thông tin, từ chối có lý do hoặc phê duyệt và chỉ định Sale mới. Với từng đơn đang chạy, Manager quyết định giữ Sale cũ hoàn tất hay chuyển quyền xử lý.

**Bước 6:** Hệ thống cập nhật SalesStaffOwnerId có hiệu lực từ thời điểm phê duyệt, tạo AssignmentHistory và gửi thông báo cho khách, Sale cũ và Sale mới. Đơn COMPLETED giữ nguyên snapshot Sale lịch sử.

**Điểm kiểm soát và ngoại lệ**

·   Đơn đang giao thường do Sale cũ hoàn tất; chỉ Manager được override khi có rủi ro hoặc khiếu nại nghiêm trọng.

·   Nếu Sale mong muốn inactive hoặc quá tải, Manager chọn Sale khác và bắt buộc ghi lý do.

·   Mọi thay đổi quyền sở hữu khách và đơn phải có before/after audit log.

**LUỒNG 8: SALE TIẾP NHẬN ĐƠN COD, XÁC NHẬN ĐƠN VÀ TẠO ĐƠN THỦ CÔNG**

Đây là luồng vận hành phía Sale đối với đơn COD và các đơn phát sinh ngoài website, bảo đảm SLA giữ tồn 35 phút và ngăn việc xác nhận đơn khi tồn đã hết hạn hoặc dữ liệu chưa hợp lệ.

**Bước 1:** Khi đơn COD được tạo, hệ thống gán đơn cho Sale đang sở hữu khách hàng, đặt OrderStatus \= PENDING\_CONFIRMATION và bắt đầu đếm SLA trên InventoryReservation 35 phút.

**Bước 2:** Sale kiểm tra thông tin khách, địa chỉ, giá do server tính, yêu cầu VAT, phương thức thanh toán và ghi chú giao hàng. Sale liên hệ khách để xác nhận nhu cầu thực tế.

**Bước 3:** Tại phút 25 hệ thống cảnh báo Sale; phút 30 gửi escalation cho Sales Manager; phút 35 nếu chưa xác nhận thì reservation tự hết hạn và số lượng giữ tạm được trả về Available.

**Bước 4:** Nếu khách xác nhận và tồn còn hợp lệ, Sale bấm Confirm COD. Hệ thống chuyển RESERVED sang ALLOCATED, cập nhật Order thành CONFIRMED và sinh FulfillmentOrder, Allocation và PickTask cho kho.

**Bước 5:** Nếu dữ liệu sai hoặc khách chưa sẵn sàng, Sale chọn yêu cầu bổ sung, từ chối hoặc hủy có reason code. Hệ thống lưu toàn bộ lịch sử trao đổi và thời điểm xử lý SLA.

**Bước 6:** Khi tạo đơn thủ công, Sale chọn hoặc tạo khách hàng, thêm SKU, số lượng và phương thức thanh toán. Giá vẫn phải được server tính; khách mới hoặc đơn đầu tiên phải hoàn tất SMS OTP trước khi Order được tạo.

**Bước 7:** Đơn SePay hợp lệ đã được hệ thống auto-confirm không hiển thị nút Confirm cho Sale. Chỉ Sales Manager xử lý ngoại lệ qua MGR-05 khi webhook, bằng chứng tiền hoặc allocation có vấn đề.

**Điểm kiểm soát và ngoại lệ**

·   Không cho xác nhận COD sau khi reservation hết hạn nếu chưa kiểm tra và giữ tồn lại.

·   Manager có quyền reassign đơn trước phút 35 khi Sale không phản hồi.

·   Thay đổi SKU, số lượng hoặc địa chỉ sau CONFIRMED phải qua change request, reallocation và audit; không sửa trực tiếp.

**LUỒNG 9: PURCHASE ORDER, NHẬP KHO VÀ ĐỐI CHIẾU HÀNG NHÀ CUNG CẤP**

Luồng này quản lý chiều mua vào của doanh nghiệp: CEO lập Purchase Order, kho nhận hàng thực tế, hệ thống chỉ tăng tồn sau khi phiếu nhập đã được Post và mọi chênh lệch được xử lý có bằng chứng.

**Bước 1:** CEO tạo Purchase Order thủ công hoặc import từ Excel/ảnh. File Excel được validate theo từng dòng; ảnh được OCR kèm confidence score. Tất cả dữ liệu import chỉ tạo PO ở trạng thái DRAFT để preview và sửa.

**Bước 2:** CEO kiểm tra nhà cung cấp, kho nhận, SKU, đơn vị tính, số lượng, đơn giá và ngày dự kiến. Dòng lỗi hoặc OCR confidence thấp phải được sửa hoặc xác nhận thủ công trước khi phát hành.

**Bước 3:** CEO phát hành PO, hệ thống chuyển trạng thái sang ISSUED/SENT\_TO\_WAREHOUSE, khóa các dòng đã phát hành và gửi thông báo cho kho. Việc phát hành PO không làm tăng bất kỳ số lượng tồn kho nào.

**Bước 4:** Khi hàng đến, Warehouse Staff mở PO và tạo GoodsReceipt theo từng đợt. Hệ thống hiển thị số lượng ordered, đã nhận trước đó và remaining để tránh nhập vượt hoặc nhận trùng.

**Bước 5:** Kho kiểm đếm và phân loại số lượng Good, Short, Excess, Damaged hoặc Wrong Item; nhập lot, hạn dùng và đính kèm ảnh/biên bản. Phần không đạt được đưa vào holding hoặc Quarantine, không tính Available.

**Bước 6:** Kho Post phần hàng đạt. Hệ thống tạo StockTransaction, tăng OnHand tại đúng warehouse/location và cập nhật PO thành PARTIALLY\_RECEIVED hoặc FULLY\_RECEIVED.

**Bước 7:** Nếu có chênh lệch, PO chuyển DISCREPANCY\_REVIEW. CEO quyết định chấp nhận hàng thừa, yêu cầu nhà cung cấp bổ sung, trả hàng, hoặc đóng phần thiếu; sau đó hệ thống cập nhật CLOSED khi nghĩa vụ đã hoàn tất.

**Điểm kiểm soát và ngoại lệ**

·   Phiếu nhập đã Post không được sửa trực tiếp; sai phải tạo reversal hoặc adjustment đối ứng.

·   Hỗ trợ nhận PO nhiều đợt và mỗi receipt line phải liên kết PO line để đối soát.

·   Import Excel/OCR phải có preview-confirm và idempotency để không tạo PO trùng khi người dùng gửi lại yêu cầu.

**LUỒNG 10: ĐIỀU CHUYỂN KHO, KIỂM KÊ, ĐIỀU CHỈNH VÀ CẢNH BÁO TỒN**

Ngoài điều chuyển phục vụ tập kết đơn hàng, hệ thống còn phải hỗ trợ cân bằng tồn giữa ba kho, kiểm kê định kỳ và điều chỉnh chênh lệch có phê duyệt.

**Bước 1:** Khi cần cân bằng tồn hoặc bổ sung kho đích, Warehouse Staff tạo StockTransfer với kho nguồn, kho đích, SKU, số lượng và lý do. Hệ thống kiểm tra Available và giữ số lượng cho transfer.

**Bước 2:** Kho nguồn pick, pack và Post phiếu xuất điều chuyển. OnHand/Available tại nguồn giảm theo chứng từ và InTransitQuantity tăng; transfer không được Post hai lần.

**Bước 3:** Kho đích nhận hàng, kiểm đếm actual, ghi nhận thiếu/hỏng và đính kèm evidence. Hệ thống giảm InTransit, tăng tồn phần đạt tại đích và chuyển transfer sang RECEIVED hoặc DISCREPANCY.

**Bước 4:** Đối với kiểm kê, kho tạo StockCountSession theo warehouse/location/SKU. Hệ thống chụp TheorySnapshot bất biến tại thời điểm mở phiên; nhân viên nhập số thực tế bằng tay hoặc Excel.

**Bước 5:** Hệ thống tính chênh lệch số lượng và giá trị. Kho nhập lý do, bằng chứng và submit; chưa được duyệt thì InventoryBalance không thay đổi.

**Bước 6:** CEO hoặc người phê duyệt theo ngưỡng giá trị chấp nhận hoặc từ chối. Khi duyệt, hệ thống tạo StockAdjustment và StockTransaction; khi từ chối, phiên được đóng mà không cập nhật tồn.

**Bước 7:** Scheduled job theo dõi mức tồn tối thiểu, tạo LowStockAlert và thông báo cho người có trách nhiệm. Cảnh báo được đóng khi tồn đã được bổ sung hoặc người có quyền xác nhận xử lý.

**Điểm kiểm soát và ngoại lệ**

·   Transfer đang InTransit không được cancel thẳng; phải tạo reverse/return transfer có chứng từ.

·   Không mở phiên kiểm kê trùng phạm vi nếu chính sách đang khóa kho/location đó.

·   InventoryBalance, transfer receipt và stock adjustment bắt buộc dùng RowVersion/optimistic concurrency để tránh ghi đè và âm kho.

**LUỒNG 11: XUẤT NGUYÊN LIỆU CHO SẢN XUẤT NGOÀI HỆ THỐNG**

Do hệ thống không triển khai tài khoản Production Staff hoặc MES chi tiết, việc giao nguyên liệu cho bộ phận sản xuất được kiểm soát bằng chứng từ giấy và tài khoản Warehouse Staff.

**Bước 1:** Bộ phận sản xuất gửi yêu cầu nguyên liệu ngoài hệ thống. Warehouse Staff tạo GoodsIssue loại MATERIAL\_TO\_PRODUCTION, chọn SKU, số lượng, bộ phận nhận và mục đích sử dụng.

**Bước 2:** Hệ thống kiểm tra tồn khả dụng. Kho pick nguyên liệu, in biên bản và chuẩn bị hàng; chưa đủ hàng thì không cho Post quá số lượng có thể xuất.

**Bước 3:** Đại diện sản xuất kiểm đếm và ký biên bản giấy. Người nhận không cần tài khoản hệ thống nhưng phải được ghi rõ họ tên và bộ phận.

**Bước 4:** Warehouse Staff nhập ExternalRecipientName, Department, ReceivedAt, PaperDocumentNumber và chụp ảnh biên bản có chữ ký đính kèm vào phiếu.

**Bước 5:** Khi bằng chứng đầy đủ, Warehouse Staff Post GoodsIssue. Hệ thống giảm tồn, khóa chứng từ và ghi audit actor, thời gian, số biên bản và attachment.

**Bước 6:** Nếu phiếu đã Post sai, kho không được sửa trực tiếp mà tạo reversal và phát hành phiếu đúng mới để bảo toàn lịch sử biến động tồn.

**Điểm kiểm soát và ngoại lệ**

·   Thiếu ảnh, chữ ký hoặc số biên bản thì không được Post.

·   PaperDocumentNumber phải duy nhất để ngăn ghi nhận trùng một lần giao nguyên liệu.

·   Không tạo role Driver hoặc ProductionStaff chỉ để ký nhận; người ngoài hệ thống được lưu dưới dạng external recipient.

**LUỒNG 12: AI MARKETING, KIỂM DUYỆT VÀ ĐĂNG FACEBOOK PAGE**

Luồng này hỗ trợ Sale tạo nội dung marketing bằng AI nhưng bắt buộc qua Sale Manager kiểm duyệt trước khi đăng, đồng thời tách hoàn toàn hoạt động marketing khỏi quy tắc phân bổ quyền sở hữu khách hàng.

**Bước 1:** Sales Staff chọn sản phẩm, template, mục tiêu, tone và prompt. Hệ thống gọi dịch vụ AI để tạo từ 2 đến 4 phương án ảnh và caption, sau đó lưu media vào thư viện nội bộ.

**Bước 2:** Sale chỉnh sửa nội dung, hashtag, CTA và chọn phương án phù hợp; bài được lưu DRAFT và submit approval khi hoàn tất.

**Bước 3:** Sales Manager xem preview, đối chiếu thông tin sản phẩm và chính sách, chỉnh sửa nếu cần, sau đó approve, reject hoặc yêu cầu Sale làm lại. Bài chưa duyệt tuyệt đối không được publish.

**Bước 4:** Manager chọn thời gian đăng. Hệ thống kiểm tra trùng lịch và giới hạn số bài scheduled theo cấu hình; đến giờ, SocialPostScheduler gọi Facebook Graph API và lưu ExternalPostId.

**Bước 5:** Nếu publish thất bại, trạng thái chuyển PUBLISH\_FAILED, hệ thống ghi lỗi, retry theo policy và cho phép Admin/Manager thao tác lại thủ công.

**Bước 6:** Scheduled job định kỳ lấy reach, like, comment và share. CEO/Admin xem lịch marketing, hiệu quả bài và lịch sử lỗi trên dashboard.

**Bước 7:** Khách truy cập từ bài Facebook vẫn được phân Sale theo khách cũ, referral hoặc Round-robin. Bài viết hoặc người tạo bài không được dùng làm AssignmentSource.

**Điểm kiểm soát và ngoại lệ**

·   Không đăng nội dung chưa được Sale Manager duyệt; mọi lần edit/approve/schedule phải có audit.

·   Thông tin giá và sản phẩm trong caption phải lấy từ dữ liệu hiện hành, tránh AI tự tạo số liệu không tồn tại.

·   Token Facebook, API key AI và thông tin tích hợp chỉ được lưu trong cấu hình bảo mật, không xuất hiện trong log nghiệp vụ.

**LUỒNG 13: QUẢN TRỊ HỆ THỐNG, AUDIT, SCHEDULED JOBS, DASHBOARD VÀ KPI**

Đây là luồng quản trị xuyên suốt, bảo đảm cấu hình có phiên bản, quyền hạn đúng vai trò, các tác vụ nền được giám sát và mọi báo cáo được tính từ dữ liệu server thay vì số liệu gửi từ frontend.

**Bước 1:** Admin quản lý user, role, sản phẩm, SKU, danh mục, kho/location, nhà cung cấp, xe/ca, DiscountTier và cấu hình tích hợp SePay, SMS, email, Google OAuth, Facebook, OCR/AI.

**Bước 2:** Mỗi thay đổi cấu hình được lưu version, effective date, actor và before/after audit. Admin không được dùng quyền kỹ thuật để quyết định nghiệp vụ báo giá, hủy Paid hoặc điều chỉnh kho thay cho Manager/CEO.

**Bước 3:** Hệ thống chạy các scheduled jobs như hết hạn reservation, cảnh báo SLA COD, low-stock, retry webhook/outbox, publish bài, lấy marketing metrics và các tác vụ tổng hợp KPI.

**Bước 4:** Admin theo dõi job run, retry, failure, webhook log và import log trên màn hình system health. Log được che OTP, token, password, secret và dữ liệu nhạy cảm.

**Bước 5:** KPI được tính ở server: Revenue là tổng giá trị Order COMPLETED theo Sale được gán; DeliverySuccess là tỷ lệ Delivered trên tổng lượt giao; ProcessingSpeed là thời gian trung bình từ assigned đến confirmed; ReturningCustomer là tỷ lệ khách có ít nhất hai đơn COMPLETED.

**Bước 6:** Sales Staff xem dashboard cá nhân; Sales Manager xem toàn đội, SLA, công nợ và ngoại lệ; CEO xem doanh thu, tồn kho, PO, chênh lệch và hiệu quả marketing. Mỗi role chỉ truy cập đúng phạm vi dữ liệu.

**Bước 7:** Audit log được phép tìm kiếm và export nhưng không sửa hoặc xóa. Hành động không đủ quyền trả 403 và được ghi security log khi có dấu hiệu bất thường.

**Điểm kiểm soát và ngoại lệ**

·   Chặn Admin tự nâng quyền nhạy cảm cho chính mình hoặc yêu cầu cơ chế phê duyệt riêng theo cấu hình.

·   Thay đổi pricing config không được làm thay đổi PriceSnapshot của giỏ hàng còn hiệu lực 24 giờ.

·   Job thất bại phải có retry, trạng thái cuối cùng và cảnh báo; không được chạy lặp gây trùng giao dịch nghiệp vụ.

| Ràng buộc kỹ thuật xuyên suốt cho các luồng bổ sung ·  Dùng optimistic concurrency/RowVersion cho InventoryBalance, RoundRobinCursor, CustomerOrderCredit, StockTransfer, GoodsReceipt/GoodsIssue và các bản ghi có thể bị cập nhật đồng thời. ·  Dùng Idempotency-Key hoặc khóa nghiệp vụ cho checkout, import PO, Post/Receive transfer, GoodsReceipt, GoodsIssue, webhook và scheduled job để ngăn tạo giao dịch trùng. ·  Các thao tác thay đổi nhiều thực thể phải chạy trong transaction; sự kiện gửi ra ngoài dùng outbox để tránh trạng thái DB đã lưu nhưng notification/event bị mất. ·  Chứng từ đã Post và quotation/chat đã gửi là bất biến; khi sai phải tạo version mới, reversal hoặc adjustment thay vì sửa trực tiếp. ·  Mọi thao tác thay đổi Sale, giá, thanh toán, tồn kho, credit, role và cấu hình tích hợp phải ghi audit đầy đủ actor, thời gian, before/after và reason. |
| :---- |

*Kết quả: kết hợp 5 luồng cốt lõi đã có với 8 luồng bổ sung trên sẽ bao phủ đầy đủ 19 workflow nghiệp vụ trong đặc tả v6.0, từ phía khách hàng, bán hàng, kho, mua hàng, vận chuyển, hậu mãi đến quản trị và marketing.*  
