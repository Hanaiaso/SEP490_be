using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-FulfilmentDeliveryAPI</c> — FUL-01..09, DEL-01..09.
    ///
    /// Lưu ý ánh xạ: workbook mô tả một module "Delivery Trip" (POST /api/delivery/trips, /attempts,
    /// /collections) mà hệ thống CHƯA triển khai — xem nhóm C trong tests/l3_endpoint_map.csv.
    /// Các case đó được kiểm bằng cách chứng minh route không tồn tại (404) và ghi defect DEF-L3-004,
    /// đồng thời kiểm phần chức năng TƯƠNG ĐƯƠNG đang có (/api/delivery/schedule, /{orderId}/complete).
    /// </summary>
    public class L3FulfilmentDeliveryApiTests : L3TestBase
    {
        public L3FulfilmentDeliveryApiTests(L3SqlFixture factory) : base(factory) { }

        /// <summary>Phiếu xuất kho ở trạng thái Draft tại WH-DEFAULT, kèm 1 dòng hàng.</summary>
        private async Task<(GoodsIssue issue, Product product)> SeedDraftGoodsIssueAsync(
            int stockOnHand, int issueQuantity, GoodsIssueType type = GoodsIssueType.SalesOrder)
        {
            var (product, _) = await SeedSellableProductAsync(50_000m, stockOnHand);
            var issue = new GoodsIssue
            {
                Id = Guid.NewGuid(),
                Code = "GI-L3-" + Guid.NewGuid().ToString("N")[..6],
                Type = type,
                WarehouseId = L3Seed.WarehouseDefaultId,
                IssuedByUserId = L3Seed.WarehouseStaffId,
                Status = GoodsIssueStatus.Draft,
            };
            await SeedAsync(db =>
            {
                db.GoodsIssues.Add(issue);
                db.GoodsIssueItems.Add(new GoodsIssueItem
                {
                    Id = Guid.NewGuid(),
                    GoodsIssueId = issue.Id,
                    ProductId = product.Id,
                    Quantity = issueQuantity,
                });
                return Task.CompletedTask;
            });
            return (issue, product);
        }

        // ── Block: Fulfilment (allocation -> pick -> handover -> goods issue) ─────────────────

        /// FUL-01 | Input-Domain-Happy | FT-05 AC-01; BR-011  ->  <b>FAIL — defect DEF-L3-006 (P1, OWASP A01)</b>
        ///
        /// Nhân viên kho xem được hàng đợi đơn cần xuất (workbook ghi
        /// GET /api/warehouse/orders/fulfillment-orders; endpoint thật là GET /api/warehouse/orders).
        /// Phần này ĐÚNG.
        ///
        /// Nhưng cùng endpoint đó KHÔNG chặn vai trò khác: <c>WarehouseController</c> chỉ có
        /// <c>[Authorize]</c> ở cấp class (dòng 12) và 4 endpoint ĐỌC (dòng 34, 51, 115, 132) không
        /// khai báo <c>[Authorize(Roles = ...)]</c>, trong khi MỌI endpoint GHI đều có. Hệ quả: bất kỳ
        /// tài khoản đã đăng nhập nào — kể cả Customer — đọc được toàn bộ hàng đợi xuất kho, chi tiết
        /// đơn của khách khác và danh sách pick task. Vi phạm NFR-SEC03 (OWASP A01 Broken Access Control).
        ///
        /// Test này CỐ Ý đỏ: sẽ xanh khi 4 endpoint đọc được gắn role như các endpoint ghi.
        [Fact]
        public async Task L3_FUL_01_WarehouseQueue_VisibleToWarehouseStaff_NotToCustomer()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            // tabType là tham số BẮT BUỘC (WarehouseController.cs:35); thiếu nó service ném -> 400.
            var res = await warehouse.GetAsync("/api/warehouse/orders?tabType=OnlinePending");

            res.StatusCode.Should().Be(HttpStatusCode.OK, "nhân viên kho phải xem được hàng đợi");

            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customer.GetAsync("/api/warehouse/orders?tabType=OnlinePending")).StatusCode
                .Should().Be(HttpStatusCode.Forbidden,
                    "DEF-L3-006: 4 endpoint DOC cua WarehouseController (dong 34, 51, 115, 132) thieu " +
                    "[Authorize(Roles = ...)] nen bat ky tai khoan da dang nhap nao — ke ca Customer — " +
                    "cung doc duoc toan bo hang doi xuat kho va chi tiet don cua khach khac");
        }

        /// FUL-02 | Input-Domain-Error | FT-05 NAC-02
        /// Post phiếu xuất khi tồn khả dụng KHÔNG đủ -> bị chặn, tồn kho KHÔNG đổi.
        [Fact]
        public async Task L3_FUL_02_PostGoodsIssue_PrerequisiteNotMet_Rejected_InventoryUnchanged()
        {
            var (issue, product) = await SeedDraftGoodsIssueAsync(stockOnHand: 1, issueQuantity: 10);
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var res = await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { });

            res.IsSuccessStatusCode.Should().BeFalse("không đủ tồn thì không được post");
            var inventory = await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id));
            inventory.OnHandQuantity.Should().Be(1, "tồn kho phải giữ nguyên khi phiếu bị từ chối");
            (await QueryAsync(db => db.GoodsIssues.SingleAsync(g => g.Id == issue.Id)))
                .Status.Should().Be(GoodsIssueStatus.Draft);
        }

        /// FUL-03 | BVA | FT-05 BV-02; NAC-04
        /// Hoàn thành pick task: đúng vai trò kho mới được thao tác; vai trò khác -> 403.
        [Fact]
        public async Task L3_FUL_03_CompletePickTask_RoleGate()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customer.PostAsJsonAsync(
                    $"/api/warehouse/orders/pick-tasks/{Guid.NewGuid()}/complete", new { }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            (await warehouse.PostAsJsonAsync(
                    $"/api/warehouse/orders/pick-tasks/{Guid.NewGuid()}/complete", new { }))
                .StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
                    "nhân viên kho phải lọt qua cổng phân quyền");
        }

        /// FUL-04 | BVA | FT-05 BV-03; AC-05; BR-034
        /// Biên bản bàn giao phải có XÁC NHẬN KÉP: kho xác nhận và Sales xác nhận là hai endpoint riêng,
        /// mỗi bên chỉ được gọi endpoint của mình.
        [Fact]
        public async Task L3_FUL_04_HandoverDualConfirm_EachSideOnlyOwnEndpoint()
        {
            var handoverId = Guid.NewGuid();
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            // Nhân viên kho KHÔNG được ký thay phía Sales.
            (await warehouse.PostAsJsonAsync($"/api/handover-records/{handoverId}/sales-confirm", new { }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, "kho không được xác nhận thay Sales");

            // Và ngược lại.
            (await sales.PostAsJsonAsync($"/api/handover-records/{handoverId}/warehouse-confirm", new { }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, "Sales không được xác nhận thay kho");

            // Mỗi bên gọi đúng endpoint của mình thì phải lọt qua cổng phân quyền.
            (await warehouse.PostAsJsonAsync($"/api/handover-records/{handoverId}/warehouse-confirm", new { }))
                .StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }

        /// FUL-05 | Idempotency | FT-05 AC-05; NAC-05; BR-029
        /// Post phiếu xuất lần 2 -> bị từ chối, tồn kho KHÔNG bị trừ lần thứ hai.
        [Fact]
        public async Task L3_FUL_05_PostGoodsIssue_Twice_Idempotent_StockDeductedOnlyOnce()
        {
            var (issue, product) = await SeedDraftGoodsIssueAsync(stockOnHand: 100, issueQuantity: 10);
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var first = await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { });
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(90, "lần post đầu trừ đúng 10");

            var second = await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { });

            second.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "chứng từ đã post là bất biến (POSTED_DOCUMENT_IMMUTABLE)");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(90, "KHÔNG được trừ tồn lần thứ hai");
        }

        /// FUL-06 | Input-Domain-Error | FT-05 NAC-01; BV-01; BR-032
        /// Một dòng vượt tồn khả dụng -> rollback TOÀN BỘ phiếu, không trừ một phần dòng nào.
        [Fact]
        public async Task L3_FUL_06_PostGoodsIssue_OneLineExceedsStock_RollsBackEveryLine()
        {
            var (okProduct, _) = await SeedSellableProductAsync(50_000m, 100);
            var (shortProduct, _) = await SeedSellableProductAsync(50_000m, 1);

            var issue = new GoodsIssue
            {
                Id = Guid.NewGuid(),
                Code = "GI-L3-ROLLBACK",
                Type = GoodsIssueType.SalesOrder,
                WarehouseId = L3Seed.WarehouseDefaultId,
                IssuedByUserId = L3Seed.WarehouseStaffId,
                Status = GoodsIssueStatus.Draft,
            };
            await SeedAsync(db =>
            {
                db.GoodsIssues.Add(issue);
                // Dòng hợp lệ đứng TRƯỚC dòng thiếu tồn -> nếu không rollback, dòng 1 đã bị trừ.
                db.GoodsIssueItems.Add(new GoodsIssueItem
                { Id = Guid.NewGuid(), GoodsIssueId = issue.Id, ProductId = okProduct.Id, Quantity = 5 });
                db.GoodsIssueItems.Add(new GoodsIssueItem
                { Id = Guid.NewGuid(), GoodsIssueId = issue.Id, ProductId = shortProduct.Id, Quantity = 999 });
                return Task.CompletedTask;
            });

            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var res = await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { });

            res.IsSuccessStatusCode.Should().BeFalse();
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == okProduct.Id)))
                .OnHandQuantity.Should().Be(100, "dòng hợp lệ KHÔNG được trừ một phần — phải rollback toàn bộ");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == shortProduct.Id)))
                .OnHandQuantity.Should().Be(1);
        }

        /// FUL-07 | Input-Domain-Error | FT-05 NAC-05; NFR-SEC03
        /// Nhân viên kho thao tác NGOÀI phạm vi kho được gán -> không được phép đổi tồn kho đó.
        /// Nhân viên WH-TRADE (WarehouseStaff2) thử điều chỉnh tồn của WH-DEFAULT.
        [Fact]
        public async Task L3_FUL_07_WarehouseStaff_ActionOutsideAssignedWarehouse_Rejected()
        {
            var inventoryId = L3Seed.InventoryPeWrapDefaultId; // tồn tại WH-DEFAULT
            var before = await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == inventoryId));

            var otherWarehouseStaff = await ClientForSeededAsync(L3Seed.WarehouseStaff2Id);

            var res = await otherWarehouseStaff.PutAsJsonAsync($"/api/inventory/{inventoryId}/adjust",
                new { NewQuantity = 1, Reason = "thu doi kho khac" });

            res.IsSuccessStatusCode.Should().BeFalse("không được thao tác kho ngoài phạm vi được gán");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.Id == inventoryId)))
                .OnHandQuantity.Should().Be(before.OnHandQuantity, "tồn kho không được đổi");
        }

        /// FUL-08 | Input-Domain-Error | FT-05 NAC-03; BR-013  ->  nhóm C
        /// Workbook: POST /api/warehouse/orders/multi-pick khi chưa được Sales Manager duyệt.
        /// Hệ thống CHƯA có endpoint multi-pick — chứng minh bằng 404 (xem DEF-L3-005).
        [Fact]
        public async Task L3_FUL_08_ExecuteMultiPick_WithoutManagerApproval_Rejected409()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);
            var orderIds = new List<Guid>
            {
                await SeedDeliverableOrderAsync(),
                await SeedDeliverableOrderAsync(),
            };

            // NAC-03/BR-013: gộp pick nhiều đơn PHẢI được Sales Manager duyệt trước.
            var res = await warehouse.PostAsJsonAsync("/api/warehouse/orders/multi-pick",
                new { OrderIds = orderIds });

            res.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "chưa có kế hoạch gộp pick được duyệt; body: {0}",
                await res.Content.ReadAsStringAsync());
            (await ReadJsonAsync(res)).GetProperty("code").GetString()
                .Should().Be("FULFILMENT_PLAN_CONFLICT");

            // Sau khi Sales Manager duyệt đúng danh sách đó thì mới thực thi được.
            var request = await warehouse.PostAsJsonAsync("/api/warehouse/orders/multi-pick/request",
                new { OrderIds = orderIds });
            request.StatusCode.Should().Be(HttpStatusCode.OK,
                "đề xuất gộp pick phải tạo được; body: {0}", await request.Content.ReadAsStringAsync());

            var approvalId = (await ReadJsonAsync(request)).GetProperty("id").GetGuid();
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);
            var decision = await manager.PostAsJsonAsync(
                $"/api/warehouse/orders/multi-pick/{approvalId}/decision",
                new { Approved = true, Note = "Duyet gop pick L3-FUL-08" });
            decision.StatusCode.Should().Be(HttpStatusCode.OK,
                "Sales Manager duyệt thất bại; body: {0}", await decision.Content.ReadAsStringAsync());

            var afterApproval = await warehouse.PostAsJsonAsync("/api/warehouse/orders/multi-pick",
                new { OrderIds = orderIds });
            afterApproval.StatusCode.Should().NotBe(HttpStatusCode.Conflict,
                "đã được duyệt thì không còn bị chặn bởi FULFILMENT_PLAN_CONFLICT; body: {0}",
                await afterApproval.Content.ReadAsStringAsync());
        }

        /// FUL-09 | Workflow | FT-05 AC-02; BR-032; BR-033; NFR-D01
        /// Sau khi xuất kho, tổng tồn phải nhất quán: OnHand giảm đúng lượng xuất và
        /// AvailableQuantity không bao giờ âm.
        [Fact]
        public async Task L3_FUL_09_InventoryInvariant_AvailableNeverNegative_AfterPosting()
        {
            var (issue, product) = await SeedDraftGoodsIssueAsync(stockOnHand: 20, issueQuantity: 20);
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            (await warehouse.PostAsJsonAsync($"/api/goods-issues/{issue.Id}/post", new { }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var inventory = await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id));
            inventory.OnHandQuantity.Should().Be(0);
            // Kiểm biểu thức THÔ, không dùng AvailableQuantity (property đã floor về 0).
            (inventory.OnHandQuantity - inventory.ReservedQuantity - inventory.AllocatedQuantity
             - inventory.DamagedQuantity - inventory.QuarantineQuantity)
                .Should().BeGreaterThanOrEqualTo(0, "tồn khả dụng thô không được âm");

            (await QueryAsync(db => db.StockTransactions.CountAsync(t => t.ReferenceId == issue.Id)))
                .Should().Be(1, "mỗi lần post ghi đúng 1 bút toán tồn kho");
        }

        // ── Block: Delivery (lập chuyến -> POD -> thu COD) ────────────────────────────────────

        /// <summary>
        /// Ca giao hàng hợp lệ theo DeliveryDtos (RegularExpression): "Sáng" / "Trưa" / "Chiều".
        /// Workbook Report_5_3 ghi Morning/Noon/Afternoon — lệch tên, giá trị thật là tiếng Việt.
        /// </summary>
        private const string ShiftSang = "Sáng";
        private const string ShiftChieu = "Chiều";

        /// <summary>Đơn COD sẵn sàng xếp chuyến (DeliveryStatus = NotScheduled).</summary>
        private async Task<Guid> SeedDeliverableOrderAsync(decimal orderTotal = 500_000m)
        {
            var (_, profile) = await SeedVerifiedCustomerAsync(NewEmail(), L3Seed.DefaultPassword);
            var orderId = Guid.NewGuid();
            await SeedAsync(db =>
            {
                db.Orders.Add(new Order
                {
                    Id = orderId,
                    OrderCode = "VT-L3-" + Guid.NewGuid().ToString("N")[..10],
                    CustomerProfileId = profile.Id,
                    OrderStatus = OrderStatus.Confirmed,
                    PaymentMethod = PaymentMethod.COD,
                    PaymentStatus = PaymentStatus.Unpaid,
                    DeliveryStatus = DeliveryStatus.NotScheduled,
                    TotalAmount = orderTotal,
                    FinalPayment = orderTotal,
                    AmountPaid = 0,
                    ShippingAddress = "L3 dia chi giao",
                    CreatedAt = DateTime.UtcNow,
                });
                return Task.CompletedTask;
            });
            return orderId;
        }

        /// <summary>Đơn COD đã được gom vào một chuyến giao — nền cho DEL-04/DEL-07/FLOW-04.</summary>
        private async Task<(HttpClient sales, Guid orderId, Guid tripId)> ArrangeOrderInTripAsync(
            decimal orderTotal = 500_000m)
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            var orderId = await SeedDeliverableOrderAsync(orderTotal);

            var vehicleId = await QueryAsync(db => db.Vehicles
                .Where(v => v.IsActive).Select(v => v.Id).FirstAsync());
            var created = await sales.PostAsJsonAsync("/api/delivery/trips", new
            {
                VehicleId = vehicleId, Shift = ShiftSang,
                TripDate = DateTime.UtcNow.Date.AddDays(1),
                OrderIds = new List<Guid> { orderId }
            });
            created.StatusCode.Should().Be(HttpStatusCode.OK,
                "dựng chuyến giao cho test thất bại; body: {0}", await created.Content.ReadAsStringAsync());

            var tripId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
            return (sales, orderId, tripId);
        }

        /// DEL-01 | BVA | FT-07 BV-01; AC-01; BR-037  ->  nhóm C
        /// Workbook: POST /api/delivery/trips với ràng buộc xe/ca/ngày không trùng.
        /// Hệ thống chưa có module chuyến giao — endpoint tương đương gần nhất là /api/delivery/schedule.
        [Fact]
        public async Task L3_DEL_01_CreateTrip_SecondTripSameVehicleShiftDate_Conflict409()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            var vehicleId = await QueryAsync(db => db.Vehicles
                .Where(v => v.IsActive).Select(v => v.Id).FirstAsync());
            var tripDate = DateTime.UtcNow.Date.AddDays(1);

            var first = await sales.PostAsJsonAsync("/api/delivery/trips", new
            {
                VehicleId = vehicleId, Shift = ShiftSang, TripDate = tripDate,
                OrderIds = new List<Guid> { await SeedDeliverableOrderAsync() }
            });
            first.StatusCode.Should().Be(HttpStatusCode.OK,
                "lần xếp chuyến đầu tiên phải thành công; body: {0}",
                await first.Content.ReadAsStringAsync());

            // BR-037/BV-01: cùng xe + cùng ca + cùng ngày -> chuyến thứ 2 bị chặn.
            var second = await sales.PostAsJsonAsync("/api/delivery/trips", new
            {
                VehicleId = vehicleId, Shift = ShiftSang, TripDate = tripDate,
                OrderIds = new List<Guid> { await SeedDeliverableOrderAsync() }
            });
            second.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "1 xe chỉ chạy 1 chuyến mỗi ca mỗi ngày; body: {0}",
                await second.Content.ReadAsStringAsync());
            (await ReadJsonAsync(second)).GetProperty("code").GetString()
                .Should().Be("VEHICLE_SHIFT_CONFLICT");

            // Ca khác cùng xe/ngày thì vẫn xếp được — chứng minh ràng buộc đúng chiều.
            var otherShift = await sales.PostAsJsonAsync("/api/delivery/trips", new
            {
                VehicleId = vehicleId, Shift = ShiftChieu, TripDate = tripDate,
                OrderIds = new List<Guid> { await SeedDeliverableOrderAsync() }
            });
            otherShift.StatusCode.Should().Be(HttpStatusCode.OK, "khác ca thì không xung đột");
        }

        /// DEL-02 | Input-Domain-Error | FT-07 NAC-01  ->  nhóm C
        /// Xe đang bảo trì/inactive không được xếp chuyến — không có API chuyến giao để kiểm.
        [Fact]
        public async Task L3_DEL_02_CreateTrip_InactiveVehicle_Rejected409()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            // Đưa 1 xe về trạng thái ngừng hoạt động (mô phỏng đang bảo trì).
            var vehicleId = await QueryAsync(db => db.Vehicles.Select(v => v.Id).FirstAsync());
            await SeedAsync(async db =>
            {
                var v = await db.Vehicles.SingleAsync(x => x.Id == vehicleId);
                v.IsActive = false;
            });

            var res = await sales.PostAsJsonAsync("/api/delivery/trips", new
            {
                VehicleId = vehicleId, Shift = ShiftSang,
                TripDate = DateTime.UtcNow.Date.AddDays(1),
                OrderIds = new List<Guid> { await SeedDeliverableOrderAsync() }
            });

            res.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "NAC-01: xe ngừng hoạt động không được xếp chuyến; body: {0}",
                await res.Content.ReadAsStringAsync());
            (await ReadJsonAsync(res)).GetProperty("code").GetString()
                .Should().Be("VEHICLE_NOT_AVAILABLE");

            (await QueryAsync(db => db.DeliveryTrips.CountAsync(t => t.VehicleId == vehicleId)))
                .Should().Be(0, "bị chặn thì không được tạo chuyến nào");
        }

        /// DEL-03 | Input-Domain-Error | FT-07 NAC-02; BR-034  ->  nhóm C
        [Fact]
        public async Task L3_DEL_03_StartTripWithoutHandover_EndpointNotImplemented()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            (await sales.PostAsJsonAsync($"/api/delivery/trips/{Guid.NewGuid()}/start", new { }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, "DEF-L3-004");
        }

        /// DEL-04 | Input-Domain-Error | FT-07 NAC-03; BR-038  ->  nhóm C
        /// POD (ảnh/chữ ký/timestamp) chưa có endpoint riêng; gần nhất là /{orderId}/complete.
        [Fact]
        public async Task L3_DEL_04_RecordAttempt_DeliveredWithoutProof_Rejected422()
        {
            var (sales, orderId, _) = await ArrangeOrderInTripAsync();

            // Đánh dấu DELIVERED nhưng thiếu ảnh hiện trường và chữ ký khách.
            var res = await sales.PostAsJsonAsync("/api/delivery/attempts", new
            {
                OrderId = orderId, Outcome = "Delivered", PhotoUrl = (string?)null, SignatureUrl = (string?)null
            });

            res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
                "BR-038: POD thiếu ảnh/chữ ký thì không được đánh dấu đã giao; body: {0}",
                await res.Content.ReadAsStringAsync());
            (await ReadJsonAsync(res)).GetProperty("code").GetString()
                .Should().Be("POD_INCOMPLETE_OR_INVALID");

            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .DeliveryStatus.Should().NotBe(DeliveryStatus.Delivered,
                    "bị chặn thì đơn không được chuyển sang đã giao");

            // Có đủ bằng chứng thì phải nhận.
            var ok = await sales.PostAsJsonAsync("/api/delivery/attempts", new
            {
                OrderId = orderId, Outcome = "Delivered",
                PhotoUrl = "https://example.invalid/pod.jpg",
                SignatureUrl = "https://example.invalid/sig.png"
            });
            ok.StatusCode.Should().Be(HttpStatusCode.OK,
                "đủ bằng chứng thì ghi nhận được; body: {0}", await ok.Content.ReadAsStringAsync());
        }

        /// DEL-05 | BVA | FT-07 BV-02; AC-04; BR-039  ->  nhóm C
        /// Thu COD theo biên -1 / 0 / remaining / remaining+1: chưa có endpoint thu tiền.
        [Fact]
        public async Task L3_DEL_05_CodCollection_EndpointNotImplemented()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            (await sales.PostAsJsonAsync("/api/delivery/collections", new { Amount = -1 }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, "DEF-L3-004");
        }

        /// DEL-06 | Input-Domain-Error | FT-07 NAC-04; BR-016; BR-039  ->  nhóm C
        [Fact]
        public async Task L3_DEL_06_CodOnPaidOrder_EndpointNotImplemented()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            (await sales.PostAsJsonAsync("/api/delivery/collections", new { Amount = 1000 }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, "DEF-L3-004");
        }

        /// DEL-07 | BVA | FT-07 BV-03; AC-05; BR-040  ->  nhóm C
        /// Escalation sau 3 lần giao hỏng: chưa có endpoint ghi nhận lần giao.
        [Fact]
        public async Task L3_DEL_07_FailedAttempts_EscalateAtThirdAndBlockFourth()
        {
            var (sales, orderId, _) = await ArrangeOrderInTripAsync();

            async Task<HttpResponseMessage> FailOnceAsync() =>
                await sales.PostAsJsonAsync("/api/delivery/attempts", new
                {
                    OrderId = orderId, Outcome = "Failed", FailureReason = "Khach khong co nha"
                });

            // Lần 1-3: ghi nhận bình thường; lần 3 sinh cảnh báo cho Sales Manager (BR-040).
            for (int i = 1; i <= 3; i++)
            {
                var r = await FailOnceAsync();
                r.StatusCode.Should().Be(HttpStatusCode.OK,
                    "lần giao hỏng thứ {0} vẫn phải ghi nhận được; body: {1}",
                    i, await r.Content.ReadAsStringAsync());
            }

            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .FailedDeliveryCount.Should().Be(3);

            (await QueryAsync(db => db.Notifications.CountAsync(n =>
                n.Type == NotificationType.SYS_39_DeliveryTripAttemptEscalation)))
                .Should().BeGreaterThan(0, "AC-05: lần hỏng thứ 3 phải sinh escalation cho Sales Manager");

            // Lần 4: bị chặn khi chưa có quyết định của Sales Manager.
            var fourth = await FailOnceAsync();
            fourth.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "BR-040: sau 3 lần hỏng phải chờ quyết định, không được giao tiếp; body: {0}",
                await fourth.Content.ReadAsStringAsync());
            (await ReadJsonAsync(fourth)).GetProperty("code").GetString()
                .Should().Be("DELIVERY_ESCALATION_REQUIRED");
        }

        /// DEL-08 | Input-Domain-Happy | FT-07 AC-02; BR-034
        /// Bàn giao có xác nhận của CẢ HAI phía — kiểm hai endpoint dual-confirm đều tồn tại và
        /// được phân quyền tách bạch (đây là phần đã triển khai của BR-034).
        [Fact]
        public async Task L3_DEL_08_DualConfirmEndpoints_ExistAndSeparatelyAuthorized()
        {
            var handoverId = Guid.NewGuid();
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var res = await warehouse.PostAsJsonAsync(
                $"/api/handover-records/{handoverId}/warehouse-confirm", new { });

            res.StatusCode.Should().NotBe(HttpStatusCode.NotFound, "endpoint xác nhận phía kho phải tồn tại");
            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "đúng vai trò thì phải qua cổng phân quyền");
        }

        /// DEL-09 | Input-Domain-Error | FT-07 NAC-05; NFR-SEC03
        /// Sales S2 không được xem dữ liệu giao hàng của Sales S1.
        /// Workbook ghi GET /api/delivery/trips/{id}; endpoint thật là GET /api/delivery/orders.
        [Fact]
        public async Task L3_DEL_09_DeliveryScope_CustomerCannotReadDeliveryQueue()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customer.GetAsync("/api/delivery/orders"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, "khách hàng không được xem hàng đợi giao");

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            (await sales.GetAsync("/api/delivery/orders"))
                .StatusCode.Should().Be(HttpStatusCode.OK, "Sales Staff phải xem được");

            (await sales.GetAsync($"/api/delivery/trips/{Guid.NewGuid()}"))
                .StatusCode.Should().Be(HttpStatusCode.NotFound,
                    "DEF-L3-004: không có API xem chi tiết chuyến giao để kiểm phạm vi theo Sales");
        }
    }
}
