using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: GoodsIssueService — L1-GI-01..04. EF InMemory + mock ICloudinaryService.</summary>
    public class GoodsIssueServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<ICloudinaryService> _cloudinary = new();
        private readonly GoodsIssueService _sut;
        private readonly User _staff;
        private readonly Warehouse _warehouse;
        private readonly WarehouseLocation _location;

        public GoodsIssueServiceTests()
        {
            _sut = new GoodsIssueService(_db, _cloudinary.Object, TestWarehouseAccessGuard.Create(_db), new NoOpAuditLogService());
            (_warehouse, _location) = TestData.Warehouse();
            // Phải gán kho cho staff: phiếu xuất kho nay bị chặn theo AssignedWarehouseId (SRS NAC-05).
            _staff = TestData.User(u =>
            {
                u.Role = SystemRole.WarehouseStaff;
                u.AssignedWarehouseId = _warehouse.Id;
            });
            _db.Users.Add(_staff);
            _db.Warehouses.Add(_warehouse);
            _db.SaveChanges();
        }

        // L1-GI-01 | EP-Valid | Tạo phiếu xuất draft với 2 dòng -> lưu status Draft + 2 GoodsIssueItem
        [Fact]
        public async Task L1_GI_01_Create_DraftWithTwoLines()
        {
            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);

            var dto = await _sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other", // không phải ProductionMaterial -> Draft
                WarehouseId = _warehouse.Id,
                Items = new List<CreateGoodsIssueItemRequestDto>
                {
                    new() { ProductId = p1.Id, Quantity = 3 },
                    new() { ProductId = p2.Id, Quantity = 2 },
                }
            }, _staff.Id);

            dto.Status.Should().Be("Draft");
            dto.Items.Should().HaveCount(2);
            _db.GoodsIssueItems.Count().Should().Be(2);
        }

        // L1-GI-02 | EP-Valid | Upload bằng chứng -> Cloudinary gọi 1 lần, ProofUrl lưu, status ProofUploaded
        [Fact]
        public async Task L1_GI_02_UploadProof_DelegatesToCloudinary()
        {
            var p1 = TestData.SeedProduct(_db);
            var issue = await _sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "ProductionMaterial", // -> ProofPending
                WarehouseId = _warehouse.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = p1.Id, Quantity = 1 } }
            }, _staff.Id);
            var file = new Mock<IFormFile>().Object;
            _cloudinary.Setup(c => c.UploadEvidenceAsync(file, "GoodsIssues")).ReturnsAsync("https://cdn/proof.png");

            var dto = await _sut.UploadProofAsync(issue.Id, _staff.Id, file);

            _cloudinary.Verify(c => c.UploadEvidenceAsync(file, "GoodsIssues"), Times.Once);
            dto.ImageProofUrl.Should().Be("https://cdn/proof.png");
            dto.Status.Should().Be("ProofUploaded");
        }

        // L1-GI-03 | State-Valid | Post phiếu xuất -> tồn kho trừ đúng số lượng, status Posted, có StockTransaction
        [Fact]
        public async Task L1_GI_03_Post_StockDecrementedAtomically()
        {
            var p1 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, _location.Id, 10));
            _db.SaveChanges();
            var issue = await _sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = _warehouse.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = p1.Id, Quantity = 3 } }
            }, _staff.Id);

            var dto = await _sut.PostGoodsIssueAsync(issue.Id, _staff.Id);

            dto.Status.Should().Be("Posted");
            _db.Inventories.Single(i => i.ProductId == p1.Id).OnHandQuantity.Should().Be(7);
            _db.StockTransactions.Should().ContainSingle(t =>
                t.ProductId == p1.Id && t.QuantityChange == -3 && t.TransactionType == TransactionType.GoodsIssue);
        }

        // L1-GI-04 | State-Invalid | Post phiếu đã Posted -> conflict, tồn kho KHÔNG bị trừ đôi
        [Fact]
        public async Task L1_GI_04_Post_AlreadyPosted_NoDoubleDecrement()
        {
            var p1 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, _location.Id, 10));
            _db.SaveChanges();
            var issue = await _sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = _warehouse.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = p1.Id, Quantity = 3 } }
            }, _staff.Id);
            await _sut.PostGoodsIssueAsync(issue.Id, _staff.Id); // 10 -> 7

            var act = () => _sut.PostGoodsIssueAsync(issue.Id, _staff.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Chứng từ đã được Post hoặc bị Hủy trước đó, không thể thao tác lại.");
            _db.Inventories.Single(i => i.ProductId == p1.Id).OnHandQuantity.Should().Be(7);
        }

        // ── Block: ⊕ v2.1 — Bàn giao & phiếu đảo (UpdateHandoverInfoAsync, CreateReversalAsync) ──

        /// <summary>Tạo phiếu xuất 1 dòng cho sản phẩm p với tồn kho ban đầu onHand.</summary>
        private async Task<(GoodsIssueDto issue, Product product)> SeedIssueAsync(int quantity = 3, int onHand = 10)
        {
            var product = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(product.Id, _location.Id, onHand));
            _db.SaveChanges();

            var issue = await _sut.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
            {
                Type = "Other",
                WarehouseId = _warehouse.Id,
                Items = new List<CreateGoodsIssueItemRequestDto> { new() { ProductId = product.Id, Quantity = quantity } }
            }, _staff.Id);
            return (issue, product);
        }

        private static UpdateGoodsIssueHandoverDto HandoverDto(string paperDoc) => new()
        {
            ExternalRecipientName = "Trần Văn B",
            Department = "Phòng sản xuất",
            ReceivedAt = DateTime.UtcNow,
            PaperDocumentNumber = paperDoc,
            UsagePurpose = "Cấp vật tư cho dây chuyền 1"
        };

        private int OnHand(Guid productId)
        {
            _db.ChangeTracker.Clear();
            return _db.Inventories.Single(i => i.ProductId == productId).OnHandQuantity;
        }

        // L1-GI-05 | EP-Valid | Cập nhật thông tin bàn giao trên phiếu CHƯA post -> lưu được, phiếu vẫn Draft
        [Fact]
        public async Task L1_GI_05_UpdateHandover_OnDraft_IsStored()
        {
            var (issue, _) = await SeedIssueAsync();

            var dto = await _sut.UpdateHandoverInfoAsync(issue.Id, _staff.Id, HandoverDto("BB-001"));

            dto.Status.Should().Be("Draft", "cập nhật bàn giao không làm đổi trạng thái chứng từ");
            _db.ChangeTracker.Clear();
            var saved = _db.GoodsIssues.Single(g => g.Id == issue.Id);
            saved.ExternalRecipientName.Should().Be("Trần Văn B");
            saved.PaperDocumentNumber.Should().Be("BB-001");
        }

        // L1-GI-06 | State-Invalid | Cập nhật bàn giao trên phiếu ĐÃ post -> conflict (chứng từ bất biến)
        [Fact]
        public async Task L1_GI_06_UpdateHandover_OnPosted_IsConflict()
        {
            var (issue, _) = await SeedIssueAsync();
            await _sut.UpdateHandoverInfoAsync(issue.Id, _staff.Id, HandoverDto("BB-002"));
            await _sut.PostGoodsIssueAsync(issue.Id, _staff.Id);

            var act = () => _sut.UpdateHandoverInfoAsync(issue.Id, _staff.Id, HandoverDto("BB-002-SUA"));

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.ChangeTracker.Clear();
            _db.GoodsIssues.Single(g => g.Id == issue.Id).PaperDocumentNumber.Should().Be("BB-002", "dữ liệu không đổi");
        }

        // L1-GI-07 | EP-Valid | Tạo phiếu đảo -> hoàn đúng số lượng đã xuất, phiếu GỐC giữ nguyên
        [Fact]
        public async Task L1_GI_07_CreateReversal_RestoresStockAndKeepsOriginal()
        {
            var (issue, product) = await SeedIssueAsync(quantity: 3, onHand: 10);
            await _sut.PostGoodsIssueAsync(issue.Id, _staff.Id);
            OnHand(product.Id).Should().Be(7);

            var reversal = await _sut.CreateReversalAsync(issue.Id,
                new CreateReversalRequestDto { ReversalReason = "Xuất nhầm phòng ban" }, _staff.Id);

            OnHand(product.Id).Should().Be(10, "tồn kho được hoàn lại đúng số đã xuất");
            reversal.Id.Should().NotBe(issue.Id, "phiếu đảo là chứng từ MỚI");
            _db.GoodsIssues.Should().HaveCount(2);
        }

        // L1-GI-08 | State-Invalid | Tạo phiếu đảo cho phiếu CHƯA post -> từ chối
        [Fact]
        public async Task L1_GI_08_CreateReversal_OnUnpostedIssue_IsRejected()
        {
            var (issue, product) = await SeedIssueAsync(quantity: 3, onHand: 10);

            var act = () => _sut.CreateReversalAsync(issue.Id,
                new CreateReversalRequestDto { ReversalReason = "Thử đảo phiếu nháp" }, _staff.Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.GoodsIssues.Should().ContainSingle("không tạo chứng từ đảo");
            OnHand(product.Id).Should().Be(10);
        }

        // L1-GI-09 | Idempotency | Đảo lần 2 cho cùng phiếu gốc -> chặn hoàn kép
        [Fact]
        public async Task L1_GI_09_CreateReversal_Twice_IsBlocked()
        {
            var (issue, product) = await SeedIssueAsync(quantity: 3, onHand: 10);
            await _sut.PostGoodsIssueAsync(issue.Id, _staff.Id);
            await _sut.CreateReversalAsync(issue.Id, new CreateReversalRequestDto { ReversalReason = "Xuất nhầm" }, _staff.Id);
            OnHand(product.Id).Should().Be(10);

            var act = () => _sut.CreateReversalAsync(issue.Id, new CreateReversalRequestDto { ReversalReason = "Đảo lần 2" }, _staff.Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
            OnHand(product.Id).Should().Be(10, "không được cộng tồn thêm lần nữa");
        }
    }
}
