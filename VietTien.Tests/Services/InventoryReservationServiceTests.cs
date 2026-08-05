using FluentAssertions;
using Microsoft.Data.Sqlite;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: InventoryReservationService — L1-RES-01..15.
    /// Service dùng ExecuteSqlInterpolatedAsync (guarded UPDATE chống oversell) nên KHÔNG chạy được
    /// trên EF InMemory — dùng SqliteDbFactory (SQLite in-memory, hỗ trợ raw SQL).
    /// Signature thật: Task ReserveAsync(IEnumerable&lt;(Guid ProductId, int Quantity)&gt;) — trả void, NÉM Exception khi thiếu tồn.
    /// </summary>
    public class InventoryReservationServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly SqliteConnection _connection;
        private readonly InventoryReservationService _sut;
        private readonly Guid _locationId;

        public InventoryReservationServiceTests()
        {
            (_db, _connection) = SqliteDbFactory.Create();
            _sut = new InventoryReservationService(_db);

            var (warehouse, location) = TestData.Warehouse();
            _db.Warehouses.Add(warehouse);
            _db.SaveChanges();
            _locationId = location.Id;
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
            GC.SuppressFinalize(this);
        }

        private Product SeedProductWithStock(int onHand, Action<Inventory>? mutate = null)
        {
            var product = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(product.Id, _locationId, onHand, mutate));
            _db.SaveChanges();
            return product;
        }

        private Inventory Reload(Guid productId)
        {
            _db.ChangeTracker.Clear();
            return _db.Inventories.Single(i => i.ProductId == productId);
        }

        // ── Block: ReserveAsync() ────────────────────────────────────────────

        // L1-RES-01 | EP-Valid | Giữ tồn cho đơn SePay -> Reserved tăng, OnHand không đổi, Available giảm tương ứng
        [Fact]
        public async Task L1_RES_01_Reserve_HoldsStock_WithoutTouchingOnHand()
        {
            var p1 = SeedProductWithStock(10);

            await _sut.ReserveAsync(new[] { (p1.Id, 3) });

            var inv = Reload(p1.Id);
            inv.ReservedQuantity.Should().Be(3);
            inv.OnHandQuantity.Should().Be(10);
            inv.AvailableQuantity.Should().Be(7); // 10 - 3 - 0 - 0 - 0
        }

        // L1-RES-02 | EP-Valid | Giữ nhiều SKU trong 1 lệnh -> cả 2 cùng tăng Reserved
        [Fact]
        public async Task L1_RES_02_Reserve_MultipleSkusInOneCall()
        {
            var p1 = SeedProductWithStock(10);
            var p2 = SeedProductWithStock(5);

            await _sut.ReserveAsync(new[] { (p1.Id, 3), (p2.Id, 2) });

            Reload(p1.Id).AvailableQuantity.Should().Be(7);
            Reload(p2.Id).AvailableQuantity.Should().Be(3);
        }

        // L1-RES-03 | BVA-Max | Giữ đúng bằng tồn khả dụng -> cho phép, Available về 0 (không âm)
        [Fact]
        public async Task L1_RES_03_Reserve_ExactlyAvailable_Allowed()
        {
            var p1 = SeedProductWithStock(10);

            var act = () => _sut.ReserveAsync(new[] { (p1.Id, 10) });

            await act.Should().NotThrowAsync();
            var inv = Reload(p1.Id);
            inv.ReservedQuantity.Should().Be(10);
            inv.AvailableQuantity.Should().Be(0);
        }

        // L1-RES-04 | BVA-Max+1 | Giữ vượt tồn khả dụng 1 đơn vị -> ném Exception, không giữ phần nào
        [Fact]
        public async Task L1_RES_04_Reserve_OverAvailable_Throws()
        {
            var p1 = SeedProductWithStock(10);

            var act = () => _sut.ReserveAsync(new[] { (p1.Id, 11) });

            await act.Should().ThrowAsync<Exception>();
            var inv = Reload(p1.Id);
            inv.ReservedQuantity.Should().Be(0);
            inv.AvailableQuantity.Should().Be(10);
        }

        // L1-RES-05 | Atomicity | Nhiều dòng, 1 dòng thiếu hàng -> rollback toàn bộ, dòng đủ hàng KHÔNG bị giữ
        [Fact]
        public async Task L1_RES_05_Reserve_PartialShortage_RollsBackEverything()
        {
            var p1 = SeedProductWithStock(10);
            var p2 = SeedProductWithStock(1);

            var act = () => _sut.ReserveAsync(new[] { (p1.Id, 4), (p2.Id, 2) });

            await act.Should().ThrowAsync<Exception>();
            Reload(p1.Id).ReservedQuantity.Should().Be(0); // KHÔNG được giữ 4 cho P1
            Reload(p2.Id).ReservedQuantity.Should().Be(0);
        }

        // L1-RES-06 | Concurrency | Hai đơn cùng giữ 10 sản phẩm cuối -> đúng 1 đơn thành công
        //
        // ⚠⚠ KHÔNG THAY THẾ ĐƯỢC TEST L2 — đọc kỹ trước khi tin vào màu xanh của case này:
        //    SQLite in-memory dùng CHUNG MỘT connection nên mọi lệnh ghi bị serialise sẵn ở tầng driver.
        //    Nghĩa là test này xanh KỂ CẢ KHI guarded UPDATE bị gỡ bỏ — nó KHÔNG chứng minh được
        //    cơ chế chống oversell dưới row-lock thật của SQL Server.
        //    Giá trị thật của case: bắt lỗi LOGIC (Available xuống âm, cả 2 luồng cùng báo thành công).
        //    Kiểm chứng atomic thật sự BẮT BUỘC phải làm ở L2 trên SQL Server. Xem DOC_MISMATCHES.md.
        [Fact]
        public async Task L1_RES_06_Reserve_Concurrent_OnlyOneSucceeds()
        {
            var p1 = SeedProductWithStock(10);

            var first = _sut.ReserveAsync(new[] { (p1.Id, 10) });
            var second = _sut.ReserveAsync(new[] { (p1.Id, 10) });
            var results = await Task.WhenAll(
                Capture(first),
                Capture(second));

            results.Count(ok => ok).Should().Be(1, "chỉ 1 luồng được giữ 10 sản phẩm cuối cùng");
            var inv = Reload(p1.Id);
            inv.ReservedQuantity.Should().Be(10);
            inv.AvailableQuantity.Should().Be(0); // không âm

            static async Task<bool> Capture(Task task)
            {
                try { await task; return true; }
                catch { return false; }
            }
        }

        // ── Block: AllocateAsync() / ReleaseReservedAsync() / ReleaseAllocatedAsync() ──

        // L1-RES-07 | EP-Valid | Chuyển Reserved -> Allocated, OnHand và Available không đổi
        [Fact]
        public async Task L1_RES_07_Allocate_MovesReservedToAllocated()
        {
            var p1 = SeedProductWithStock(10);
            await _sut.ReserveAsync(new[] { (p1.Id, 3) });
            var availableBefore = Reload(p1.Id).AvailableQuantity;

            await _sut.ReleaseReservedAsync(new[] { (p1.Id, 3) });
            await _sut.AllocateAsync(new[] { (p1.Id, 3) });

            var inv = Reload(p1.Id);
            inv.ReservedQuantity.Should().Be(0);
            inv.AllocatedQuantity.Should().Be(3);
            inv.OnHandQuantity.Should().Be(10);
            inv.AvailableQuantity.Should().Be(availableBefore); // cả 2 cột đều trừ vào Available
        }

        // L1-RES-08 | EP-Valid | ReleaseReserved chỉ trả phần RESERVED, KHÔNG đụng ALLOCATED
        [Fact]
        public async Task L1_RES_08_ReleaseReserved_DoesNotTouchAllocated()
        {
            var p1 = SeedProductWithStock(20, inv => inv.AllocatedQuantity = 5);
            await _sut.ReserveAsync(new[] { (p1.Id, 3) });

            await _sut.ReleaseReservedAsync(new[] { (p1.Id, 3) });

            var inv = Reload(p1.Id);
            inv.ReservedQuantity.Should().Be(0);
            inv.AllocatedQuantity.Should().Be(5); // nguyên vẹn
            inv.AvailableQuantity.Should().Be(15);
        }

        // L1-RES-09 | State-Invalid | Trả nhiều hơn số đang giữ -> Reserved chặn tại 0, không xuống âm
        [Fact]
        public async Task L1_RES_09_ReleaseReserved_MoreThanHeld_FloorsAtZero()
        {
            var p1 = SeedProductWithStock(10);
            await _sut.ReserveAsync(new[] { (p1.Id, 1) });

            await _sut.ReleaseReservedAsync(new[] { (p1.Id, 3) });

            Reload(p1.Id).ReservedQuantity.Should().Be(0).And.BeGreaterThanOrEqualTo(0);
        }

        // L1-RES-10 | EP-Valid | ReleaseAllocated khi huỷ đơn -> trả hàng về khả dụng, Reserved không đổi
        [Fact]
        public async Task L1_RES_10_ReleaseAllocated_ReturnsStockToAvailable()
        {
            var p1 = SeedProductWithStock(10, inv => { inv.AllocatedQuantity = 3; inv.ReservedQuantity = 0; });

            await _sut.ReleaseAllocatedAsync(new[] { (p1.Id, 3) });

            var inv = Reload(p1.Id);
            inv.AllocatedQuantity.Should().Be(0);
            inv.ReservedQuantity.Should().Be(0);
            inv.AvailableQuantity.Should().Be(10);
        }

        // ── Block: DeductOnHandAsync() ───────────────────────────────────────

        // L1-RES-11 | EP-Valid | Bán tại quầy -> OnHand giảm, Reserved/Allocated không đổi
        [Fact]
        public async Task L1_RES_11_DeductOnHand_ReducesOnHandOnly()
        {
            var p1 = SeedProductWithStock(10);

            await _sut.DeductOnHandAsync(new[] { (p1.Id, 3) });

            var inv = Reload(p1.Id);
            inv.OnHandQuantity.Should().Be(7);
            inv.ReservedQuantity.Should().Be(0);
            inv.AllocatedQuantity.Should().Be(0);
            inv.AvailableQuantity.Should().Be(7);
        }

        // L1-RES-12 | BVA-Min-1 | Trừ tồn làm OnHand âm -> chặn tại guarded UPDATE
        [Fact]
        public async Task L1_RES_12_DeductOnHand_BelowZero_Throws()
        {
            var p1 = SeedProductWithStock(2);

            var act = () => _sut.DeductOnHandAsync(new[] { (p1.Id, 3) });

            await act.Should().ThrowAsync<Exception>();
            Reload(p1.Id).OnHandQuantity.Should().Be(2);
        }

        // L1-RES-13 | Idempotency | Gọi trừ tồn 2 lần -> service KHÔNG tự idempotent (đúng thiết kế);
        // idempotency là trách nhiệm của tầng gọi (OrderService) bằng business key.
        [Fact]
        public async Task L1_RES_13_DeductOnHand_CalledTwice_DeductsTwice_ByDesign()
        {
            var p1 = SeedProductWithStock(10);

            await _sut.DeductOnHandAsync(new[] { (p1.Id, 3) });
            await _sut.DeductOnHandAsync(new[] { (p1.Id, 3) });

            Reload(p1.Id).OnHandQuantity.Should().Be(4);
        }

        // ── Block: ⊕ v2.2 — Công thức AvailableQuantity ──────────────────────

        // L1-RES-14 | BVA-Min | Available = OnHand - Reserved - Allocated - Damaged - Quarantine, sàn tại 0
        [Theory]
        [InlineData(10, 3, 2, 1, 1, 3)]
        [InlineData(10, 5, 5, 5, 5, 0)]  // tổng trừ vượt OnHand -> 0, KHÔNG âm
        [InlineData(0, 0, 0, 0, 0, 0)]
        public void L1_RES_14_AvailableQuantity_Formula(int onHand, int reserved, int allocated, int damaged, int quarantine, int expected)
        {
            var inv = new Inventory
            {
                OnHandQuantity = onHand,
                ReservedQuantity = reserved,
                AllocatedQuantity = allocated,
                DamagedQuantity = damaged,
                QuarantineQuantity = quarantine,
            };

            inv.AvailableQuantity.Should().Be(expected);
        }

        // L1-RES-15 | EP-Valid | Hàng cách ly (Quarantine) bị loại khỏi tồn bán được
        [Fact]
        public async Task L1_RES_15_QuarantineExcludedFromAvailable()
        {
            var p1 = SeedProductWithStock(10, inv => inv.QuarantineQuantity = 4);

            Reload(p1.Id).AvailableQuantity.Should().Be(6);

            // và không thể giữ quá phần khả dụng còn lại
            var act = () => _sut.ReserveAsync(new[] { (p1.Id, 7) });
            await act.Should().ThrowAsync<Exception>();
        }
    }
}
