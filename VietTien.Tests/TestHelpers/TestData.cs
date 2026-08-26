using VietTien.API.Data;
using VietTien.API.Models;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// Builder tạo dữ liệu mẫu hợp lệ mặc định, override từng field qua Action.
    /// Các hàm Seed* thêm thẳng vào DbContext và SaveChanges.
    /// </summary>
    public static class TestData
    {
        public static User User(Action<User>? mutate = null)
        {
            var u = new User
            {
                FullName = "Nguyễn Văn A",
                Email = $"user-{Guid.NewGuid():N}@test.com",
                PhoneNumber = $"09{Random.Shared.Next(10_000_000, 99_999_999)}", // unique để tránh va chạm lookup theo phone
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("P@ss123"),
                Role = SystemRole.Customer,
                IsEmailVerified = true,
            };
            mutate?.Invoke(u);
            return u;
        }

        public static CustomerProfile Profile(Guid userId, Action<CustomerProfile>? mutate = null)
        {
            var p = new CustomerProfile { UserId = userId };
            mutate?.Invoke(p);
            return p;
        }

        public static Category Category(Action<Category>? mutate = null)
        {
            var c = new Category { Name = "Ống nhựa" };
            mutate?.Invoke(c);
            return c;
        }

        public static Product Product(Guid categoryId, Action<Product>? mutate = null)
        {
            var p = new Product
            {
                CategoryId = categoryId,
                Name = "Ống PVC D21",
                Sku = $"SKU-{Guid.NewGuid():N}"[..12],
                StandardListedPrice = 50_000m,
                Unit = "Cái",
                IsDiscontinued = false,
            };
            mutate?.Invoke(p);
            return p;
        }

        public static Cart Cart(Guid customerProfileId, Action<Cart>? mutate = null)
        {
            var c = new Cart
            {
                CustomerProfileId = customerProfileId,
                UpdatedAt = DateTime.UtcNow,
            };
            mutate?.Invoke(c);
            return c;
        }

        public static CartItem CartItem(Guid cartId, Guid productId, int qty = 1, decimal unitPrice = 50_000m)
            => new CartItem { CartId = cartId, ProductId = productId, Quantity = qty, UnitPrice = unitPrice };

        public static Order Order(Guid customerProfileId, Action<Order>? mutate = null)
        {
            var o = new Order
            {
                CustomerProfileId = customerProfileId,
                OrderCode = $"VT{Random.Shared.Next(100000, 999999)}",
                TotalAmount = 5_000_000m,
                FinalPayment = 5_000_000m,
                PaymentMethod = PaymentMethod.SePay,
                PaymentStatus = PaymentStatus.Unpaid,
                OrderStatus = OrderStatus.PendingConfirmation,
            };
            mutate?.Invoke(o);
            return o;
        }

        public static OrderItem OrderItem(Guid orderId, Guid productId, int qty = 1, decimal price = 50_000m)
            => new OrderItem { OrderId = orderId, ProductId = productId, Quantity = qty, PriceSnapshot = price };

        public static (Warehouse warehouse, WarehouseLocation location) Warehouse(Action<Warehouse>? mutate = null)
        {
            var w = new Warehouse { Name = "Kho thành phẩm", Code = $"WH-{Guid.NewGuid():N}"[..10] };
            var loc = new WarehouseLocation { WarehouseId = w.Id, Name = "Khu A" };
            w.Locations.Add(loc);
            mutate?.Invoke(w);
            return (w, loc);
        }

        public static Inventory Inventory(Guid productId, Guid warehouseLocationId, int onHand, Action<Inventory>? mutate = null)
        {
            var inv = new Inventory
            {
                ProductId = productId,
                WarehouseLocationId = warehouseLocationId,
                OnHandQuantity = onHand,
            };
            mutate?.Invoke(inv);
            return inv;
        }

        /// <summary>Seed 1 khách hàng đầy đủ: User (verified) + CustomerProfile. Trả về (user, profile).</summary>
        public static (User user, CustomerProfile profile) SeedCustomer(ApplicationDbContext db, Action<User>? mutateUser = null)
        {
            var user = User(mutateUser);
            var profile = Profile(user.Id);
            db.Users.Add(user);
            db.CustomerProfiles.Add(profile);
            db.SaveChanges();
            return (user, profile);
        }

        /// <summary>Seed 1 sản phẩm kèm category. Trả về product.</summary>
        public static Product SeedProduct(ApplicationDbContext db, Action<Product>? mutate = null)
        {
            var cat = Category();
            db.Categories.Add(cat);
            var product = Product(cat.Id, mutate);
            db.Products.Add(product);
            db.SaveChanges();
            return product;
        }

        /// <summary>Seed kho + vị trí + tồn kho cho 1 sản phẩm. Trả về inventory.</summary>
        public static Inventory SeedInventory(ApplicationDbContext db, Guid productId, int onHand)
        {
            var (w, loc) = Warehouse();
            db.Warehouses.Add(w);
            var inv = Inventory(productId, loc.Id, onHand);
            db.Inventories.Add(inv);
            db.SaveChanges();
            return inv;
        }

        /// <summary>Seed 3 ca làm việc mặc định (Sáng/Trưa/Chiều) — cần thiết từ khi
        /// ScheduleDeliveryAsync/SchedulePickupAsync/CreateTripAsync đổi sang kiểm tra tên ca hợp lệ
        /// theo bảng WarehouseShifts thay vì mảng hardcode 3 chuỗi cố định (BUGFIX: Admin đổi tên ca
        /// không cập nhật cho các luồng xếp lịch xe).</summary>
        public static void SeedWarehouseShifts(ApplicationDbContext db)
        {
            db.WarehouseShifts.AddRange(
                new WarehouseShift { Name = "Sáng", StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(14, 0, 0) },
                new WarehouseShift { Name = "Trưa", StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(22, 0, 0) },
                new WarehouseShift { Name = "Chiều", StartTime = new TimeSpan(22, 0, 0), EndTime = new TimeSpan(6, 0, 0) }
            );
            db.SaveChanges();
        }

        // =====================================================================
        // Admin / Job / Marketing — bổ sung cho các sheet mới của doc L1 v2.2
        // =====================================================================

        /// <summary>
        /// Seed 1 khoá cấu hình kèm 1 phiên bản giá trị hiệu lực. EffectiveDate mặc định lùi 1 ngày
        /// để GetEffectiveValueAsync() ở thời điểm hiện tại luôn nhìn thấy giá trị này.
        /// </summary>
        public static SystemConfig SeedConfig(
            ApplicationDbContext db, string key, string value,
            SystemConfigValueType type = SystemConfigValueType.Int, DateTime? effectiveFrom = null)
        {
            var config = db.SystemConfigs.FirstOrDefault(c => c.Key == key);
            if (config == null)
            {
                config = new SystemConfig { Key = key, ValueType = type, OwnerLevel = "Admin" };
                db.SystemConfigs.Add(config);
            }

            db.SystemConfigVersions.Add(new SystemConfigVersion
            {
                ConfigKey = key,
                Value = value,
                EffectiveDate = effectiveFrom ?? DateTime.UtcNow.AddDays(-1),
                CreatedAt = effectiveFrom ?? DateTime.UtcNow.AddDays(-1),
            });
            db.SaveChanges();
            return config;
        }

        public static DiscountTier DiscountTier(decimal minAmount, decimal? maxAmount, decimal percent, Action<DiscountTier>? mutate = null)
        {
            var t = new DiscountTier { MinAmount = minAmount, MaxAmount = maxAmount, DiscountPercent = percent, IsActive = true };
            mutate?.Invoke(t);
            return t;
        }

        public static Vehicle Vehicle(int number, Action<Vehicle>? mutate = null)
        {
            var v = new Vehicle { VehicleNumber = number, LicensePlate = $"51C-{number:D5}", IsActive = true };
            mutate?.Invoke(v);
            return v;
        }

        public static WebhookLog WebhookLog(Action<WebhookLog>? mutate = null)
        {
            var log = new WebhookLog { Source = "SePay", RawPayload = "{}", Status = WebhookLogStatus.Received };
            mutate?.Invoke(log);
            return log;
        }

        public static AuditLog AuditLog(string entityName, string action, Action<AuditLog>? mutate = null)
        {
            var log = new AuditLog { EntityName = entityName, EntityId = Guid.NewGuid().ToString(), Action = action };
            mutate?.Invoke(log);
            return log;
        }

        public static JobRun JobRun(string jobName, Action<JobRun>? mutate = null)
        {
            var run = new JobRun { JobName = jobName, Status = JobRunStatus.Success, FinishedAt = DateTime.UtcNow };
            mutate?.Invoke(run);
            return run;
        }

        public static MarketingPost MarketingPost(Guid productId, Guid createdByUserId, Action<MarketingPost>? mutate = null)
        {
            var post = new MarketingPost
            {
                Code = $"MP-{Random.Shared.Next(100000, 999999)}",
                ProductId = productId,
                CreatedByUserId = createdByUserId,
                PromptUsed = "Giới thiệu ống nhựa PVC",
                GeneratedCaption = "Ống nhựa PVC chất lượng cao",
                EditedCaption = "Ống nhựa PVC chất lượng cao",
                SelectedImageUrl = "https://cdn/marketing.png",
                Status = MarketingPostStatus.Draft,
            };
            mutate?.Invoke(post);
            return post;
        }
    }
}
