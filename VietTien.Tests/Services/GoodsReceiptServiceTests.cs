using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.PurchaseOrder;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: GoodsReceiptService — L1-GR-01..04. EF InMemory + mock INotificationService.</summary>
    public class GoodsReceiptServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly GoodsReceiptService _sut;
        private readonly User _whStaff;
        private readonly Warehouse _warehouse;
        private readonly WarehouseLocation _location;
        private readonly Product _p1;

        public GoodsReceiptServiceTests()
        {
            _sut = new GoodsReceiptService(_db, new Mock<INotificationService>().Object, new Mock<ICloudinaryService>().Object, new Mock<ILogger<GoodsReceiptService>>().Object, new NoOpAuditLogService());
            _whStaff = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            (_warehouse, _location) = TestData.Warehouse();
            _db.Users.Add(_whStaff);
            _db.Warehouses.Add(_warehouse);
            _p1 = TestData.SeedProduct(_db);
        }

        private PurchaseOrder SeedPo(PurchaseOrderStatus status, int expectedQty = 50)
        {
            var po = new PurchaseOrder
            {
                Code = $"PO-{Guid.NewGuid():N}"[..12],
                CreatedById = _whStaff.Id,
                SupplierId = Guid.NewGuid(),
                WarehouseId = _warehouse.Id,
                Status = status,
            };
            po.Items.Add(new PurchaseOrderItem { ProductId = _p1.Id, ExpectedQuantity = expectedQty, Unit = "Cái" });
            _db.PurchaseOrders.Add(po);
            _db.SaveChanges();
            return po;
        }

        // L1-GR-01 | EP-Valid | Tạo phiếu nhập từ PO SentToWarehouse -> receipt Draft tham chiếu PO, line khớp
        [Fact]
        public async Task L1_GR_01_CreateFromPO_SentToWarehouse_DraftCreated()
        {
            var po = SeedPo(PurchaseOrderStatus.SentToWarehouse);
            var poItem = po.Items.First();

            var dto = await _sut.CreateFromPOAsync(po.Id, _whStaff.Id, new CreateGoodsReceiptRequest
            {
                Items = new List<CreateGoodsReceiptItemRequest>
                {
                    new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = 50 }
                }
            });

            dto.PurchaseOrderId.Should().Be(po.Id);
            dto.Status.Should().Be("Draft");
            dto.Items.Should().ContainSingle(i => i.PurchaseOrderItemId == poItem.Id && i.AcceptedQuantity == 50);
        }

        // L1-GR-02 | State-Invalid | Tạo phiếu nhập từ PO chưa gửi kho (Draft) -> conflict, không tạo receipt
        [Fact]
        public async Task L1_GR_02_CreateFromPO_PoNotSent_Rejected()
        {
            var po = SeedPo(PurchaseOrderStatus.Draft);

            var act = () => _sut.CreateFromPOAsync(po.Id, _whStaff.Id, new CreateGoodsReceiptRequest());

            await act.Should().ThrowAsync<Exception>().WithMessage("Purchase Order is not ready to receive goods");
            _db.GoodsReceipts.Count().Should().Be(0);
        }

        // L1-GR-03 | EP-Valid | Post phiếu nhập -> tồn kho tăng đúng AcceptedQuantity, receipt Posted, có StockTransaction
        [Fact]
        public async Task L1_GR_03_PostReceipt_InventoryIncreasedAtomically()
        {
            var po = SeedPo(PurchaseOrderStatus.SentToWarehouse, expectedQty: 50);
            var poItem = po.Items.First();
            _db.Inventories.Add(TestData.Inventory(_p1.Id, _location.Id, 10)); // tồn ban đầu 10
            _db.SaveChanges();
            var receipt = await _sut.CreateFromPOAsync(po.Id, _whStaff.Id, new CreateGoodsReceiptRequest
            {
                Items = new List<CreateGoodsReceiptItemRequest> { new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = 50 } }
            });

            var dto = await _sut.PostReceiptAsync(receipt.Id, _whStaff.Id);

            dto.Status.Should().Be("Posted");
            _db.Inventories.Single(i => i.ProductId == _p1.Id).OnHandQuantity.Should().Be(60);
            _db.StockTransactions.Should().ContainSingle(t =>
                t.ProductId == _p1.Id && t.QuantityChange == 50 && t.TransactionType == TransactionType.GoodsReceipt);
            _db.PurchaseOrders.Single(p => p.Id == po.Id).Status.Should().Be(PurchaseOrderStatus.FullyReceived);
        }

        // L1-GR-04 | State-Invalid | Post phiếu đã Posted -> conflict, tồn kho KHÔNG tăng đôi
        [Fact]
        public async Task L1_GR_04_PostReceipt_AlreadyPosted_NoDoubleIncrement()
        {
            var po = SeedPo(PurchaseOrderStatus.SentToWarehouse, expectedQty: 50);
            var poItem = po.Items.First();
            _db.Inventories.Add(TestData.Inventory(_p1.Id, _location.Id, 10));
            _db.SaveChanges();
            var receipt = await _sut.CreateFromPOAsync(po.Id, _whStaff.Id, new CreateGoodsReceiptRequest
            {
                Items = new List<CreateGoodsReceiptItemRequest> { new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = 50 } }
            });
            await _sut.PostReceiptAsync(receipt.Id, _whStaff.Id); // lần 1: 10 -> 60

            var act = () => _sut.PostReceiptAsync(receipt.Id, _whStaff.Id); // lần 2

            await act.Should().ThrowAsync<Exception>().WithMessage("Goods Receipt is already posted or cancelled");
            _db.Inventories.Single(i => i.ProductId == _p1.Id).OnHandQuantity.Should().Be(60); // không cộng đôi
        }

        // L1-GR-04x | State-Valid | Sửa số lượng phiếu còn Draft -> lưu thật, Post sau đó ghi sổ theo số ĐÃ SỬA
        // BUGFIX: trước đây màn hình sửa chi tiết phiếu nhập chỉ đổi state React cục bộ, không có API nào
        // để lưu -> mọi chỉnh sửa biến mất khi tải lại trang, Post luôn ghi theo số liệu lúc TẠO phiếu.
        [Fact]
        public async Task L1_GR_04x_UpdateDraftReceipt_ThenPost_UsesUpdatedQuantities()
        {
            var po = SeedPo(PurchaseOrderStatus.SentToWarehouse, expectedQty: 50);
            var poItem = po.Items.First();
            _db.Inventories.Add(TestData.Inventory(_p1.Id, _location.Id, 10));
            _db.SaveChanges();
            var receipt = await _sut.CreateFromPOAsync(po.Id, _whStaff.Id, new CreateGoodsReceiptRequest
            {
                Items = new List<CreateGoodsReceiptItemRequest> { new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = 50 } }
            });

            // Nhân viên phát hiện đếm nhầm lúc tạo phiếu: sửa lại còn 40 đạt, 10 hỏng (Thừa/Thiếu tự tính).
            var updated = await _sut.UpdateDraftReceiptAsync(receipt.Id, _whStaff.Id, new UpdateGoodsReceiptRequest
            {
                Items = new List<UpdateGoodsReceiptItemRequest>
                {
                    new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = 40, DamagedQuantity = 10 }
                }
            });
            updated.Items.Single().AcceptedQuantity.Should().Be(40);
            updated.Items.Single().DamagedQuantity.Should().Be(10);
            updated.Items.Single().ExcessQuantity.Should().Be(0, "50 đạt+hỏng = đúng remaining 50, không thừa");
            updated.Items.Single().ShortQuantity.Should().Be(0);

            // Guard hiện có: bắt buộc có ảnh minh chứng khi có hàng Hỏng/Sai loại.
            var receiptEntity = await _db.GoodsReceipts.FindAsync(receipt.Id);
            receiptEntity!.ImageProofUrl = "https://cdn/proof.png";
            await _db.SaveChangesAsync();

            await _sut.PostReceiptAsync(receipt.Id, _whStaff.Id);

            var inv = _db.Inventories.Single(i => i.ProductId == _p1.Id);
            inv.OnHandQuantity.Should().Be(60, "Post phải ghi sổ theo số liệu ĐÃ SỬA (40+10), không phải số liệu lúc tạo phiếu (50+0)");
            inv.QuarantineQuantity.Should().Be(10);
        }

        // L1-GR-04y | State-Invalid | Sửa phiếu đã Posted -> conflict, không cho sửa sau khi đã ghi sổ
        [Fact]
        public async Task L1_GR_04y_UpdateDraftReceipt_AlreadyPosted_Rejected()
        {
            var po = SeedPo(PurchaseOrderStatus.SentToWarehouse, expectedQty: 50);
            var poItem = po.Items.First();
            _db.Inventories.Add(TestData.Inventory(_p1.Id, _location.Id, 10));
            _db.SaveChanges();
            var receipt = await _sut.CreateFromPOAsync(po.Id, _whStaff.Id, new CreateGoodsReceiptRequest
            {
                Items = new List<CreateGoodsReceiptItemRequest> { new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = 50 } }
            });
            await _sut.PostReceiptAsync(receipt.Id, _whStaff.Id);

            var act = () => _sut.UpdateDraftReceiptAsync(receipt.Id, _whStaff.Id, new UpdateGoodsReceiptRequest
            {
                Items = new List<UpdateGoodsReceiptItemRequest> { new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = 999 } }
            });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.Inventories.Single(i => i.ProductId == _p1.Id).OnHandQuantity.Should().Be(60, "không được sửa/ảnh hưởng tồn kho sau khi đã Posted");
        }

        // L1-GR-04b | State-Valid | Post phiếu có hàng hỏng/cách ly -> OnHand cộng ĐỦ (Accepted + hỏng),
        // không chỉ Accepted. BUGFIX: AvailableQuantity = OnHand - Reserved - Allocated - Damaged -
        // Quarantine coi Quarantine là một khoản GIỮ trừ bớt từ OnHand, không phải cộng thêm — khi QA
        // sau đó duyệt hàng cách ly là "còn dùng được" (DispatchQuarantine chỉ giảm QuarantineQuantity),
        // số lượng đó phải đã có sẵn trong OnHand từ lúc Post, nếu không sẽ biến mất khỏi hệ thống vĩnh
        // viễn dù vẫn nằm thật trên kệ. Trước đây chỉ cộng AcceptedQuantity, bỏ sót phần hỏng.
        [Fact]
        public async Task L1_GR_04b_PostReceipt_WithDamagedItems_CreditsFullPhysicalQuantityToOnHand()
        {
            var po = SeedPo(PurchaseOrderStatus.SentToWarehouse, expectedQty: 50);
            var poItem = po.Items.First();
            _db.Inventories.Add(TestData.Inventory(_p1.Id, _location.Id, 10)); // tồn ban đầu 10
            _db.SaveChanges();
            var receipt = await _sut.CreateFromPOAsync(po.Id, _whStaff.Id, new CreateGoodsReceiptRequest
            {
                Items = new List<CreateGoodsReceiptItemRequest>
                {
                    new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = 40, DamagedQuantity = 10 }
                }
            });
            // Guard hiện có: bắt buộc có ảnh minh chứng khi có hàng Hỏng/Sai loại.
            var receiptEntity = await _db.GoodsReceipts.FindAsync(receipt.Id);
            receiptEntity!.ImageProofUrl = "https://cdn/proof.png";
            await _db.SaveChangesAsync();

            var dto = await _sut.PostReceiptAsync(receipt.Id, _whStaff.Id);

            dto.Status.Should().Be("Posted");
            var inv = _db.Inventories.Single(i => i.ProductId == _p1.Id);
            inv.OnHandQuantity.Should().Be(60, "10 tồn cũ + 40 đạt + 10 hỏng vẫn nằm vật lý trong kho chờ QA");
            inv.QuarantineQuantity.Should().Be(10);
            _db.StockTransactions.Should().ContainSingle(t =>
                t.ProductId == _p1.Id && t.QuantityChange == 50 && t.TransactionType == TransactionType.GoodsReceipt,
                "vết audit phải khớp đúng tổng số lượng vật lý vừa nhận (40 đạt + 10 hỏng), không chỉ phần Accepted");
        }

        // ── Block: ⊕ v2.1 — Chứng từ & danh sách (UploadProofAsync, GetAllAsync) ──

        private async Task<GoodsReceiptDto> SeedDraftReceiptAsync(int acceptedQty = 50)
        {
            var po = SeedPo(PurchaseOrderStatus.SentToWarehouse, expectedQty: acceptedQty);
            var poItem = po.Items.First();
            return await _sut.CreateFromPOAsync(po.Id, _whStaff.Id, new CreateGoodsReceiptRequest
            {
                Items = new List<CreateGoodsReceiptItemRequest> { new() { PurchaseOrderItemId = poItem.Id, AcceptedQuantity = acceptedQty } }
            });
        }

        // L1-GR-05 | EP-Valid | Upload chứng từ nhận hàng -> gọi Cloudinary 1 lần và lưu URL trả về
        [Fact]
        public async Task L1_GR_05_UploadProof_DelegatesToCloudinaryAndStoresUrl()
        {
            var cloudinary = new Mock<ICloudinaryService>();
            var sut = new GoodsReceiptService(_db, new Mock<INotificationService>().Object, cloudinary.Object,
                new Mock<ILogger<GoodsReceiptService>>().Object, new NoOpAuditLogService());
            var receipt = await SeedDraftReceiptAsync();
            var file = new Mock<Microsoft.AspNetCore.Http.IFormFile>().Object;
            cloudinary.Setup(c => c.UploadEvidenceAsync(file, It.IsAny<string>())).ReturnsAsync("https://cdn/receipt-proof.png");

            var dto = await sut.UploadProofAsync(receipt.Id, file);

            cloudinary.Verify(c => c.UploadEvidenceAsync(file, It.IsAny<string>()), Times.Once);
            dto.ImageProofUrl.Should().Be("https://cdn/receipt-proof.png");
        }

        // L1-GR-06 | EP-Invalid | File ngoài allowlist / vượt kích thước -> chặn TRƯỚC khi gọi Cloudinary
        // Guard nằm ở ImageFileAttribute (tầng validation DTO) — xem thêm L1-EXT-06.
        [Theory]
        [InlineData("virus.exe", "application/x-msdownload", 1024)]
        [InlineData("huge.png", "image/png", 6 * 1024 * 1024)]
        public void L1_GR_06_UploadProof_DisallowedFile_IsRejectedBeforeUpload(string fileName, string contentType, long length)
        {
            var file = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
            file.SetupGet(f => f.FileName).Returns(fileName);
            file.SetupGet(f => f.ContentType).Returns(contentType);
            file.SetupGet(f => f.Length).Returns(length);

            var result = new API.Infrastructure.Validation.ImageFileAttribute()
                .GetValidationResult(file.Object, new System.ComponentModel.DataAnnotations.ValidationContext(new object()));

            result.Should().NotBe(System.ComponentModel.DataAnnotations.ValidationResult.Success);
        }

        // L1-GR-07 | EP-Valid | GetAll lọc theo trạng thái -> chỉ trả phiếu khớp
        [Fact]
        public async Task L1_GR_07_GetAll_FiltersByStatus()
        {
            var posted = new List<Guid>();
            for (var i = 0; i < 3; i++)
            {
                var r = await SeedDraftReceiptAsync();
                _db.Inventories.Add(TestData.Inventory(_p1.Id, _location.Id, 0));
                _db.SaveChanges();
                await _sut.PostReceiptAsync(r.Id, _whStaff.Id);
                posted.Add(r.Id);
            }
            await SeedDraftReceiptAsync();
            await SeedDraftReceiptAsync();

            var result = (await _sut.GetAllAsync("Posted")).ToList();

            result.Should().HaveCount(3);
            result.Select(r => r.Id).Should().BeEquivalentTo(posted);
            result.Should().OnlyContain(r => r.Status == "Posted");
        }
    }
}
