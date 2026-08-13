using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        // Đăng ký toàn bộ các thuộc tính DbSet tương ứng với các bảng
        public DbSet<User> Users => Set<User>();
        public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
        public DbSet<Address> Addresses => Set<Address>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductMaterial> ProductMaterials => Set<ProductMaterial>();
        public DbSet<Cart> Carts => Set<Cart>();
        public DbSet<CartItem> CartItems => Set<CartItem>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
        public DbSet<PaymentException> PaymentExceptions => Set<PaymentException>();
        public DbSet<Inventory> Inventories => Set<Inventory>();
        public DbSet<Material> Materials => Set<Material>();
        public DbSet<ReturnedGoodsLog> ReturnedGoodsLogs => Set<ReturnedGoodsLog>();
        public DbSet<CustomerDebt> CustomerDebts => Set<CustomerDebt>();
        public DbSet<EmployeeSalary> EmployeeSalaries => Set<EmployeeSalary>();
        public DbSet<MonthlyPayroll> MonthlyPayrolls => Set<MonthlyPayroll>();
        public DbSet<PayrollDetail> PayrollDetails => Set<PayrollDetail>();
        public DbSet<AiMarketingCampaign> AiMarketingCampaigns => Set<AiMarketingCampaign>();
        public DbSet<MarketingPost> MarketingPosts => Set<MarketingPost>();

        // Quotation / Negotiation
        public DbSet<Quotation> Quotations => Set<Quotation>();
        public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
        public DbSet<QuotationVersion> QuotationVersions => Set<QuotationVersion>();
        public DbSet<QuotationVersionItem> QuotationVersionItems => Set<QuotationVersionItem>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

        // System Notifications
        public DbSet<Notification> Notifications => Set<Notification>();

        // Warehousing & PO & Replacement
        public DbSet<Warehouse> Warehouses => Set<Warehouse>();
        public DbSet<WarehouseLocation> WarehouseLocations => Set<WarehouseLocation>();
        public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
        public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
        public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();
        public DbSet<PaymentReallocation> PaymentReallocations => Set<PaymentReallocation>();

        // Advanced Warehouse Modules (v6.0)
        public DbSet<PickTask> PickTasks => Set<PickTask>();
        public DbSet<PickTaskItem> PickTaskItems => Set<PickTaskItem>();
        public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
        public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
        public DbSet<GoodsIssue> GoodsIssues => Set<GoodsIssue>();
        public DbSet<GoodsIssueItem> GoodsIssueItems => Set<GoodsIssueItem>();
        public DbSet<HandoverRecord> HandoverRecords => Set<HandoverRecord>();
        public DbSet<WarehouseShift> WarehouseShifts => Set<WarehouseShift>();
        public DbSet<QuarantineLog> QuarantineLogs => Set<QuarantineLog>();
        public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
        public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
        // Đặt tên bảng lệch với tên class (InventoryCountingSessions, không phải InventoryCountSessions):
        // production đã có sẵn 1 bảng "InventoryCountSessions" từ migration khác của đồng đội chưa
        // commit vào git (chạy thẳng vào DB), tránh trùng tên gây lỗi migration khi deploy.
        public DbSet<InventoryCountSession> InventoryCountingSessions => Set<InventoryCountSession>();
        public DbSet<InventoryCountSessionItem> InventoryCountingSessionItems => Set<InventoryCountSessionItem>();

        // UC-34: Sales Manager xử lý xung đột lịch xe/ca khi lập lịch giao hàng
        public DbSet<DeliveryScheduleConflict> DeliveryScheduleConflicts => Set<DeliveryScheduleConflict>();

        // Nhóm C (DEL-01..07): chuyến giao hàng theo xe/ca/ngày (Trip-based), song song với luồng theo Order ở trên
        public DbSet<DeliveryTrip> DeliveryTrips => Set<DeliveryTrip>();
        public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

        // Nhóm C (FUL-08): gộp pick nhiều đơn — cần Sales Manager duyệt trước khi thực thi
        public DbSet<MultiPickApproval> MultiPickApprovals => Set<MultiPickApproval>();

        // Phân bổ khách hàng cho Sale (Round-robin)
        public DbSet<RoundRobinState> RoundRobinStates => Set<RoundRobinState>();
        public DbSet<RoundRobinParticipant> RoundRobinParticipants => Set<RoundRobinParticipant>();
        public DbSet<CustomerAssignmentHistory> CustomerAssignmentHistories => Set<CustomerAssignmentHistory>();
        public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();

        // LUỒNG 7: Khách hàng yêu cầu đổi Sale phụ trách
        public DbSet<SalesChangeRequest> SalesChangeRequests => Set<SalesChangeRequest>();
        public DbSet<SalesChangeRequestOrderDecision> SalesChangeRequestOrderDecisions => Set<SalesChangeRequestOrderDecision>();

        // YÊU CẦU ĐỔI TRẢ HÀNG SAU GIAO
        public DbSet<ReturnExchangeRequest> ReturnExchangeRequests => Set<ReturnExchangeRequest>();
        public DbSet<ReturnExchangeRequestItem> ReturnExchangeRequestItems => Set<ReturnExchangeRequestItem>();
        public DbSet<ReturnExchangeRequestNewItem> ReturnExchangeRequestNewItems => Set<ReturnExchangeRequestNewItem>();

        // PHÂN HỆ ADMIN: Audit Log & Cấu hình hệ thống (Phase 1)
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
        public DbSet<SystemConfigVersion> SystemConfigVersions => Set<SystemConfigVersion>();

        // PHÂN HỆ ADMIN: Scheduled Jobs & System Health (Phase 2)
        public DbSet<JobRun> JobRuns => Set<JobRun>();
        public DbSet<WebhookLog> WebhookLogs => Set<WebhookLog>();

        // PHÂN HỆ ADMIN: Master Data (Vehicle, DiscountTier)
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<DiscountTier> DiscountTiers => Set<DiscountTier>();

        // Đánh giá sản phẩm (khách hàng)
        public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // AuditLog là bảng chỉ-ghi (insert-only): chặn mọi hành vi Update/Delete ở tầng DbContext
            // để đảm bảo tuyệt đối không ai có thể sửa/xóa nhật ký kiểm toán, kể cả do lỗi code sau này.
            var illegalAuditChange = ChangeTracker.Entries<AuditLog>()
                .Any(e => e.State == EntityState.Modified || e.State == EntityState.Deleted);

            if (illegalAuditChange)
                throw new InvalidOperationException("AuditLog là bất biến: không được phép Update hoặc Delete bản ghi kiểm toán.");

            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =========================================================================
            // 1. CẤU HÌNH TỰ ĐỘNG SINH SEQUENTIAL GUID TRÊN MSSQL (GIẢI QUYẾT HIỆU NĂNG)
            // =========================================================================
            // Giúp MSSQL tự tăng Guid theo thời gian, chống phân mảnh Clustered Index khi INSERT dữ liệu
            var entitiesWithGuidKey = modelBuilder.Model.GetEntityTypes()
                .Where(e => e.FindPrimaryKey()?.Properties.Count == 1 &&
                            e.FindPrimaryKey()?.Properties[0].ClrType == typeof(Guid));

            foreach (var entity in entitiesWithGuidKey)
            {
                var pkProperty = entity.FindPrimaryKey()!.Properties[0];
                modelBuilder.Entity(entity.ClrType).Property(pkProperty.Name)
                    .HasDefaultValueSql("NEWSEQUENTIALID()");
            }

            // =========================================================================
            // 2. ĐỊNH NGHĨA RÕ RÀNG MỐI QUAN HỆ GIỮA CÁC BẢNG (FLUENT API RELATIONSHIPS)
            // =========================================================================

            modelBuilder.Entity<GoodsReceiptItem>()
                .HasOne(gri => gri.PurchaseOrderItem)
                .WithMany()
                .HasForeignKey(gri => gri.PurchaseOrderItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- PHÂN HỆ NGƯỜI DÙNG & HỒ SƠ Khách hàng ---
            // Bắt buộc default=true ở tầng DB: các user hiện có trong DB (trước migration này)
            // phải được mở khóa mặc định, tuyệt đối không được tự động khóa toàn bộ tài khoản cũ.
            modelBuilder.Entity<User>()
                .Property(u => u.IsActive)
                .HasDefaultValue(true);

            // Quan hệ 1 - 1 giữa User và CustomerProfile
            modelBuilder.Entity<CustomerProfile>()
                .HasOne(cp => cp.User)
                .WithOne(u => u.CustomerProfile)
                .HasForeignKey<CustomerProfile>(cp => cp.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ Sale phụ trách
            modelBuilder.Entity<CustomerProfile>()
                .HasOne(cp => cp.AssignedSalesStaff)
                .WithMany()
                .HasForeignKey(cp => cp.AssignedSalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            // Sale giới thiệu khách (referral khi đăng ký) — self-reference nên bắt buộc Restrict
            modelBuilder.Entity<User>()
                .HasOne(u => u.ReferredBySalesStaff)
                .WithMany()
                .HasForeignKey(u => u.ReferredBySalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- PHÂN HỆ THÔNG BÁO ---
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.RecipientUser)
                .WithMany()
                .HasForeignKey(n => n.RecipientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Notification>()
                .Property(n => n.Type)
                .HasConversion<string>()
                .HasMaxLength(50);

            // --- PHÂN HỆ ROUND-ROBIN & LỊCH SỬ PHÂN BỔ ---
            modelBuilder.Entity<RoundRobinState>()
                .HasOne(rs => rs.LastAssignedSalesStaff)
                .WithMany()
                .HasForeignKey(rs => rs.LastAssignedSalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RoundRobinState>()
                .HasOne(rs => rs.UpdatedBy)
                .WithMany()
                .HasForeignKey(rs => rs.UpdatedById)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RoundRobinParticipant>()
                .HasOne(rp => rp.SalesStaff)
                .WithMany()
                .HasForeignKey(rp => rp.SalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RoundRobinParticipant>()
                .HasIndex(rp => rp.SalesStaffId)
                .IsUnique();

            modelBuilder.Entity<CustomerAssignmentHistory>()
                .HasOne(h => h.CustomerProfile)
                .WithMany()
                .HasForeignKey(h => h.CustomerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CustomerAssignmentHistory>()
                .HasOne(h => h.SalesStaff)
                .WithMany()
                .HasForeignKey(h => h.SalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerAssignmentHistory>()
                .HasOne(h => h.AssignedBy)
                .WithMany()
                .HasForeignKey(h => h.AssignedById)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerAssignmentHistory>()
                .HasOne(h => h.PreviousSalesStaff)
                .WithMany()
                .HasForeignKey(h => h.PreviousSalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerAssignmentHistory>()
                .Property(h => h.Source)
                .HasMaxLength(30);

            // --- LUỒNG 7: YÊU CẦU ĐỔI SALE PHỤ TRÁCH ---
            // Nhiều FK cùng trỏ về Users nên bắt buộc Restrict (tránh multiple cascade paths trên SQL Server)
            modelBuilder.Entity<SalesChangeRequest>(entity =>
            {
                entity.HasOne(r => r.CustomerProfile)
                    .WithMany()
                    .HasForeignKey(r => r.CustomerProfileId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.CurrentSalesStaff)
                    .WithMany()
                    .HasForeignKey(r => r.CurrentSalesStaffId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.DesiredSalesStaff)
                    .WithMany()
                    .HasForeignKey(r => r.DesiredSalesStaffId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.NewSalesStaff)
                    .WithMany()
                    .HasForeignKey(r => r.NewSalesStaffId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.ReviewedBy)
                    .WithMany()
                    .HasForeignKey(r => r.ReviewedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.ExplanationRequestedBy)
                    .WithMany()
                    .HasForeignKey(r => r.ExplanationRequestedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(r => r.Reason).HasMaxLength(2000);
                entity.Property(r => r.ProblemDescription).HasMaxLength(2000);

                // Chỉ cho phép 1 yêu cầu đang mở (Pending=0, MoreInfoRequested=1) cho mỗi khách hàng
                entity.HasIndex(r => r.CustomerProfileId)
                    .IsUnique()
                    .HasFilter("[Status] IN (0, 1)");
            });

            modelBuilder.Entity<SalesChangeRequestOrderDecision>(entity =>
            {
                entity.HasOne(d => d.SalesChangeRequest)
                    .WithMany(r => r.OrderDecisions)
                    .HasForeignKey(d => d.SalesChangeRequestId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Order)
                    .WithMany()
                    .HasForeignKey(d => d.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Snapshot Sale phụ trách trên đơn hàng
            modelBuilder.Entity<Order>()
                .HasOne(o => o.SalesStaff)
                .WithMany()
                .HasForeignKey(o => o.SalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerProfile>()
                .Property(cp => cp.AssignmentSource)
                .HasMaxLength(30);

            // Quan hệ 1 - 1 (hoặc 1 - n) giữa CustomerProfile và Cart
            modelBuilder.Entity<Cart>()
                .HasOne(c => c.CustomerProfile)
                .WithMany()
                .HasForeignKey(c => c.CustomerProfileId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa profile thì xóa giỏ hàng

            modelBuilder.Entity<CreditTransaction>()
                .HasOne(ct => ct.CustomerProfile)
                .WithMany()
                .HasForeignKey(ct => ct.CustomerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CreditTransaction>()
                .HasOne(ct => ct.Order)
                .WithMany()
                .HasForeignKey(ct => ct.OrderId)
                .OnDelete(DeleteBehavior.SetNull);

            // Quan hệ 1 - n giữa CustomerProfile và Sổ địa chỉ (Addresses)
            modelBuilder.Entity<Address>()
                .HasOne(a => a.CustomerProfile)
                .WithMany(cp => cp.Addresses)
                .HasForeignKey(a => a.CustomerProfileId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa tài khoản khách thì xóa luôn sổ địa chỉ kèm theo

            // Cấu hình Address.Type lưu dạng string
            modelBuilder.Entity<Address>()
                .Property(a => a.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            // --- PHÂN HỆ SẢN PHẨM & GIỎ HÀNG ---
            // Quan hệ 1 - n giữa Category và Product
            modelBuilder.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Inventory -> Product (nullable, dùng cho thành phẩm/hàng hóa)
            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.Product)
                .WithMany(p => p.Inventories)
                .HasForeignKey(i => i.ProductId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Inventory -> Material (nullable, dùng cho nguyên liệu)
            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.Material)
                .WithMany(m => m.Inventories)
                .HasForeignKey(i => i.MaterialId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.WarehouseLocation)
                .WithMany(wl => wl.Inventories)
                .HasForeignKey(i => i.WarehouseLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            // Chống race check-then-insert ở InventoryService.AddProductToWarehouseAsync: 2 request đồng
            // thời thêm cùng 1 sản phẩm/nguyên liệu vào cùng 1 vị trí kho có thể tạo 2 dòng Inventory trùng
            // nhau (mỗi dòng tự cộng/trừ tồn riêng, gây lệch số liệu). SQL Server coi NULL là "không phân
            // biệt" trong unique index (khác Postgres) -> phải lọc IS NOT NULL, tách riêng theo Product/Material.
            modelBuilder.Entity<Inventory>()
                .HasIndex(i => new { i.ProductId, i.WarehouseLocationId })
                .IsUnique()
                .HasFilter("[ProductId] IS NOT NULL");

            modelBuilder.Entity<Inventory>()
                .HasIndex(i => new { i.MaterialId, i.WarehouseLocationId })
                .IsUnique()
                .HasFilter("[MaterialId] IS NOT NULL");

            modelBuilder.Entity<Inventory>()
                .HasOne(i => i.LastUpdatedByUser)
                .WithMany()
                .HasForeignKey(i => i.LastUpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<WarehouseLocation>()
                .HasOne(wl => wl.Warehouse)
                .WithMany(w => w.Locations)
                .HasForeignKey(wl => wl.WarehouseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Quan hệ 1 - n giữa Cart và CartItem
            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Cart)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.CartId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa giỏ hàng tổng thì tự động dọn sạch item con

            // Quan hệ 1 - n giữa Product và CartItem
            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Product)
                .WithMany(p => p.CartItems)
                .HasForeignKey(ci => ci.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- PHÂN HỆ ĐƠN HÀNG, THANH TOÁN & HOÀN HÀNG ---
            // Quan hệ 1 - n giữa CustomerProfile và Order
            modelBuilder.Entity<Order>()
                .HasOne(o => o.CustomerProfile)
                .WithMany(cp => cp.Orders)
                .HasForeignKey(o => o.CustomerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // Self-referencing Order (ReplacementOrder)
            modelBuilder.Entity<Order>()
                .HasOne(o => o.ReplacementOrder)
                .WithMany()
                .HasForeignKey(o => o.ReplacementOrderId)
                .OnDelete(DeleteBehavior.NoAction);

            // Quan hệ 1 - n giữa Order và Chi tiết đơn hàng (OrderItem)
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Order)
                .WithMany(o => o.OrderItems)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade); // Hủy/Xóa đơn hàng vật lý (nếu có) thì xóa chi tiết đơn

            // Quan hệ 1 - n giữa Product và OrderItem
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- PHÂN HỆ ĐÁNH GIÁ SẢN PHẨM ---
            modelBuilder.Entity<ProductReview>()
                .HasOne(r => r.Product)
                .WithMany(p => p.Reviews)
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProductReview>()
                .HasOne(r => r.CustomerProfile)
                .WithMany()
                .HasForeignKey(r => r.CustomerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProductReview>()
                .HasOne(r => r.Order)
                .WithMany()
                .HasForeignKey(r => r.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Sales phụ trách/Admin trả lời công khai — 1 phản hồi/đánh giá
            modelBuilder.Entity<ProductReview>()
                .HasOne(r => r.RepliedByUser)
                .WithMany()
                .HasForeignKey(r => r.RepliedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Mỗi khách chỉ đánh giá 1 lần cho 1 sản phẩm (mua lại vẫn sửa qua PUT, không tạo review mới)
            modelBuilder.Entity<ProductReview>()
                .HasIndex(r => new { r.CustomerProfileId, r.ProductId })
                .IsUnique();

            // Quan hệ 1 - n giữa Order và Nhật ký giao dịch SePay (PaymentTransaction)
            modelBuilder.Entity<PaymentTransaction>()
                .HasOne(pt => pt.Order)
                .WithMany(o => o.Transactions)
                .HasForeignKey(pt => pt.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PaymentTransaction>()
                .HasOne(pt => pt.ConfirmedBy)
                .WithMany()
                .HasForeignKey(pt => pt.ConfirmedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // MGR-05: PaymentException relationships
            modelBuilder.Entity<PaymentException>()
                .HasOne(pe => pe.Order)
                .WithMany(o => o.PaymentExceptions)
                .HasForeignKey(pe => pe.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PaymentException>()
                .HasOne(pe => pe.LastRetryBy)
                .WithMany()
                .HasForeignKey(pe => pe.LastRetryByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PaymentException>()
                .HasOne(pe => pe.ResolvedBy)
                .WithMany()
                .HasForeignKey(pe => pe.ResolvedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // MGR-05: Order.ManualConfirmedBy relationship
            modelBuilder.Entity<Order>()
                .HasOne(o => o.ManualConfirmedBy)
                .WithMany()
                .HasForeignKey(o => o.ManualConfirmedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ 1 - n giữa Order và Nhật ký hàng hoàn trả Kho (ReturnedGoodsLog)
            modelBuilder.Entity<ReturnedGoodsLog>()
                .HasOne(r => r.Order)
                .WithMany(o => o.ReturnedGoodsLogs)
                .HasForeignKey(r => r.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- PHÂN HỆ KẾ TOÁN, CÔNG NỢ & TIỀN LƯƠNG ---
            // Quan hệ 1 - n giữa CustomerProfile và Công nợ (CustomerDebt)
            modelBuilder.Entity<CustomerDebt>()
                .HasOne(cd => cd.CustomerProfile)
                .WithMany(cp => cp.Debts)
                .HasForeignKey(cd => cd.CustomerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ 1 - n giữa Order và Công nợ (Mỗi đơn nợ tạo 1 dòng theo dõi tuổi nợ)
            modelBuilder.Entity<CustomerDebt>()
                .HasOne(cd => cd.Order)
                .WithMany(o => o.Debts)
                .HasForeignKey(cd => cd.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // P2-6: Sales Manager tất toán công nợ (UC-35)
            modelBuilder.Entity<CustomerDebt>()
                .HasOne(cd => cd.SettledByUser)
                .WithMany()
                .HasForeignKey(cd => cd.SettledByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // P2-6: Sales Manager mở khóa đơn giao lại (UC-35)
            modelBuilder.Entity<Order>()
                .HasOne(o => o.UnblockedByUser)
                .WithMany()
                .HasForeignKey(o => o.UnblockedByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ 1 - 1 giữa User và Hồ sơ lương gốc (EmployeeSalary)
            modelBuilder.Entity<EmployeeSalary>()
                .HasOne(es => es.User)
                .WithOne(u => u.EmployeeSalary)
                .HasForeignKey<EmployeeSalary>(es => es.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ 1 - n giữa MonthlyPayroll và Chi tiết lương nhân sự từng tháng (PayrollDetail)
            modelBuilder.Entity<PayrollDetail>()
                .HasOne(pd => pd.MonthlyPayroll)
                .WithMany(mp => mp.Details)
                .HasForeignKey(pd => pd.MonthlyPayrollId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa bảng lương tháng tổng thì xóa chi tiết của tháng đó

            // --- PHÂN HỆ TRỢ LÝ AI MARKETING ---
            // Quan hệ 1 - n giữa Product và Chiến dịch AI Marketing
            modelBuilder.Entity<AiMarketingCampaign>()
                .HasOne(am => am.Product)
                .WithMany(p => p.MarketingCampaigns)
                .HasForeignKey(am => am.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ 1 - n giữa User (Admin thực hiện) và Chiến dịch AI Marketing
            modelBuilder.Entity<AiMarketingCampaign>()
                .HasOne(am => am.Admin)
                .WithMany(u => u.MarketingCampaigns)
                .HasForeignKey(am => am.AdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ cho MarketingPost
            modelBuilder.Entity<MarketingPost>()
                .HasOne(mp => mp.Product)
                .WithMany()
                .HasForeignKey(mp => mp.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MarketingPost>()
                .HasOne(mp => mp.CreatedByUser)
                .WithMany()
                .HasForeignKey(mp => mp.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MarketingPost>()
                .HasOne(mp => mp.ApprovedByUser)
                .WithMany()
                .HasForeignKey(mp => mp.ApprovedByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);


            // --- CẤU HÌNH CHO PHÂN HỆ VẬT LIỆU (MATERIALS) ---
            // Thiết lập quan hệ Nhiều - Nhiều giữa Product và Material thông qua bảng ProductMaterial (BOM)
            modelBuilder.Entity<ProductMaterial>()
                .HasOne(pm => pm.Product)
                .WithMany() // Nếu cần bạn có thể khai báo ICollection<ProductMaterial> trong Product.cs
                .HasForeignKey(pm => pm.ProductId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa sản phẩm thì xóa định mức vật liệu của nó

            modelBuilder.Entity<ProductMaterial>()
                .HasOne(pm => pm.Material)
                .WithMany()
                .HasForeignKey(pm => pm.MaterialId)
                .OnDelete(DeleteBehavior.Restrict); // Không cho phép xóa vật liệu nếu đang có sản phẩm áp định mức


            // --- CẤU HÌNH BẢNG LƯƠNG (MONTHLYPAYROLL & PAYROLLDETAILS) ---
            // Quan hệ 1 - n giữa Bảng lương tổng (MonthlyPayroll) và Chi tiết (PayrollDetail)
            modelBuilder.Entity<PayrollDetail>()
                .HasOne(pd => pd.MonthlyPayroll)
                .WithMany(mp => mp.Details)
                .HasForeignKey(pd => pd.MonthlyPayrollId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa bảng lương tháng tổng thì tự động xóa hết chi tiết lương bên trong

            // RÀNG BUỘC QUAN TRỌNG: Kết nối trực tiếp dòng lương về bảng Người dùng (Users)
            modelBuilder.Entity<PayrollDetail>()
                .HasOne(pd => pd.Employee)
                .WithMany(u => u.PayrollDetails)
                .HasForeignKey(pd => pd.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict); // Ngăn chặn xóa User nhân viên nếu hệ thống đã chạy bảng lương lịch sử của họ

            // --- PHÂN HỆ ĐÀM PHÁN GIÁ (QUOTATION & CHAT) ---
            modelBuilder.Entity<Quotation>()
                .HasOne(q => q.CustomerProfile)
                .WithMany()
                .HasForeignKey(q => q.CustomerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Quotation>()
                .HasOne(q => q.SalesStaff)
                .WithMany()
                .HasForeignKey(q => q.SalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuotationItem>()
                .HasOne(qi => qi.Quotation)
                .WithMany(q => q.Items)
                .HasForeignKey(qi => qi.QuotationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuotationItem>()
                .HasOne(qi => qi.Product)
                .WithMany()
                .HasForeignKey(qi => qi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatMessage>()
                .HasOne(cm => cm.Quotation)
                .WithMany(q => q.ChatMessages)
                .HasForeignKey(cm => cm.QuotationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatMessage>()
                .HasOne(cm => cm.Sender)
                .WithMany()
                .HasForeignKey(cm => cm.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- MỐI QUAN HỆ CỦA CÁC PHÂN HỆ KHÁC (ĐÃ XÓA CÁC KHAI BÁO TRÙNG LẶP DƯ THỪA) ---

            // Payment Reallocation
            modelBuilder.Entity<PaymentReallocation>()
                .HasOne(pr => pr.OriginalOrder)
                .WithMany()
                .HasForeignKey(pr => pr.OriginalOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PaymentReallocation>()
                .HasOne(pr => pr.ReplacementOrder)
                .WithMany()
                .HasForeignKey(pr => pr.ReplacementOrderId)
                .OnDelete(DeleteBehavior.NoAction);

            // ReturnExchangeRequest -> Order & ReplacementOrder
            modelBuilder.Entity<ReturnExchangeRequest>()
                .HasOne(r => r.Order)
                .WithMany(o => o.ReturnExchangeRequests)
                .HasForeignKey(r => r.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReturnExchangeRequest>()
                .HasOne(r => r.ReplacementOrder)
                .WithMany()
                .HasForeignKey(r => r.ReplacementOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Purchase Order
            modelBuilder.Entity<PurchaseOrder>()
                .HasOne(po => po.CreatedBy)
                .WithMany()
                .HasForeignKey(po => po.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PurchaseOrderItem>()
                .HasOne(poi => poi.PurchaseOrder)
                .WithMany(po => po.Items)
                .HasForeignKey(poi => poi.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // PurchaseOrderItem -> Product (nullable)
            modelBuilder.Entity<PurchaseOrderItem>()
                .HasOne(poi => poi.Product)
                .WithMany()
                .HasForeignKey(poi => poi.ProductId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // PurchaseOrderItem -> Material (nullable)
            modelBuilder.Entity<PurchaseOrderItem>()
                .HasOne(poi => poi.Material)
                .WithMany()
                .HasForeignKey(poi => poi.MaterialId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // =========================================================================
            // 2.1 CẤU HÌNH CÁC BẢNG ADVANCED WAREHOUSE MODULES (v6.0)
            // =========================================================================

            modelBuilder.Entity<PickTask>()
                .HasOne(pt => pt.Order)
                .WithMany()
                .HasForeignKey(pt => pt.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PickTask>()
                .HasOne(pt => pt.Warehouse)
                .WithMany()
                .HasForeignKey(pt => pt.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PickTask>()
                .HasOne(pt => pt.AssignedUser)
                .WithMany()
                .HasForeignKey(pt => pt.AssignedUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PickTaskItem>()
                .HasOne(pti => pti.PickTask)
                .WithMany(pt => pt.Items)
                .HasForeignKey(pti => pti.PickTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StockTransfer>()
                .HasOne(st => st.SourceWarehouse)
                .WithMany()
                .HasForeignKey(st => st.SourceWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockTransfer>()
                .HasOne(st => st.DestinationWarehouse)
                .WithMany()
                .HasForeignKey(st => st.DestinationWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockTransfer>()
                .HasOne(st => st.CreatedByUser)
                .WithMany()
                .HasForeignKey(st => st.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockTransferItem>()
                .HasOne(sti => sti.StockTransfer)
                .WithMany(st => st.Items)
                .HasForeignKey(sti => sti.StockTransferId)
                .OnDelete(DeleteBehavior.Cascade);

            // StockTransferItem -> Product (nullable)
            modelBuilder.Entity<StockTransferItem>()
                .HasOne(sti => sti.Product)
                .WithMany()
                .HasForeignKey(sti => sti.ProductId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // StockTransferItem -> Material (nullable)
            modelBuilder.Entity<StockTransferItem>()
                .HasOne(sti => sti.Material)
                .WithMany()
                .HasForeignKey(sti => sti.MaterialId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuotationVersion>()
                .HasOne(qv => qv.CreatedByUser)
                .WithMany()
                .HasForeignKey(qv => qv.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GoodsIssue>()
                .HasOne(gi => gi.Warehouse)
                .WithMany()
                .HasForeignKey(gi => gi.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GoodsIssue>()
                .HasOne(gi => gi.IssuedByUser)
                .WithMany()
                .HasForeignKey(gi => gi.IssuedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GoodsIssue>()
                .HasIndex(gi => gi.PaperDocumentNumber)
                .IsUnique()
                .HasFilter("[PaperDocumentNumber] IS NOT NULL");

            modelBuilder.Entity<GoodsIssueItem>()
                .HasOne(gii => gii.GoodsIssue)
                .WithMany(gi => gi.Items)
                .HasForeignKey(gii => gii.GoodsIssueId)
                .OnDelete(DeleteBehavior.Cascade);

            // GoodsIssueItem -> Product (nullable)
            modelBuilder.Entity<GoodsIssueItem>()
                .HasOne(gii => gii.Product)
                .WithMany()
                .HasForeignKey(gii => gii.ProductId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // GoodsIssueItem -> Material (nullable)
            modelBuilder.Entity<GoodsIssueItem>()
                .HasOne(gii => gii.Material)
                .WithMany()
                .HasForeignKey(gii => gii.MaterialId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<HandoverRecord>()
                .HasOne(hr => hr.Order)
                .WithMany()
                .HasForeignKey(hr => hr.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HandoverRecord>()
                .HasOne(hr => hr.WarehouseStaff)
                .WithMany()
                .HasForeignKey(hr => hr.WarehouseStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<HandoverRecord>()
                .HasOne(hr => hr.SalesStaff)
                .WithMany()
                .HasForeignKey(hr => hr.SalesStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            // ─── LUỒNG 5: QuarantineLog ───────────────────────────────────────
            modelBuilder.Entity<QuarantineLog>()
                .HasOne(q => q.Order)
                .WithMany()
                .HasForeignKey(q => q.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            modelBuilder.Entity<QuarantineLog>()
                .HasOne(q => q.GoodsReceiptItem)
                .WithMany()
                .HasForeignKey(q => q.GoodsReceiptItemId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            modelBuilder.Entity<QuarantineLog>()
                .HasOne(q => q.Product)
                .WithMany()
                .HasForeignKey(q => q.ProductId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            modelBuilder.Entity<QuarantineLog>()
                .HasOne(q => q.Material)
                .WithMany()
                .HasForeignKey(q => q.MaterialId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            modelBuilder.Entity<QuarantineLog>()
                .HasOne(ql => ql.Inventory)
                .WithMany()
                .HasForeignKey(ql => ql.InventoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuarantineLog>()
                .HasOne(ql => ql.ReceivedByUser)
                .WithMany()
                .HasForeignKey(ql => ql.ReceivedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuarantineLog>()
                .HasOne(ql => ql.DispatchedByUser)
                .WithMany()
                .HasForeignKey(ql => ql.DispatchedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuarantineLog>()
                .Property(ql => ql.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            modelBuilder.Entity<DeliveryScheduleConflict>(entity =>
            {
                entity.Property(c => c.Shift).HasMaxLength(20);
                entity.Property(c => c.OrderIds).HasMaxLength(2000);
                entity.Property(c => c.ResolutionAction).HasMaxLength(20);
                entity.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
                entity.HasIndex(c => c.Status);
            });

            // Nhóm C (DEL-01..07): DeliveryTrip / DeliveryAttempt
            modelBuilder.Entity<DeliveryTrip>(entity =>
            {
                entity.Property(t => t.Shift).HasMaxLength(20);
                entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
                entity.HasIndex(t => new { t.VehicleId, t.Shift, t.TripDate });

                entity.HasOne(t => t.Vehicle)
                    .WithMany()
                    .HasForeignKey(t => t.VehicleId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(t => t.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(t => t.CreatedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Order>()
                .HasOne(o => o.DeliveryTrip)
                .WithMany(t => t.Orders)
                .HasForeignKey(o => o.DeliveryTripId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DeliveryAttempt>(entity =>
            {
                entity.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20);
                entity.Property(a => a.FailureReason).HasMaxLength(500);
                entity.HasIndex(a => new { a.OrderId, a.DeliveryTripId });

                entity.HasOne(a => a.Order)
                    .WithMany()
                    .HasForeignKey(a => a.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(a => a.DeliveryTrip)
                    .WithMany(t => t.Attempts)
                    .HasForeignKey(a => a.DeliveryTripId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(a => a.RecordedByUser)
                    .WithMany()
                    .HasForeignKey(a => a.RecordedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Nhóm C (FUL-08): MultiPickApproval
            modelBuilder.Entity<MultiPickApproval>(entity =>
            {
                entity.Property(a => a.OrderIds).HasMaxLength(2000);
                entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
                entity.Property(a => a.DecisionNote).HasMaxLength(1000);
                entity.HasIndex(a => a.Status);

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(a => a.RequestedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(a => a.DecidedByUserId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =========================================================================
            // 3. CẤU HÌNH CÁC CHỈ MỤC ĐỘC NHẤT (UNIQUE INDEXES) & ĐỘ CHÍNH XÁC (PRECISION)
            // =========================================================================

            // Đảm bảo dữ liệu không bị trùng lặp ở tầng database vật lý
            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
            modelBuilder.Entity<User>().HasIndex(u => u.PhoneNumber).IsUnique()
                .HasFilter("[PhoneNumber] IS NOT NULL AND [PhoneNumber] <> ''");
            modelBuilder.Entity<Order>().HasIndex(o => o.OrderCode).IsUnique();

            // MGR-05: Unique index cho mã giao dịch ngân hàng (chống tạo 2 lần cùng 1 transaction)
            modelBuilder.Entity<PaymentTransaction>()
                .HasIndex(pt => pt.TransactionId)
                .IsUnique()
                .HasFilter("[TransactionId] != ''");


            // Ép MSSQL nhận diện đúng định dạng Decimal(18,2) thay vì bị cảnh báo cắt cụt dữ liệu tài chính
            var decimalProperties = modelBuilder.Model.GetEntityTypes()
                .SelectMany(e => e.GetProperties())
                .Where(p => p.ClrType == typeof(decimal));

            foreach (var property in decimalProperties)
            {
                property.SetPrecision(18);
                property.SetScale(2);
            }

            // =========================================================================
            // 4. SEED DATA TÀI KHOẢN MẪU
            // =========================================================================
            var defaultPasswordHash = "$2a$11$yxVoqFJ39C6xv9yAy6v8culp85Msmy.BhBGfAreZWDxCY5RSs0wY."; // "123456"
            var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var adminUser = new User { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), FullName = "Admin Test", Email = "admin.test@viettien.com", PhoneNumber = "0999000001", PasswordHash = defaultPasswordHash, Role = SystemRole.Admin, IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };
            var ceoUser = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), FullName = "CEO Test", Email = "ceo.test@viettien.com", PhoneNumber = "0999000002", PasswordHash = defaultPasswordHash, Role = SystemRole.CEO, IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };
            var smUser = new User { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), FullName = "Sales Manager Test", Email = "salesmanager.test@viettien.com", PhoneNumber = "0999000003", PasswordHash = defaultPasswordHash, Role = SystemRole.SalesManager, IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };
            var ssUser = new User { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), FullName = "Sales Staff Test", Email = "salesstaff.test@viettien.com", PhoneNumber = "0999000004", PasswordHash = defaultPasswordHash, Role = SystemRole.SalesStaff, IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };
            // AssignedWarehouseId = WH-DEFAULT: bắt buộc phải set, nếu không InventoryService/StockTransferService
            // sẽ chặn luôn mọi thao tác kho của WarehouseStaff này (so sánh AssignedWarehouseId != warehouseId, null luôn lệch).
            var wsUser = new User { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), FullName = "Warehouse Staff Test", Email = "warehousestaff.test@viettien.com", PhoneNumber = "0999000005", PasswordHash = defaultPasswordHash, Role = SystemRole.WarehouseStaff, AssignedWarehouseId = Guid.Parse("ee73f2cc-05fd-4b0e-8a48-61f89a2d345a"), IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };
            var asUser = new User { Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), FullName = "Accounting Staff Test", Email = "accountingstaff.test@viettien.com", PhoneNumber = "0999000006", PasswordHash = defaultPasswordHash, Role = SystemRole.AccountingStaff, IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };
            var customerUser = new User { Id = Guid.Parse("77777777-7777-7777-7777-777777777777"), FullName = "Customer Test", Email = "customer.test@viettien.com", PhoneNumber = "0999000007", PasswordHash = defaultPasswordHash, Role = SystemRole.Customer, IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };

            // Thêm Sales Staff #2/#3 để Round-robin (WF-01) có nhiều hơn 1 lượt xoay vòng thật sự
            var ss2User = new User { Id = Guid.Parse("44444444-4444-4444-4444-444444444402"), FullName = "Sales Staff Test 2", Email = "salesstaff2.test@viettien.com", PhoneNumber = "0999000104", PasswordHash = defaultPasswordHash, Role = SystemRole.SalesStaff, IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };
            var ss3User = new User { Id = Guid.Parse("44444444-4444-4444-4444-444444444403"), FullName = "Sales Staff Test 3", Email = "salesstaff3.test@viettien.com", PhoneNumber = "0999000204", PasswordHash = defaultPasswordHash, Role = SystemRole.SalesStaff, IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };

            // Warehouse Staff #2/#3: mỗi kho vệ tinh mới (WH-TRADE/WH-PE) có 1 người phụ trách riêng
            var ws2User = new User { Id = Guid.Parse("55555555-5555-5555-5555-555555555502"), FullName = "Warehouse Staff Test 2", Email = "warehousestaff2.test@viettien.com", PhoneNumber = "0999000105", PasswordHash = defaultPasswordHash, Role = SystemRole.WarehouseStaff, AssignedWarehouseId = Guid.Parse("f0000003-0003-4003-a003-000000000001"), IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };
            var ws3User = new User { Id = Guid.Parse("55555555-5555-5555-5555-555555555503"), FullName = "Warehouse Staff Test 3", Email = "warehousestaff3.test@viettien.com", PhoneNumber = "0999000205", PasswordHash = defaultPasswordHash, Role = SystemRole.WarehouseStaff, AssignedWarehouseId = Guid.Parse("f0000004-0004-4004-a004-000000000001"), IsEmailVerified = true, IsPhoneVerified = true, CreatedAt = baseDate };

            modelBuilder.Entity<User>().HasData(
                adminUser, ceoUser, smUser, ssUser, wsUser, asUser, customerUser,
                ss2User, ss3User, ws2User, ws3User
            );

            var customerProfile = new CustomerProfile
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                UserId = customerUser.Id
            };
            
            modelBuilder.Entity<CustomerProfile>().HasData(customerProfile);

            // Seed Data: Warehouse & Location
            var defaultWarehouseId = Guid.Parse("ee73f2cc-05fd-4b0e-8a48-61f89a2d345a");
            modelBuilder.Entity<Warehouse>().HasData(new Warehouse
            {
                Id = defaultWarehouseId,
                Name = "Kho mặc định",
                Code = "WH-DEFAULT"
            });

            var defaultLocationId = Guid.Parse("2006d0a6-37a9-46ca-b8a0-bb061ec9f1e9");
            modelBuilder.Entity<WarehouseLocation>().HasData(new WarehouseLocation
            {
                Id = defaultLocationId,
                WarehouseId = defaultWarehouseId,
                Name = "Vị trí mặc định",
                Type = "Normal"
            });

            // Seed Data: 2 kho vệ tinh theo đúng cấu trúc 3 kho ở business.md §1.4 (WH-TRADE, WH-PE).
            // WH-DEFAULT giữ nguyên vai trò WH-PROD (kho SX + điểm tập kết trung tâm) vì OrderService.cs
            // đang hard-code chuỗi "WH-DEFAULT" ở nhiều chỗ -> không được đổi code/tên của kho này.
            var whTradeId = Guid.Parse("f0000003-0003-4003-a003-000000000001");
            var whPeId = Guid.Parse("f0000004-0004-4004-a004-000000000001");
            modelBuilder.Entity<Warehouse>().HasData(
                new Warehouse { Id = whTradeId, Name = "Kho Thương Mại", Code = "WH-TRADE" },
                new Warehouse { Id = whPeId, Name = "Kho Màng PE & Xốp", Code = "WH-PE" }
            );

            var whTradeLocId = Guid.Parse("f0000003-0003-4003-a003-000000000002");
            var whPeLocId = Guid.Parse("f0000004-0004-4004-a004-000000000002");
            modelBuilder.Entity<WarehouseLocation>().HasData(
                new WarehouseLocation { Id = whTradeLocId, WarehouseId = whTradeId, Name = "Vị trí mặc định", Type = "Normal" },
                new WarehouseLocation { Id = whPeLocId, WarehouseId = whPeId, Name = "Vị trí mặc định", Type = "Normal" }
            );

            // Seed Data: WarehouseShifts
            modelBuilder.Entity<WarehouseShift>().HasData(
                new WarehouseShift { Id = Guid.Parse("11111111-1111-4111-a111-111111111111"), Name = "Ca Sáng", StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(14, 0, 0), Description = "Ca làm việc buổi sáng" },
                new WarehouseShift { Id = Guid.Parse("22222222-2222-4222-a222-222222222222"), Name = "Ca Trưa", StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(22, 0, 0), Description = "Ca làm việc buổi chiều/tối" },
                new WarehouseShift { Id = Guid.Parse("33333333-3333-4333-a333-333333333333"), Name = "Ca Chiều", StartTime = new TimeSpan(22, 0, 0), EndTime = new TimeSpan(6, 0, 0), Description = "Ca làm việc đêm" }
            );

            // Seed Data: Categories
            var catSpongeId = Guid.Parse("bc7b7b78-9319-4574-8f99-01a6cbfb7d5e");
            var catCartonId = Guid.Parse("cec401fa-bd4a-4d94-bc7a-0d26007445c9");
            var catTapeId = Guid.Parse("d373bbfa-184c-4eac-9633-38bee5ef6478");
            var catToolId = Guid.Parse("f69da084-f0e9-4fdf-acc5-7917818991c3");

            modelBuilder.Entity<Category>().HasData(
                new Category { Id = catSpongeId, Name = "Màng Bọc Chống Sốc", Description = "Màng xốp hơi, xốp nổ, màng PE quấn pallet", IsActive = true },
                new Category { Id = catCartonId, Name = "Thùng Carton", Description = "Hộp carton đóng hàng 3 lớp, 5 lớp đủ kích thước", IsActive = true },
                new Category { Id = catTapeId, Name = "Băng Keo / Băng Dính", Description = "Các loại băng keo trong, đục, băng keo 2 mặt, băng keo giấy", IsActive = true },
                new Category { Id = catToolId, Name = "Dụng Cụ Đóng Gói", Description = "Cắt băng keo, dao rọc giấy, màng co", IsActive = true }
            );

            // Seed Data: Products
            var pTapeTrongId = Guid.Parse("659870d7-5b15-4496-a4bb-03ab28900170");
            var pPeWrapId = Guid.Parse("e24b1960-21d2-4385-8155-17557c0ce8b9");
            var pBubbleId = Guid.Parse("a3c3e6e5-860a-464c-a073-1b847a9db570");
            var pTapeDucId = Guid.Parse("3a369d6a-500b-4e11-b127-494e6c74a72e");
            var pCutToolId = Guid.Parse("aa275908-173a-47fb-a2cb-8eb173c934ef");
            var pCartonId = Guid.Parse("cc25fd5c-3ad6-4f95-b19f-e86635d1d16d");

            modelBuilder.Entity<Product>().HasData(
                new Product { Id = pTapeTrongId, CategoryId = catTapeId, Name = "Băng Keo Trong OPP 5F 100 Yard (Cây 6 Cuộn)", Sku = "TAPE-TR5F-100", StandardListedPrice = 65000m, Description = "Băng keo trong suốt dán thùng OPP siêu dính. Độ dày màng 50 mic, không đứt ngang khi kéo dán. Thích hợp đóng gói bưu phẩm, thùng hàng.", Specifications = "Quy Cách: Cây 6 cuộn\nChiều Rộng: 5cm (5F)\nChiều Dài: 100 Yard\nĐộ Dính: 50 Mic", ImageUrl = "https://res.cloudinary.com/dx9acdd0y/image/upload/v1781689328/82ad4b54-13f5-4ff2-b624-87b0cf08d545.png", IsDiscontinued = false },
                new Product { Id = pPeWrapId, CategoryId = catSpongeId, Name = "Cuộn Màng Chít PE Quấn Pallet Lõi Cứng", Sku = "WRAP-PE-3KG", StandardListedPrice = 120000m, Description = "Màng co PE quấn hàng hóa, cố định kiện hàng trên pallet, chống bụi bẩn và chống thấm nước.", Specifications = "Trọng Lượng: 3.0 kg\nĐộ Dày: 17 mic\nMàu Sắc: Trong suốt", ImageUrl = "https://res.cloudinary.com/dx9acdd0y/image/upload/v1781689368/fb41db67-49fd-4213-afa2-3153ac46028f.png", IsDiscontinued = false },
                new Product { Id = pBubbleId, CategoryId = catSpongeId, Name = "Cuộn Màng Xốp Hơi (Bong Bóng) 1.2m x 100m", Sku = "WRAP-BB-1M2", StandardListedPrice = 250000m, Description = "Màng xốp hơi (bubble wrap) bong bóng khí chống sốc, bảo vệ hàng dễ vỡ trong quá trình vận chuyển.", Specifications = "Kích Thước: Cao 1.2m x Dài 100m\nĐường Kính Hạt: 10mm\nMàu Sắc: Trắng", ImageUrl = "https://res.cloudinary.com/dx9acdd0y/image/upload/v1781689413/8d00a868-e6f0-4c21-8cd5-8228301b06cc.png", IsDiscontinued = false },
                new Product { Id = pTapeDucId, CategoryId = catTapeId, Name = "Băng Keo Đục Dán Thùng 5F 100 Yard (Cây 6 Cuộn)", Sku = "TAPE-BR5F-100", StandardListedPrice = 65000m, Description = "Băng keo màu đục, bám dính tốt trên bề mặt giấy carton. Phù hợp đóng gói hàng hóa, bưu phẩm.", Specifications = "Quy Cách: Cây 6 cuộn\nChiều Rộng: 5cm (5F)\nChiều Dài: 100 Yard\nMàu Sắc: Đục / Nâu", ImageUrl = "https://res.cloudinary.com/dx9acdd0y/image/upload/v1781689446/8a39bfa0-7bbb-4727-baea-86fbfb315512.png", IsDiscontinued = false },
                new Product { Id = pCutToolId, CategoryId = catToolId, Name = "Dụng Cụ Cắt Băng Keo Cầm Tay 5F Dân Cường", Sku = "TOOL-CUT-5F", StandardListedPrice = 25000m, Description = "Dụng cụ cắt băng keo cầm tay chắc chắn, lưỡi dao sắc bén, chuyên dùng cho băng keo 5cm.", Specifications = "Thương Hiệu: Dân Cường\nChất Liệu: Sắt sơn tĩnh điện\nDùng Cho: Băng keo 5cm (5F)", ImageUrl = "https://placehold.co/600x600/f3f4f6/9ca3af?text=Cat+Bang+Keo", IsDiscontinued = false },
                new Product { Id = pCartonId, CategoryId = catCartonId, Name = "Thùng Carton 3 Lớp Gửi GHTK 30x20x15cm", Sku = "BOX-3L-302015", StandardListedPrice = 3500m, Description = "Thùng carton đóng hàng 3 lớp sóng B cứng cáp, chịu lực tốt. Kích thước phù hợp gửi hàng qua đơn vị vận chuyển.", Specifications = "Kích Thước: 30x20x15 cm\nCấu Tạo: 3 lớp sóng B\nĐịnh Lượng: 120g", ImageUrl = "https://placehold.co/600x600/f3f4f6/9ca3af?text=Thung+Carton", IsDiscontinued = false }
            );

            // Sản phẩm riêng cho WH-TRADE: hàng nhập ngoài từ nhà cung cấp (business.md §1.4), chỉ tồn kho tại WH-TRADE
            var pTapeLogoImportId = Guid.Parse("f0000007-0007-4007-a007-000000000001");
            modelBuilder.Entity<Product>().HasData(
                new Product { Id = pTapeLogoImportId, CategoryId = catTapeId, Name = "Băng Keo In Logo Nhập Khẩu 5F 100 Yard (Cây 6 Cuộn)", Sku = "TAPE-IMP-LOGO5F", StandardListedPrice = 95000m, Description = "Băng keo in logo theo yêu cầu, nhập khẩu từ nhà cung cấp đối tác, chất lượng cao cấp cho khách hàng doanh nghiệp.", Specifications = "Quy Cách: Cây 6 cuộn\nChiều Rộng: 5cm (5F)\nChiều Dài: 100 Yard\nNguồn Gốc: Nhập khẩu", ImageUrl = "https://placehold.co/600x600/f3f4f6/9ca3af?text=Tape+Import", IsDiscontinued = false }
            );

            // --- STOCK TRANSACTION (LỊCH SỬ TỒN KHO) ---
            modelBuilder.Entity<StockTransaction>()
                .HasOne(st => st.Inventory)
                .WithMany()
                .HasForeignKey(st => st.InventoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // StockTransaction -> Product (nullable)
            modelBuilder.Entity<StockTransaction>()
                .HasOne(st => st.Product)
                .WithMany()
                .HasForeignKey(st => st.ProductId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // StockTransaction -> Material (nullable)
            modelBuilder.Entity<StockTransaction>()
                .HasOne(st => st.Material)
                .WithMany()
                .HasForeignKey(st => st.MaterialId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockTransaction>()
                .HasOne(st => st.WarehouseLocation)
                .WithMany()
                .HasForeignKey(st => st.WarehouseLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockTransaction>()
                .HasOne(st => st.CreatedByUser)
                .WithMany()
                .HasForeignKey(st => st.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- P0-1: Đề xuất điều chỉnh tồn kho (Warehouse Staff -> CEO duyệt) ---
            modelBuilder.Entity<StockAdjustment>()
                .HasOne(a => a.Inventory)
                .WithMany()
                .HasForeignKey(a => a.InventoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockAdjustment>()
                .HasOne(a => a.ProposedByUser)
                .WithMany()
                .HasForeignKey(a => a.ProposedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockAdjustment>()
                .HasOne(a => a.DecidedByUser)
                .WithMany()
                .HasForeignKey(a => a.DecidedByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockAdjustment>()
                .Property(a => a.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            // --- DEF-L4-003: Phiên kiểm kê tồn kho theo Warehouse ---
            modelBuilder.Entity<InventoryCountSession>()
                .HasOne(s => s.Warehouse)
                .WithMany()
                .HasForeignKey(s => s.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<InventoryCountSession>()
                .HasOne(s => s.OpenedByUser)
                .WithMany()
                .HasForeignKey(s => s.OpenedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<InventoryCountSession>()
                .HasOne(s => s.ClosedByUser)
                .WithMany()
                .HasForeignKey(s => s.ClosedByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<InventoryCountSession>()
                .Property(s => s.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            modelBuilder.Entity<InventoryCountSessionItem>()
                .HasOne(i => i.Session)
                .WithMany(s => s.Items)
                .HasForeignKey(i => i.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<InventoryCountSessionItem>()
                .HasOne(i => i.Inventory)
                .WithMany()
                .HasForeignKey(i => i.InventoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<InventoryCountSessionItem>()
                .HasOne(i => i.StockAdjustment)
                .WithMany()
                .HasForeignKey(i => i.StockAdjustmentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Seed Data: Materials (nguyên liệu thô cho WH-PE, phục vụ WF-17 xuất NVL cho sản xuất ngoài hệ thống)
            var matPeResinId = Guid.Parse("f0000005-0005-4005-a005-000000000001");
            var matPeFilmRawId = Guid.Parse("f0000005-0005-4005-a005-000000000002");
            modelBuilder.Entity<Material>().HasData(
                new Material { Id = matPeResinId, Name = "Hạt Nhựa PE Nguyên Sinh", Unit = "Kg", CurrentStock = 0, SafetyThreshold = 100 },
                new Material { Id = matPeFilmRawId, Name = "Cuộn Màng PE Thô (Chưa Cắt)", Unit = "Cuộn", CurrentStock = 0, SafetyThreshold = 50 }
            );

            // Seed Data: Inventories
            modelBuilder.Entity<Inventory>().HasData(
                new Inventory { Id = Guid.Parse("b115bc37-ab72-40e4-b1fa-274d7b329efe"), ProductId = pPeWrapId, WarehouseLocationId = defaultLocationId, OnHandQuantity = 10000, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("0f79f912-5b0d-4d7c-ad7b-35fbf4a6497e"), ProductId = pBubbleId, WarehouseLocationId = defaultLocationId, OnHandQuantity = 10000, ReservedQuantity = 50, QuarantineQuantity = 50, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("16eaf448-d4e7-4757-b60c-3a3348cbf10c"), ProductId = pTapeTrongId, WarehouseLocationId = defaultLocationId, OnHandQuantity = 10000, ReservedQuantity = 1000, QuarantineQuantity = 1000, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("d934b287-a9e9-4d7c-86cf-4e82d97957f6"), ProductId = pCartonId, WarehouseLocationId = defaultLocationId, OnHandQuantity = 10000, ReservedQuantity = 5000, QuarantineQuantity = 5000, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("c9e0fca9-3d26-402b-b6a4-58357e527d10"), ProductId = pCutToolId, WarehouseLocationId = defaultLocationId, OnHandQuantity = 10000, ReservedQuantity = 200, QuarantineQuantity = 200, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("9925c3cf-e4af-4a88-8840-961d4281f417"), ProductId = pTapeDucId, WarehouseLocationId = defaultLocationId, OnHandQuantity = 9999, ReservedQuantity = 799, QuarantineQuantity = 749, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },

                // WH-PE: đúng chuyên trách "Màng PE & Xốp" -> tồn thêm 2 sản phẩm màng/xốp đã có sẵn tại kho này
                new Inventory { Id = Guid.Parse("f0000008-0008-4008-a008-000000000001"), ProductId = pPeWrapId, WarehouseLocationId = whPeLocId, OnHandQuantity = 8000, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("f0000008-0008-4008-a008-000000000002"), ProductId = pBubbleId, WarehouseLocationId = whPeLocId, OnHandQuantity = 8000, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                // WH-PE: nguyên liệu thô phục vụ WF-17 (xuất NVL cho sản xuất ngoài hệ thống)
                new Inventory { Id = Guid.Parse("f0000008-0008-4008-a008-000000000003"), MaterialId = matPeResinId, WarehouseLocationId = whPeLocId, OnHandQuantity = 500, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("f0000008-0008-4008-a008-000000000004"), MaterialId = matPeFilmRawId, WarehouseLocationId = whPeLocId, OnHandQuantity = 300, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },

                // WH-TRADE: hàng nhập ngoài từ nhà cung cấp -> vài SKU thương mại phổ biến + 1 SKU riêng chỉ có ở kho này
                new Inventory { Id = Guid.Parse("f0000009-0009-4009-a009-000000000001"), ProductId = pTapeTrongId, WarehouseLocationId = whTradeLocId, OnHandQuantity = 5000, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("f0000009-0009-4009-a009-000000000002"), ProductId = pCartonId, WarehouseLocationId = whTradeLocId, OnHandQuantity = 6000, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("f0000009-0009-4009-a009-000000000003"), ProductId = pCutToolId, WarehouseLocationId = whTradeLocId, OnHandQuantity = 3000, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 },
                new Inventory { Id = Guid.Parse("f0000009-0009-4009-a009-000000000004"), ProductId = pTapeLogoImportId, WarehouseLocationId = whTradeLocId, OnHandQuantity = 2000, ReservedQuantity = 0, QuarantineQuantity = 0, AllocatedQuantity = 0, DamagedQuantity = 0, InTransitQuantity = 0 }
            );

            // =========================================================================
            // 5. PHÂN HỆ ADMIN: AUDIT LOG & CẤU HÌNH HỆ THỐNG (PHASE 1)
            // =========================================================================

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(a => a.EntityName);
                entity.HasIndex(a => a.ActorUserId);
                entity.HasIndex(a => a.CreatedAt);
                entity.HasIndex(a => a.Action);
                entity.Property(a => a.EntityName).HasMaxLength(100);
                entity.Property(a => a.EntityId).HasMaxLength(200);
                entity.Property(a => a.Action).HasMaxLength(50);
                entity.Property(a => a.ActorEmail).HasMaxLength(256);
                entity.Property(a => a.ActorRole).HasMaxLength(50);
                entity.Property(a => a.IpAddress).HasMaxLength(64);
            });

            modelBuilder.Entity<SystemConfig>(entity =>
            {
                entity.HasKey(c => c.Key);
                entity.Property(c => c.Key).HasMaxLength(100);
                entity.Property(c => c.ValueType).HasConversion<string>().HasMaxLength(20);
                entity.Property(c => c.OwnerLevel).HasMaxLength(50);
                entity.Property(c => c.IsSecret).HasDefaultValue(false);
            });

            modelBuilder.Entity<SystemConfigVersion>(entity =>
            {
                entity.HasOne(v => v.Config)
                    .WithMany(c => c.Versions)
                    .HasForeignKey(v => v.ConfigKey)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(v => new { v.ConfigKey, v.EffectiveDate });
                entity.Property(v => v.ActorEmail).HasMaxLength(256);
            });

            // Seed các tham số cấu hình hệ thống theo business.md §7 (mỗi key có 1 version khởi tạo)
            var configBaseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var systemConfigSeeds = new (string Key, string Value, SystemConfigValueType Type, string Unit, string Owner, string Description, Guid VersionId)[]
            {
                ("PRICE_LOCK_HOURS", "24", SystemConfigValueType.Int, "Giờ", "Admin", "Thời gian khóa giá báo giá", Guid.Parse("a0000001-0001-4001-a001-000000000001")),
                ("SEPAY_RESERVATION_MINUTES", "15", SystemConfigValueType.Int, "Phút", "Admin", "Thời gian giữ tồn cho đơn SePay chờ thanh toán", Guid.Parse("a0000001-0001-4001-a001-000000000002")),
                ("COD_RESERVATION_MINUTES", "35", SystemConfigValueType.Int, "Phút", "Admin", "Thời gian giữ tồn cho đơn COD chờ xác nhận", Guid.Parse("a0000001-0001-4001-a001-000000000003")),
                ("COD_WARNING_MINUTES", "25", SystemConfigValueType.Int, "Phút", "Admin", "Mốc cảnh báo Sale trước khi hết hạn giữ tồn COD", Guid.Parse("a0000001-0001-4001-a001-000000000004")),
                ("COD_ESCALATION_MINUTES", "30", SystemConfigValueType.Int, "Phút", "Admin", "Mốc leo thang cảnh báo Manager cho đơn COD", Guid.Parse("a0000001-0001-4001-a001-000000000005")),
                ("OTP_EXPIRE_MINUTES", "5", SystemConfigValueType.Int, "Phút", "Admin", "Thời gian hết hạn mã OTP", Guid.Parse("a0000001-0001-4001-a001-000000000006")),
                ("OTP_RESEND_SECONDS", "60", SystemConfigValueType.Int, "Giây", "Admin", "Thời gian tối thiểu giữa 2 lần gửi lại OTP", Guid.Parse("a0000001-0001-4001-a001-000000000007")),
                ("OTP_MAX_ATTEMPTS", "5", SystemConfigValueType.Int, "Lần", "Admin", "Số lần gửi OTP tối đa trong 30 phút", Guid.Parse("a0000001-0001-4001-a001-000000000008")),
                ("QUOTATION_MIN_VALUE", "100000000", SystemConfigValueType.Decimal, "VND", "Admin/CEO", "Ngưỡng giá trị đơn bắt buộc chuyển sang luồng báo giá", Guid.Parse("a0000001-0001-4001-a001-000000000009")),
                ("LIST_PRICE_MAX_EXCLUSIVE", "10000000", SystemConfigValueType.Decimal, "VND", "Admin/CEO", "Ngưỡng áp dụng giá niêm yết (dưới ngưỡng này)", Guid.Parse("a0000001-0001-4001-a001-000000000010")),
                ("MAX_SCHEDULED_MARKETING_POSTS", "30", SystemConfigValueType.Int, "Bài viết", "Admin", "Số bài viết marketing được lên lịch tối đa", Guid.Parse("a0000001-0001-4001-a001-000000000011")),
                ("DELIVERY_FAILURE_MANAGER_THRESHOLD", "3", SystemConfigValueType.Int, "Lần thử giao", "Admin/Manager", "Số lần giao thất bại trước khi báo Manager", Guid.Parse("a0000001-0001-4001-a001-000000000012")),
                ("INVENTORY_COUNT_VARIANCE_THRESHOLD", "5", SystemConfigValueType.Int, "Đơn vị", "Admin/CEO", "Chênh lệch tối đa (số lượng tuyệt đối) khi đóng phiên kiểm kê được áp dụng thẳng; vượt ngưỡng bắt buộc CEO duyệt", Guid.Parse("a0000001-0001-4001-a001-000000000013")),
            };

            modelBuilder.Entity<SystemConfig>().HasData(
                systemConfigSeeds.Select(s => new SystemConfig
                {
                    Key = s.Key,
                    ValueType = s.Type,
                    Unit = s.Unit,
                    OwnerLevel = s.Owner,
                    Description = s.Description,
                    IsActive = true
                }).ToArray()
            );

            // Đăng ký (registry only, KHÔNG seed version) các tham số Integrations cho Admin cấu hình
            // runtime thay vì hardcode trong appsettings.json (UC-59). Không seed SystemConfigVersion ở
            // đây để tránh chép secret vào file migration — chừng nào Admin chưa set giá trị mới qua UI,
            // EffectiveValue = null và mọi service đọc config sẽ tự fallback về appsettings/IConfiguration
            // như hiện tại (hành vi không đổi ngay sau migration).
            var integrationConfigSeeds = new (string Key, SystemConfigValueType Type, string? Unit, string Owner, string Description, bool IsSecret)[]
            {
                ("SEPAY_API_TOKEN", SystemConfigValueType.String, null, "Admin", "API Token xác thực webhook SePay", true),
                ("SEPAY_BANK_ACCOUNT", SystemConfigValueType.String, null, "Admin", "Số tài khoản ngân hàng nhận thanh toán SePay", false),
                ("SEPAY_BANK_ID", SystemConfigValueType.String, null, "Admin", "Mã ngân hàng (bankId) dùng sinh QR SePay", false),
                ("GOOGLE_OAUTH_CLIENT_ID", SystemConfigValueType.String, null, "Admin", "Google OAuth Client ID dùng xác thực đăng nhập Google", false),
                ("ESMS_API_KEY", SystemConfigValueType.String, null, "Admin", "API Key dịch vụ SMS eSMS", true),
                ("ESMS_SECRET_KEY", SystemConfigValueType.String, null, "Admin", "Secret Key dịch vụ SMS eSMS", true),
                ("EMAIL_SMTP_HOST", SystemConfigValueType.String, null, "Admin", "SMTP host gửi email hệ thống", false),
                ("EMAIL_SMTP_PORT", SystemConfigValueType.Int, null, "Admin", "SMTP port gửi email hệ thống", false),
                ("EMAIL_SENDER_EMAIL", SystemConfigValueType.String, null, "Admin", "Địa chỉ email gửi đi", false),
                ("EMAIL_SENDER_PASSWORD", SystemConfigValueType.String, null, "Admin", "Mật khẩu ứng dụng (App Password) của hộp thư gửi", true),
                ("MAKE_WEBHOOK_URL", SystemConfigValueType.String, null, "Admin", "Webhook URL kịch bản Make.com đăng bài Facebook", false),
            };

            modelBuilder.Entity<SystemConfig>().HasData(
                integrationConfigSeeds.Select(s => new SystemConfig
                {
                    Key = s.Key,
                    ValueType = s.Type,
                    Unit = s.Unit,
                    OwnerLevel = s.Owner,
                    Description = s.Description,
                    IsActive = true,
                    IsSecret = s.IsSecret
                }).ToArray()
            );

            modelBuilder.Entity<JobRun>(entity =>
            {
                entity.HasIndex(r => r.JobName);
                entity.HasIndex(r => r.StartedAt);
                entity.HasIndex(r => new { r.JobName, r.StartedAt });
                entity.Property(r => r.JobName).HasMaxLength(100);
                entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
                entity.Property(r => r.TriggerType).HasConversion<string>().HasMaxLength(20);
            });

            modelBuilder.Entity<WebhookLog>(entity =>
            {
                entity.HasIndex(w => w.Status);
                entity.HasIndex(w => w.ReceivedAt);
                entity.Property(w => w.Source).HasMaxLength(50);
                entity.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);
            });

            modelBuilder.Entity<Vehicle>(entity =>
            {
                entity.HasIndex(v => v.VehicleNumber).IsUnique();
                entity.Property(v => v.LicensePlate).HasMaxLength(20);
                // decimal? không bị vòng lặp SetPrecision(18,2) phía dưới bắt được (ClrType của property nullable
                // khác typeof(decimal)) -> khai báo rõ ràng ở đây để tránh SQL Server âm thầm cắt phần thập phân.
                entity.Property(v => v.Capacity).HasPrecision(18, 2);
            });

            modelBuilder.Entity<DiscountTier>(entity =>
            {
                entity.HasIndex(t => t.MinAmount);
                entity.Property(t => t.MaxAmount).HasPrecision(18, 2);
            });

            // Seed 5 xe khớp đúng "xe 1..5" đang dùng cứng trong OrderService/DeliveryController hôm nay
            // -> hành vi không đổi ngay sau migration, Admin chỉnh sửa dữ liệu thật từ đây về sau.
            modelBuilder.Entity<Vehicle>().HasData(
                new Vehicle { Id = Guid.Parse("f0000001-0001-4001-a001-000000000001"), VehicleNumber = 1, LicensePlate = "51C-000.01", IsActive = true },
                new Vehicle { Id = Guid.Parse("f0000001-0001-4001-a001-000000000002"), VehicleNumber = 2, LicensePlate = "51C-000.02", IsActive = true },
                new Vehicle { Id = Guid.Parse("f0000001-0001-4001-a001-000000000003"), VehicleNumber = 3, LicensePlate = "51C-000.03", IsActive = true },
                new Vehicle { Id = Guid.Parse("f0000001-0001-4001-a001-000000000004"), VehicleNumber = 4, LicensePlate = "51C-000.04", IsActive = true },
                new Vehicle { Id = Guid.Parse("f0000001-0001-4001-a001-000000000005"), VehicleNumber = 5, LicensePlate = "51C-000.05", IsActive = true }
            );

            // Seed khung chiết khấu khớp đúng if-chain cũ trong OrderService.CalculateDiscount (5/6/7/8%)
            // -> hành vi checkout không đổi ngay sau migration.
            modelBuilder.Entity<DiscountTier>().HasData(
                new DiscountTier { Id = Guid.Parse("f0000002-0002-4002-a002-000000000001"), MinAmount = 10_000_000m, MaxAmount = 31_000_000m, DiscountPercent = 0.05m, IsActive = true, Description = "10tr - <31tr: 5%" },
                new DiscountTier { Id = Guid.Parse("f0000002-0002-4002-a002-000000000002"), MinAmount = 31_000_000m, MaxAmount = 51_000_000m, DiscountPercent = 0.06m, IsActive = true, Description = "31tr - <51tr: 6%" },
                new DiscountTier { Id = Guid.Parse("f0000002-0002-4002-a002-000000000003"), MinAmount = 51_000_000m, MaxAmount = 71_000_000m, DiscountPercent = 0.07m, IsActive = true, Description = "51tr - <71tr: 7%" },
                new DiscountTier { Id = Guid.Parse("f0000002-0002-4002-a002-000000000004"), MinAmount = 71_000_000m, MaxAmount = 100_000_000m, DiscountPercent = 0.08m, IsActive = true, Description = "71tr - <100tr: 8%" }
            );

            modelBuilder.Entity<SystemConfigVersion>().HasData(
                systemConfigSeeds.Select(s => new SystemConfigVersion
                {
                    Id = s.VersionId,
                    ConfigKey = s.Key,
                    Value = s.Value,
                    EffectiveDate = configBaseDate,
                    CreatedAt = configBaseDate,
                    ActorEmail = "system-seed",
                    ChangeReason = "Khởi tạo giá trị mặc định theo business.md §7"
                }).ToArray()
            );
        }
    }
}
