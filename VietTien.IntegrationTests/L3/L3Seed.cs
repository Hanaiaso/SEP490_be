namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Hằng số trỏ tới dữ liệu ĐÃ ĐƯỢC SEED SẴN bởi <c>ApplicationDbContext.OnModelCreating</c>
    /// (HasData) — host L3 gọi <c>EnsureCreated()</c> nên toàn bộ seed này có mặt trong EF InMemory.
    ///
    /// Dùng hằng số ở đây thay vì tự tạo lại dữ liệu: nhiều service hard-code theo mã kho
    /// ("WH-DEFAULT" trong OrderService) hoặc so khớp <c>AssignedWarehouseId</c> của nhân viên kho,
    /// nên dữ liệu tự chế sẽ bị các service đó từ chối.
    ///
    /// Mật khẩu của MỌI tài khoản seed là <c>123456</c> (ApplicationDbContext.cs:907).
    /// </summary>
    internal static class L3Seed
    {
        public const string DefaultPassword = "123456";

        // ── Tài khoản theo vai trò ────────────────────────────────────────────────────────────
        public static readonly Guid AdminId          = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid CeoId            = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly Guid SalesManagerId   = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid SalesStaffId     = Guid.Parse("44444444-4444-4444-4444-444444444444");
        public static readonly Guid SalesStaff2Id    = Guid.Parse("44444444-4444-4444-4444-444444444402");
        public static readonly Guid WarehouseStaffId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        /// <summary>Nhân viên kho được gán WH-TRADE — dùng cho case "thao tác ngoài phạm vi kho".</summary>
        public static readonly Guid WarehouseStaff2Id = Guid.Parse("55555555-5555-5555-5555-555555555502");
        public static readonly Guid CustomerId       = Guid.Parse("77777777-7777-7777-7777-777777777777");

        public const string AdminEmail          = "admin.test@viettien.com";
        public const string CeoEmail            = "ceo.test@viettien.com";
        public const string SalesManagerEmail   = "salesmanager.test@viettien.com";
        public const string SalesStaffEmail     = "salesstaff.test@viettien.com";
        public const string WarehouseStaffEmail = "warehousestaff.test@viettien.com";
        public const string CustomerEmail       = "customer.test@viettien.com";

        // ── Kho & vị trí ──────────────────────────────────────────────────────────────────────
        /// <summary>WH-DEFAULT — OrderService hard-code theo mã kho này, KHÔNG được thay bằng kho khác.</summary>
        public static readonly Guid WarehouseDefaultId  = Guid.Parse("ee73f2cc-05fd-4b0e-8a48-61f89a2d345a");
        public static readonly Guid LocationDefaultId   = Guid.Parse("2006d0a6-37a9-46ca-b8a0-bb061ec9f1e9");
        public static readonly Guid WarehouseTradeId    = Guid.Parse("f0000003-0003-4003-a003-000000000001");
        public static readonly Guid LocationTradeId     = Guid.Parse("f0000003-0003-4003-a003-000000000002");
        public static readonly Guid WarehousePeId       = Guid.Parse("f0000004-0004-4004-a004-000000000001");
        public static readonly Guid LocationPeId        = Guid.Parse("f0000004-0004-4004-a004-000000000002");

        // ── Sản phẩm (giá niêm yết) ───────────────────────────────────────────────────────────
        public static readonly Guid ProductTapeTrongId = Guid.Parse("659870d7-5b15-4496-a4bb-03ab28900170"); // 65.000
        public static readonly Guid ProductPeWrapId    = Guid.Parse("e24b1960-21d2-4385-8155-17557c0ce8b9"); // 120.000
        public static readonly Guid ProductBubbleId    = Guid.Parse("a3c3e6e5-860a-464c-a073-1b847a9db570"); // 250.000
        public static readonly Guid ProductCartonId    = Guid.Parse("cc25fd5c-3ad6-4f95-b19f-e86635d1d16d"); // 3.500
        public static readonly Guid ProductCutToolId   = Guid.Parse("aa275908-173a-47fb-a2cb-8eb173c934ef"); // 25.000

        public const decimal PriceTapeTrong = 65_000m;
        public const decimal PricePeWrap    = 120_000m;
        public const decimal PriceBubble    = 250_000m;
        public const decimal PriceCarton    = 3_500m;

        /// <summary>Tồn kho WH-DEFAULT của màng PE — dòng "sạch" nhất (Reserved = 0, Quarantine = 0).</summary>
        public static readonly Guid InventoryPeWrapDefaultId = Guid.Parse("b115bc37-ab72-40e4-b1fa-274d7b329efe");

        // ── Ngưỡng cấu hình (BR-006 / BR-026) ─────────────────────────────────────────────────
        /// <summary>LIST_PRICE_MAX_EXCLUSIVE — dưới ngưỡng này dùng giá niêm yết, không chiết khấu.</summary>
        public const decimal ListPriceMaxExclusive = 10_000_000m;
        /// <summary>QUOTATION_MIN_VALUE — từ ngưỡng này bắt buộc đi luồng báo giá.</summary>
        public const decimal QuotationMinValue = 100_000_000m;
    }
}
