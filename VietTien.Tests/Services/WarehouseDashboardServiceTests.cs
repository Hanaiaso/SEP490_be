using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// WarehouseDashboardService.GetDashboardAsync — chỉ phủ phần badge sidebar mới thêm
    /// (Outbound.PendingHandover); các KPI khác đã được test gián tiếp qua các service nguồn của
    /// chúng (WarehouseServiceTests, InventoryService...).
    /// </summary>
    public class WarehouseDashboardServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly InventoryService _inventoryService;
        private readonly Mock<ISystemConfigService> _sysConfig = new();
        private readonly WarehouseDashboardService _sut;
        private readonly CustomerProfile _profile;

        public WarehouseDashboardServiceTests()
        {
            _inventoryService = new InventoryService(_db, MockHubContext.Create<WarehouseHub>().Object, new Mock<ILogger<InventoryService>>().Object, TestWarehouseAccessGuard.Create(_db));
            _sut = new WarehouseDashboardService(_db, _inventoryService, _sysConfig.Object);
            (_, _profile) = TestData.SeedCustomer(_db);
        }

        private void SeedOrder(FulfillmentStatus status)
        {
            var order = TestData.Order(_profile.Id, o => o.FulfillmentStatus = status);
            _db.Orders.Add(order);
            _db.SaveChanges();
        }

        // DASH-WH-01 | EP-Valid | Outbound.PendingHandover đếm đúng đơn Consolidated (chờ 1 trong 2 chữ ký), bỏ qua trạng thái khác
        [Fact]
        public async Task DASH_WH_01_PendingHandover_CountsOnlyConsolidatedOrders()
        {
            SeedOrder(FulfillmentStatus.Consolidated);
            SeedOrder(FulfillmentStatus.Consolidated);
            SeedOrder(FulfillmentStatus.Ready);
            SeedOrder(FulfillmentStatus.Fulfilled);

            var dashboard = await _sut.GetDashboardAsync();

            dashboard.Outbound.PendingHandover.Should().Be(2, "khớp đúng điều kiện tabType=\"Handover\" của GetOrdersForWarehouseAsync");
        }
    }
}
