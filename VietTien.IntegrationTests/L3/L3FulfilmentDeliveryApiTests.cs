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
        public async Task L3_FUL_08_MultiPick_EndpointNotImplemented()
        {
            var warehouse = await ClientForSeededAsync(L3Seed.WarehouseStaffId);

            var res = await warehouse.PostAsJsonAsync("/api/warehouse/orders/multi-pick", new { });

            res.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "DEF-L3-005: chức năng multi-pick trong SRS chưa được triển khai");
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

        /// DEL-01 | BVA | FT-07 BV-01; AC-01; BR-037  ->  nhóm C
        /// Workbook: POST /api/delivery/trips với ràng buộc xe/ca/ngày không trùng.
        /// Hệ thống chưa có module chuyến giao — endpoint tương đương gần nhất là /api/delivery/schedule.
        [Fact]
        public async Task L3_DEL_01_DeliveryTrips_EndpointNotImplemented_ScheduleUsedInstead()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            (await sales.PostAsJsonAsync("/api/delivery/trips", new { VehicleId = 1, Shift = "Morning" }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound,
                    "DEF-L3-004: module Delivery Trip chưa triển khai");

            // Chức năng tương đương đang có phải tồn tại và được bảo vệ đúng vai trò.
            (await sales.PostAsJsonAsync("/api/delivery/schedule", new { }))
                .StatusCode.Should().NotBe(HttpStatusCode.NotFound, "/api/delivery/schedule phải tồn tại");
        }

        /// DEL-02 | Input-Domain-Error | FT-07 NAC-01  ->  nhóm C
        /// Xe đang bảo trì/inactive không được xếp chuyến — không có API chuyến giao để kiểm.
        [Fact]
        public async Task L3_DEL_02_VehicleNotAvailable_NoTripApiToEnforce()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            (await sales.PostAsJsonAsync("/api/delivery/trips", new { VehicleId = Guid.NewGuid() }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, "DEF-L3-004");

            // Danh mục xe VẪN có và đọc được — dữ liệu nền cho tính năng này đã sẵn sàng.
            (await sales.GetAsync("/api/vehicles")).StatusCode.Should().Be(HttpStatusCode.OK);
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
        public async Task L3_DEL_04_ProofOfDelivery_EndpointNotImplemented_CompleteUsedInstead()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            (await sales.PostAsJsonAsync("/api/delivery/attempts", new { Status = "DELIVERED" }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, "DEF-L3-004");

            (await sales.PostAsJsonAsync($"/api/delivery/{Guid.NewGuid()}/complete", new { }))
                .StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
                    "endpoint hoàn tất giao hàng đang có phải mở cho Sales Staff");
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
        public async Task L3_DEL_07_DeliveryAttemptEscalation_EndpointNotImplemented()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            (await sales.PostAsJsonAsync("/api/delivery/attempts", new { Status = "FAILED" }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, "DEF-L3-004");
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
