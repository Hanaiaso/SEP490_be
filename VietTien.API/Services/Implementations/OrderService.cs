using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Cart;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.SePay;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class OrderService : IOrderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ICartService _cartService;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly ICloudinaryService _cloudinaryService;

        public OrderService(IUnitOfWork unitOfWork, ApplicationDbContext context, IConfiguration configuration, ICartService cartService, IEmailService emailService, INotificationService notificationService, ICloudinaryService cloudinaryService)
        {
            _unitOfWork = unitOfWork;
            _context = context;
            _configuration = configuration;
            _cartService = cartService;
            _emailService = emailService;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
        }

        private async Task<CustomerProfile> GetCustomerProfileAsync(Guid userId)
        {
            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            if (profile == null)
                throw new Exception("Customer Profile not found.");
            return profile;
        }

        private (decimal discountAmount, decimal discountPercentage) CalculateDiscount(decimal totalAmount)
        {
            if (totalAmount >= 100_000_000)
                throw new Exception("Đơn hàng trên 100 triệu vui lòng liên hệ NV Bán hàng để nhận báo giá B2B.");

            decimal discountPercentage = 0;
            if (totalAmount >= 71_000_000)
                discountPercentage = 0.08m; // 8%
            else if (totalAmount >= 51_000_000)
                discountPercentage = 0.07m; // 7%
            else if (totalAmount >= 31_000_000)
                discountPercentage = 0.06m; // 6%
            else if (totalAmount >= 10_000_000)
                discountPercentage = 0.05m; // 5%

            return (totalAmount * discountPercentage, discountPercentage);
        }

        public async Task<OrderPreviewDto> GetCheckoutSummaryAsync(Guid userId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cart = await _cartService.GetCartAsync(userId); 
            
            if (cart == null || !cart.Items.Any())
                throw new Exception("Giỏ hàng trống.");

            var baseTotal = cart.Items.Sum(i => i.TotalPrice);
            var (discountAmount, discountPercentage) = CalculateDiscount(baseTotal);

            var totalAfterDiscount = baseTotal - discountAmount;
            
            // VAT 10% sau chiết khấu nếu khách hàng có cấu hình MST trong hồ sơ
            var requiresVat = !string.IsNullOrEmpty(profile.TaxCode);
            decimal vatPercentage = requiresVat ? 0.10m : 0m;
            decimal vatAmount = totalAfterDiscount * vatPercentage;
            decimal finalPayment = totalAfterDiscount + vatAmount;

            return new OrderPreviewDto
            {
                TotalAmount = baseTotal,
                DiscountAmount = discountAmount,
                DiscountPercentage = discountPercentage * 100, 
                VatPercentage = vatPercentage * 100,
                VatAmount = vatAmount,
                FinalPayment = finalPayment,
                Items = cart.Items
            };
        }

        public async Task<OrderResponseDto> PlaceOrderAsync(Guid userId, PlaceOrderRequestDto request)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var cart = await _cartService.GetCartAsync(userId);
            var cartEntity = await _unitOfWork.Carts.GetCartByCustomerIdAsync(profile.Id);

            if (cart == null || !cart.Items.Any() || cartEntity == null)
                throw new Exception("Giỏ hàng trống.");

            var baseTotal = cart.Items.Sum(i => i.TotalPrice);
            var (discountAmount, discountPercentage) = CalculateDiscount(baseTotal);
            var totalAfterDiscount = baseTotal - discountAmount;
            decimal vatAmount = request.RequiresRedInvoice ? (totalAfterDiscount * 0.10m) : 0m;
            decimal finalPayment = totalAfterDiscount + vatAmount;

            // --- Xử lý áp dụng Credit ---
            decimal creditApplied = 0m;
            if (profile.AvailableCredit > 0)
            {
                creditApplied = Math.Min(finalPayment, profile.AvailableCredit);
                profile.AvailableCredit -= creditApplied;
                finalPayment -= creditApplied;
                
                // Cập nhật CustomerProfile
                _unitOfWork.Users.Update(profile.User); // Nếu Entity tracking, hoặc gọi phương thức repo update profile
            }

            var orderCode = $"VT{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
            
            var orderStatus = request.PaymentMethod == PaymentMethod.COD ? OrderStatus.PendingConfirmation : OrderStatus.Draft;
            var paymentStatus = PaymentStatus.Pending;
            var fulfillmentStatus = request.PaymentMethod == PaymentMethod.COD ? FulfillmentStatus.Reserved : FulfillmentStatus.Unallocated;

            // Nếu thanh toán toàn bộ bằng Credit
            if (finalPayment == 0)
            {
                paymentStatus = PaymentStatus.Paid;
                orderStatus = OrderStatus.Confirmed; // Bỏ qua Draft/PendingConfirmation vì đã trả đủ
                // Nếu là SePay/COD nhưng trả 100% credit thì coi như COD đã xác nhận hoặc SePay đã thanh toán
            }

            var order = new Order
            {
                CustomerProfileId = profile.Id,
                OrderCode = orderCode,
                TotalAmount = baseTotal,
                DiscountAmount = discountAmount,
                VatAmount = vatAmount,
                CreditApplied = creditApplied,
                FinalPayment = finalPayment,
                PaymentMethod = request.PaymentMethod,
                PaymentStatus = paymentStatus,
                OrderStatus = orderStatus,
                FulfillmentStatus = fulfillmentStatus,
                CreatedAt = DateTime.UtcNow,
                RequiresRedInvoice = request.RequiresRedInvoice,
                SalesStaffId = profile.AssignedSalesStaffId // Snapshot Sale phụ trách tại thời điểm tạo đơn (LUỒNG 7)
            };

            foreach (var item in cart.Items)
            {
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    PriceSnapshot = item.UnitPrice,
                    CostSnapshot = 0 
                });
            }

            await _unitOfWork.Orders.CreateOrderAsync(order);
            await _unitOfWork.Carts.ClearCartAsync(cartEntity.Id);
            await _unitOfWork.SaveChangesAsync();

            if (creditApplied > 0)
            {
                _context.CreditTransactions.Add(new CreditTransaction
                {
                    CustomerProfileId = profile.Id,
                    Amount = -creditApplied,
                    Description = $"Thanh toán cho đơn hàng {order.OrderCode}",
                    OrderId = order.Id,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            // Notify assigned sales staff (SYS-02)
            if (profile.AssignedSalesStaffId.HasValue)
            try
            {
                // Notify assigned sales staff (SYS-02)
                if (profile.AssignedSalesStaffId.HasValue)
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_02_NewOrder,
                        profile.AssignedSalesStaffId.Value,
                        $"Đơn hàng mới {order.OrderCode}",
                        $"Khách hàng {profile.User.FullName} vừa đặt đơn hàng trị giá {order.FinalPayment:N0}đ.",
                        order.Id,
                        "Order"
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OrderService] Error sending new order notification: {ex.Message}");
            }

            return new OrderResponseDto
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                FinalPayment = order.FinalPayment,
                PaymentMethod = order.PaymentMethod,
                SePayQrUrl = null // Lấy QR qua API khác
            };
        }

        public async Task<SePayQrResponseDto> GenerateSePayQrAsync(Guid orderId)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");

            var bankAccount = _configuration["SePaySettings:BankAccount"];
            var bankId = _configuration["SePaySettings:BankId"];
            
            var qrUrl = $"https://qr.sepay.vn/img?acc={bankAccount}&bank={bankId}&amount={(int)order.FinalPayment}&des={order.OrderCode}";

            return new SePayQrResponseDto
            {
                QrImageUrl = qrUrl,
                TransferContent = order.OrderCode
            };
        }

        public async Task ProcessSePayWebhookAsync(SePayWebhookDto payload, string providedToken)
        {
            var apiToken = _configuration["SePaySettings:ApiToken"];
            var isDevelopment = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
            if (providedToken != apiToken)
            {
                if (isDevelopment && string.IsNullOrEmpty(providedToken))
                {
                    Console.WriteLine("[SePay Webhook] WARNING: Token is missing but bypassed because ASPNETCORE_ENVIRONMENT is Development.");
                }
                else
                {
                    throw new UnauthorizedAccessException("Token không hợp lệ.");
                }
            }

            var transferContentText = !string.IsNullOrEmpty(payload.content) ? payload.content : payload.transferContent;
            var orderCode = ExtractOrderCode(transferContentText);
            if (string.IsNullOrEmpty(orderCode)) return;

            var order = await _unitOfWork.Orders.GetOrderByCodeAsync(orderCode);
            if (order == null || order.PaymentStatus == PaymentStatus.Paid) return;

            if (payload.transferAmount >= order.FinalPayment)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                if (order.OrderCode.StartsWith("VT-DT-", StringComparison.OrdinalIgnoreCase))
                {
                    order.OrderStatus = OrderStatus.Completed;
                }
                
                var refCode = !string.IsNullOrEmpty(payload.referenceCode) ? payload.referenceCode : payload.referenceNumber;
                var transaction = new PaymentTransaction
                {
                    OrderId = order.Id,
                    TransactionId = !string.IsNullOrEmpty(refCode) ? refCode : Guid.NewGuid().ToString(),
                    Amount = payload.transferAmount,
                    AccountNumber = payload.accountNumber,
                    ReferenceCode = transferContentText,
                    IsSuccess = true,
                    Timestamp = DateTime.UtcNow
                };

                await _context.PaymentTransactions.AddAsync(transaction);
                await _unitOfWork.Orders.UpdateOrderAsync(order);
                await _unitOfWork.SaveChangesAsync();

                // Load full details for email invoice notification
                try
                {
                    var fullOrder = await _context.Orders
                        .Include(o => o.CustomerProfile)
                        .ThenInclude(c => c.User)
                        .Include(o => o.OrderItems)
                        .ThenInclude(oi => oi.Product)
                        .FirstOrDefaultAsync(o => o.Id == order.Id);

                    if (fullOrder != null)
                    {
                        // Send confirmation email to Customer
                        var customerEmail = fullOrder.CustomerProfile?.User?.Email ?? fullOrder.CustomerProfile?.InvoiceEmail;
                        var customerName = fullOrder.CustomerProfile?.Representative ?? fullOrder.CustomerProfile?.CompanyName ?? "Khách hàng";
                        if (!string.IsNullOrEmpty(customerEmail))
                        {
                            await _emailService.SendOrderInvoiceEmailAsync(customerEmail, customerName, fullOrder, isSalesNotify: false);
                        }

                        // Send notification email to Sales/Admin
                        var salesEmail = _configuration["EmailSettings:SenderEmail"] ?? "sales@viettien.vn";
                        await _emailService.SendOrderInvoiceEmailAsync(salesEmail, "Bộ phận Bán hàng VietTien", fullOrder, isSalesNotify: true);

                        // Notify assigned sales staff (SYS-05)
                        if (fullOrder.CustomerProfile?.AssignedSalesStaffId != null)
                        {
                            await _notificationService.CreateNotificationAsync(
                                NotificationType.SYS_05_SePaySuccess,
                                fullOrder.CustomerProfile.AssignedSalesStaffId.Value,
                                $"Thanh toán thành công {fullOrder.OrderCode}",
                                $"Khách hàng đã thanh toán {payload.transferAmount:N0}đ qua SePay cho đơn hàng {fullOrder.OrderCode}.",
                                fullOrder.Id,
                                "Order"
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SePay Webhook] Error sending invoice emails: {ex.Message}");
                }
                
                // TODO: Trigger event xuống kho chuẩn bị đơn hàng (FIFO)
            }
        }

        public async Task<PaymentStatusResponseDto> GetPaymentStatusAsync(Guid orderId)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");

            return new PaymentStatusResponseDto
            {
                Status = order.PaymentStatus.ToString()
            };
        }

        public async Task<DirectOrderResponseDto> PlaceDirectOrderAsync(PlaceDirectOrderRequestDto request)
        {
            if (request.Items == null || !request.Items.Any())
            {
                throw new Exception("Danh sách sản phẩm trống.");
            }

            var phone = request.PhoneNumber?.Trim();
            if (string.IsNullOrEmpty(phone))
            {
                phone = "0000000000";
            }

            CustomerProfile? customerProfile = null;
            var existingUser = await _context.Users
                .Include(u => u.CustomerProfile)
                .FirstOrDefaultAsync(u => u.PhoneNumber == phone);

            if (existingUser != null)
            {
                customerProfile = existingUser.CustomerProfile;
                if (customerProfile == null)
                {
                    customerProfile = new CustomerProfile
                    {
                        UserId = existingUser.Id,
                        Representative = request.CustomerName,
                        CompanyName = request.CustomerName,
                        CompanyAddress = request.Address
                    };
                    await _unitOfWork.Users.AddCustomerProfileAsync(customerProfile);
                    await _unitOfWork.SaveChangesAsync();
                }
            }
            else
            {
                var guidStr = Guid.NewGuid().ToString("N").Substring(0, 8);
                var newUser = new User
                {
                    FullName = request.CustomerName,
                    PhoneNumber = phone,
                    Email = $"guest_{phone}_{guidStr}@store.viettien.vn",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                    Role = SystemRole.Customer,
                    IsEmailVerified = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Users.AddAsync(newUser);
                await _unitOfWork.SaveChangesAsync();

                customerProfile = new CustomerProfile
                {
                    UserId = newUser.Id,
                    Representative = request.CustomerName,
                    CompanyName = request.CustomerName,
                    CompanyAddress = request.Address
                };
                await _unitOfWork.Users.AddCustomerProfileAsync(customerProfile);
                await _unitOfWork.SaveChangesAsync();
            }

            var orderCode = $"VT-DT-{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";

            string? pdfUrl = null;
            if (!string.IsNullOrEmpty(request.InvoicePdfBase64))
            {
                try
                {
                    var base64Data = request.InvoicePdfBase64;
                    if (base64Data.Contains(","))
                    {
                        base64Data = base64Data.Split(',')[1];
                    }

                    var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var invoicesPath = Path.Combine(wwwrootPath, "invoices");
                    if (!Directory.Exists(invoicesPath))
                    {
                        Directory.CreateDirectory(invoicesPath);
                    }

                    var filePath = Path.Combine(invoicesPath, $"{orderCode}.pdf");
                    var fileBytes = Convert.FromBase64String(base64Data);
                    await File.WriteAllBytesAsync(filePath, fileBytes);
                    
                    pdfUrl = $"/invoices/{orderCode}.pdf";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving PDF invoice: {ex.Message}");
                }
            }

            var order = new Order
            {
                CustomerProfileId = customerProfile.Id,
                OrderCode = orderCode,
                TotalAmount = request.TotalAmount,
                DiscountAmount = request.DiscountAmount,
                VatAmount = request.VatAmount,
                FinalPayment = request.FinalPayment,
                PaymentMethod = request.PaymentMethod,
                PaymentStatus = request.PaymentMethod == PaymentMethod.Cash ? PaymentStatus.Paid : PaymentStatus.Pending,
                OrderStatus = request.PaymentMethod == PaymentMethod.COD ? OrderStatus.PendingConfirmation : OrderStatus.Draft,
                FulfillmentStatus = request.PaymentMethod == PaymentMethod.COD ? FulfillmentStatus.Reserved : FulfillmentStatus.Unallocated,
                IsExternalOrder = true,
                CreatedAt = DateTime.UtcNow,
                RequiresRedInvoice = false,
                InvoicePdfUrl = pdfUrl,
                SalesStaffId = customerProfile.AssignedSalesStaffId // Snapshot Sale phụ trách tại thời điểm tạo đơn (LUỒNG 7)
            };

            foreach (var item in request.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product == null)
                {
                    throw new Exception($"Không tìm thấy sản phẩm với ID: {item.ProductId}");
                }

                var inventory = await _context.Inventories.FirstOrDefaultAsync(i => i.ProductId == item.ProductId);
                if (inventory == null)
                {
                    throw new Exception($"Không tìm thấy thông tin kho cho sản phẩm {product.Name}.");
                }

                if (inventory.AvailableQuantity < item.Quantity)
                {
                    throw new Exception("Stock depleted by another transaction. Please refresh inventory data.");
                }

                inventory.OnHandQuantity -= item.Quantity;

                order.OrderItems.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    PriceSnapshot = item.Price,
                    CostSnapshot = 0
                });
            }

            try
            {
                await _unitOfWork.Orders.CreateOrderAsync(order);
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new Exception("Stock depleted by another transaction. Please refresh inventory data.");
            }

            return new DirectOrderResponseDto
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                FinalPayment = order.FinalPayment,
                InvoicePdfUrl = order.InvoicePdfUrl
            };
        }

        private string? ExtractOrderCode(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(VT-?DT-?\d+|VT\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value.ToUpper() : null;
        }

        public async Task<SalesDashboardStatsDto> GetSalesDashboardStatsAsync()
        {
            var today = DateTime.UtcNow.Date;

            // 1. KPI aggregates
            var allOrders = await _context.Orders.ToListAsync();
            var newOrdersCount = allOrders.Count(o => o.OrderStatus == OrderStatus.Draft);
            var processingOrdersCount = allOrders.Count(o => o.OrderStatus == OrderStatus.Draft || o.OrderStatus == OrderStatus.Confirmed);
            var shippingOrdersCount = allOrders.Count(o => o.OrderStatus == OrderStatus.Processing);
            var deliveredTodayCount = allOrders.Count(o => o.OrderStatus == OrderStatus.Completed && o.CreatedAt.Date == today);
            var revenueToday = allOrders.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt.Date == today).Sum(o => o.FinalPayment);
            var pendingDebt = allOrders.Where(o => o.PaymentStatus == PaymentStatus.Pending).Sum(o => o.FinalPayment);

            var kpiDto = new DashboardKpiDto
            {
                NewOrdersCount = newOrdersCount,
                ProcessingOrdersCount = processingOrdersCount,
                ShippingOrdersCount = shippingOrdersCount,
                DeliveredTodayCount = deliveredTodayCount,
                RevenueToday = revenueToday,
                PendingDebt = pendingDebt
            };

            // 2. Recent orders (top 10)
            var recentOrdersList = await _context.Orders
                .Include(o => o.CustomerProfile)
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .Select(o => new DashboardOrderDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    CustomerName = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách lẻ",
                    CreatedAt = o.CreatedAt,
                    FinalPayment = o.FinalPayment,
                    PaymentMethod = o.PaymentMethod,
                    PaymentStatus = o.PaymentStatus,
                    OrderStatus = o.OrderStatus,
                    InvoicePdfUrl = o.InvoicePdfUrl
                })
                .ToListAsync();

            // 3. Urgent orders (top 5 oldest pending orders)
            var urgentOrdersList = await _context.Orders
                .Include(o => o.CustomerProfile)
                .Where(o => o.OrderStatus == OrderStatus.Draft || o.OrderStatus == OrderStatus.Confirmed)
                .OrderBy(o => o.CreatedAt)
                .Take(5)
                .ToListAsync();

            var urgentDtos = new List<DashboardUrgentOrderDto>();
            foreach (var o in urgentOrdersList)
            {
                var timeElapsed = DateTime.UtcNow - o.CreatedAt;
                string deadlineText;
                string level;

                if (timeElapsed.TotalMinutes < 60)
                {
                    deadlineText = $"{(int)timeElapsed.TotalMinutes} phút trước";
                    level = "normal";
                }
                else if (timeElapsed.TotalHours < 2)
                {
                    deadlineText = $"{(int)timeElapsed.TotalHours} giờ trước";
                    level = "high";
                }
                else
                {
                    deadlineText = $"{(int)timeElapsed.TotalHours} giờ trước";
                    level = "critical";
                }

                urgentDtos.Add(new DashboardUrgentOrderDto
                {
                    Id = o.OrderCode,
                    Customer = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách lẻ",
                    Amount = o.FinalPayment,
                    Deadline = deadlineText,
                    Level = level
                });
            }

            // 4. Warehouse Queue (top 5 packing orders)
            var warehouseList = await _context.Orders
                .Include(o => o.CustomerProfile)
                .Include(o => o.OrderItems)
                .Where(o => o.OrderStatus == OrderStatus.Draft || o.OrderStatus == OrderStatus.Confirmed)
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Select(o => new DashboardWarehouseQueueDto
                {
                    Id = o.OrderCode,
                    Customer = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách lẻ",
                    ItemsCount = o.OrderItems.Sum(oi => oi.Quantity),
                    Status = o.OrderStatus == OrderStatus.Draft ? "Chờ xác nhận" : "Đang đóng gói"
                })
                .ToListAsync();

            // 5. Quote requests (top 5 pending/negotiating quotations)
            var quotationsList = await _context.Quotations
                .Include(q => q.CustomerProfile)
                .Where(q => q.Status == QuotationStatus.Draft || q.Status == QuotationStatus.Negotiating)
                .OrderByDescending(q => q.RequestDate)
                .Take(5)
                .Select(q => new DashboardQuoteRequestDto
                {
                    Id = q.Id,
                    CustomerName = q.CustomerProfile.Representative ?? q.CustomerProfile.CompanyName ?? "Khách hàng",
                    Value = q.OriginalTotal,
                    RequestDate = q.RequestDate
                })
                .ToListAsync();

            // 6. Top Products (top 5 by revenue)
            var topProductsList = await _context.OrderItems
                .Include(oi => oi.Product)
                .Where(oi => oi.Order.PaymentStatus == PaymentStatus.Paid)
                .GroupBy(oi => new { oi.ProductId, oi.Product.Name })
                .Select(g => new DashboardTopProductDto
                {
                    Name = g.Key.Name,
                    Revenue = g.Sum(oi => oi.Quantity * oi.PriceSnapshot) / 1000000m // convert to triệu đồng
                })
                .OrderByDescending(p => p.Revenue)
                .Take(5)
                .ToListAsync();

            // 7. Last 7 Days Revenue
            var revenueDays = new List<DashboardRevenueDayDto>();
            var sevenDaysAgo = today.AddDays(-6);
            var paidOrdersIn7Days = await _context.Orders
                .Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt >= sevenDaysAgo)
                .ToListAsync();

            var culture = new System.Globalization.CultureInfo("vi-VN");
            for (int i = 6; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                var dayName = culture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek);
                if (d.Date == today) dayName = "Hôm nay";

                var dailyRevenue = paidOrdersIn7Days
                    .Where(o => o.CreatedAt.Date == d.Date)
                    .Sum(o => o.FinalPayment) / 1000000m; // convert to triệu đồng

                revenueDays.Add(new DashboardRevenueDayDto
                {
                    Day = dayName,
                    Revenue = Math.Round(dailyRevenue, 2),
                    Target = 5.0m // Giả định target 5 triệu/ngày
                });
            }

            return new SalesDashboardStatsDto
            {
                Kpi = kpiDto,
                RecentOrders = recentOrdersList,
                UrgentOrders = urgentDtos,
                WarehouseQueue = warehouseList,
                QuoteRequests = quotationsList,
                TopProducts = topProductsList,
                Last7DaysRevenue = revenueDays
            };
        }

        public async Task ConfirmDirectOrderPaymentAsync(Guid orderId)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");

            order.PaymentStatus = PaymentStatus.Paid;
            order.OrderStatus = OrderStatus.Completed; // Delivered upon counter cash payment confirmation

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task UploadInvoicePdfAsync(Guid orderId, string pdfBase64)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");

            if (!string.IsNullOrEmpty(pdfBase64))
            {
                try
                {
                    var base64Data = pdfBase64;
                    if (base64Data.Contains(","))
                    {
                        base64Data = base64Data.Split(',')[1];
                    }

                    var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var invoicesPath = Path.Combine(wwwrootPath, "invoices");
                    if (!Directory.Exists(invoicesPath))
                    {
                        Directory.CreateDirectory(invoicesPath);
                    }

                    var filePath = Path.Combine(invoicesPath, $"{order.OrderCode}.pdf");
                    var fileBytes = Convert.FromBase64String(base64Data);
                    await File.WriteAllBytesAsync(filePath, fileBytes);

                    order.InvoicePdfUrl = $"/invoices/{order.OrderCode}.pdf";
                    await _unitOfWork.Orders.UpdateOrderAsync(order);
                    await _unitOfWork.SaveChangesAsync();

                    // If COD, send email immediately after uploading the invoice
                    if (order.PaymentMethod == PaymentMethod.COD)
                    {
                        var fullOrder = await _context.Orders
                            .Include(o => o.CustomerProfile)
                            .ThenInclude(c => c.User)
                            .Include(o => o.OrderItems)
                            .ThenInclude(oi => oi.Product)
                            .FirstOrDefaultAsync(o => o.Id == order.Id);

                        if (fullOrder != null)
                        {
                            var customerEmail = fullOrder.CustomerProfile?.User?.Email ?? fullOrder.CustomerProfile?.InvoiceEmail;
                            var customerName = fullOrder.CustomerProfile?.Representative ?? fullOrder.CustomerProfile?.CompanyName ?? "Khách hàng";
                            if (!string.IsNullOrEmpty(customerEmail))
                            {
                                await _emailService.SendOrderInvoiceEmailAsync(customerEmail, customerName, fullOrder, isSalesNotify: false);
                            }

                            var salesEmail = _configuration["EmailSettings:SenderEmail"] ?? "sales@viettien.vn";
                            await _emailService.SendOrderInvoiceEmailAsync(salesEmail, "Bộ phận Bán hàng VietTien", fullOrder, isSalesNotify: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error uploading/saving PDF invoice: {ex.Message}");
                    throw;
                }
            }
        }


        public async Task<PagedResultDto<OrderHistoryItemDto>> GetOrderHistoryAsync(
            Guid userId, OrderHistoryQueryDto query)
        {
            var profile = await GetCustomerProfileAsync(userId);

            var pageSize = Math.Max(1, Math.Min(query.PageSize, 50));
            var page     = Math.Max(1, query.Page);

            var (items, totalCount) = await _unitOfWork.Orders.GetOrderHistoryAsync(profile.Id, query);

            const int vatWindowHours = 168;

            var dtos = items.Select(o =>
            {
                var vatDeadline  = o.CreatedAt.AddHours(vatWindowHours);
                var canRequestVat = o.OrderStatus == OrderStatus.Completed
                                    && DateTime.UtcNow < vatDeadline
                                    && o.RedInvoiceStatus == RedInvoiceStatus.None;

                return new OrderHistoryItemDto
                {
                    Id               = o.Id,
                    OrderCode        = o.OrderCode,
                    CreatedAt        = o.CreatedAt,
                    TotalAmount      = o.TotalAmount,
                    DiscountAmount   = o.DiscountAmount,
                    VatAmount        = o.VatAmount,
                    FinalPayment     = o.FinalPayment,
                    PaymentMethod    = o.PaymentMethod.ToString(),
                    PaymentStatus    = o.PaymentStatus.ToString(),
                    OrderStatus      = o.OrderStatus.ToString(),
                    RedInvoiceStatus = o.RedInvoiceStatus.ToString(),
                    ItemCount        = o.OrderItems.Sum(oi => oi.Quantity),
                    HasInvoicePdf    = !string.IsNullOrEmpty(o.InvoicePdfUrl),
                    CanRequestVat    = canRequestVat,
                };
            }).ToList();

            return new PagedResultDto<OrderHistoryItemDto>
            {
                Items      = dtos,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };
        }


        public async Task<SpendingStatsDto> GetSpendingStatsAsync(Guid userId, string period)
        {
            var profile = await GetCustomerProfileAsync(userId);

            // Map kỳ thống kê -> số ngày lùi lại từ hiện tại
            var days = (period?.Trim().ToLower()) switch
            {
                "week"    => 7,
                "quarter" => 90,
                "year"    => 365,
                _         => 30,   // month (mặc định)
            };
            var fromUtc = DateTime.UtcNow.AddDays(-days);

            var orders = await _unitOfWork.Orders.GetPaidOrdersForStatsAsync(profile.Id, fromUtc);

            // Chi tiêu theo tháng (sắp xếp tăng dần theo thời gian)
            var spendingByMonth = orders
                .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new SpendingPointDto
                {
                    Label = $"T{g.Key.Month}",
                    Value = g.Sum(o => o.FinalPayment)
                })
                .ToList();

            // Top 5 sản phẩm theo tổng số lượng đặt
            var topProducts = orders
                .SelectMany(o => o.OrderItems)
                .Where(oi => oi.Product != null)
                .GroupBy(oi => oi.Product.Name)
                .Select(g => new TopProductDto { Name = g.Key, Value = g.Sum(oi => oi.Quantity) })
                .OrderByDescending(p => p.Value)
                .Take(5)
                .ToList();

            return new SpendingStatsDto
            {
                TotalOrders     = orders.Count,
                TotalSpent      = orders.Sum(o => o.FinalPayment),
                VatInvoiceCount = orders.Count(o => o.RedInvoiceStatus != RedInvoiceStatus.None),
                TopProductName  = topProducts.FirstOrDefault()?.Name ?? "Chưa có dữ liệu",
                SpendingByMonth = spendingByMonth,
                TopProducts     = topProducts,
            };
        }


        public async Task<OrderHistoryDetailDto> GetOrderDetailForCustomerAsync(Guid userId, Guid orderId)
        {
            
            var profile = await GetCustomerProfileAsync(userId);

            var order = await _unitOfWork.Orders.GetOrderDetailForCustomerAsync(orderId, profile.Id);
            if (order == null)
                throw new KeyNotFoundException("Không tìm thấy đơn hàng hoặc bạn không có quyền xem đơn này.");

            const int vatWindowHours = 168;
            var vatDeadline  = order.CreatedAt.AddHours(vatWindowHours);
            var canRequestVat = order.OrderStatus == OrderStatus.Completed
                                && DateTime.UtcNow < vatDeadline
                                && order.RedInvoiceStatus == RedInvoiceStatus.None;

           
            var itemDtos = order.OrderItems.Select(oi => new OrderItemDetailDto
            {
                ProductId    = oi.ProductId,
                ProductName  = oi.Product?.Name ?? "(Sản phẩm đã bị xóa)",
                ProductSku   = oi.Product?.Sku  ?? string.Empty,
                ProductImage = oi.Product?.ImageUrl,
                Quantity     = oi.Quantity,
                PriceSnapshot = oi.PriceSnapshot,
                LineTotal    = oi.PriceSnapshot * oi.Quantity,
            }).ToList();

            var defaultAddress = order.CustomerProfile?.Addresses?.FirstOrDefault(a => a.IsDefault) 
                                 ?? order.CustomerProfile?.Addresses?.FirstOrDefault();

            string? addressString = null;
            if (defaultAddress != null)
            {
                addressString = $"{defaultAddress.SpecificAddress}, {defaultAddress.Ward}, {defaultAddress.District}, {defaultAddress.City}";
            }
            else if (!string.IsNullOrEmpty(order.CustomerProfile?.CompanyAddress))
            {
                addressString = order.CustomerProfile.CompanyAddress;
            }

            var customerName = order.CustomerProfile?.User?.FullName 
                               ?? order.CustomerProfile?.CompanyName 
                               ?? "Khách hàng";
                               
            var customerPhone = order.CustomerProfile?.User?.PhoneNumber 
                                ?? order.CustomerProfile?.CompanyPhone 
                                ?? "---";

            return new OrderHistoryDetailDto
            {
                Id               = order.Id,
                OrderCode        = order.OrderCode,
                CreatedAt        = order.CreatedAt,
                TotalAmount      = order.TotalAmount,
                DiscountAmount   = order.DiscountAmount,
                VatAmount        = order.VatAmount,
                FinalPayment     = order.FinalPayment,
                PaymentMethod    = order.PaymentMethod.ToString(),
                PaymentStatus    = order.PaymentStatus.ToString(),
                OrderStatus      = order.OrderStatus.ToString(),
                RedInvoiceStatus = order.RedInvoiceStatus.ToString(),
                
                CustomerName     = customerName,
                CustomerPhone    = customerPhone,
                ShippingAddress  = addressString ?? "---",
                
                DeliveryShift    = order.DeliveryShift,
                DeliveryVehicleId = order.DeliveryVehicleId,
                DeliveryStatus   = order.DeliveryStatus.ToString(),
                ScheduledDeliveryDate = order.ScheduledDeliveryDate,
                DeliveredAt      = order.DeliveredAt,
                CustomerSignatureUrl = order.CustomerSignatureUrl,
                DeliveryPhotoUrl  = order.DeliveryPhotoUrl,
                FailedDeliveryCount = order.FailedDeliveryCount,
                AmountPaid       = order.AmountPaid,

                InvoicePdfUrl    = order.InvoicePdfUrl,
                CanRequestVat    = canRequestVat,
                VatDeadline      = canRequestVat ? vatDeadline : null,
                Items            = itemDtos,
            };
        }

        public async Task RequestVatInvoiceAsync(Guid userId, Guid orderId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var order   = await _unitOfWork.Orders.GetOrderDetailForCustomerAsync(orderId, profile.Id);

            if (order == null)
                throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus != OrderStatus.Completed)
                throw new InvalidOperationException("Chỉ đơn hàng đã giao thành công mới được yêu cầu hóa đơn VAT.");

            const int vatWindowHours = 168;
            if (DateTime.UtcNow > order.CreatedAt.AddHours(vatWindowHours))
                throw new InvalidOperationException("Đã quá thời hạn 7 ngày để yêu cầu hóa đơn VAT.");

            if (order.RedInvoiceStatus != RedInvoiceStatus.None)
                throw new InvalidOperationException("Đơn hàng này đã có yêu cầu hóa đơn VAT trước đó.");

            order.RequiresRedInvoice = true;
            order.RedInvoiceStatus   = RedInvoiceStatus.Pending;

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<PagedResultDto<SalesOrderListDto>> GetSalesOrdersAsync(SalesOrderQueryDto query, Guid? salesStaffId = null)
        {
            var queryable = _context.Orders
                .Include(o => o.CustomerProfile)
                .ThenInclude(cp => cp.User)
                .AsQueryable();

            if (salesStaffId.HasValue)
            {
                // Lọc theo snapshot Sale trên đơn (không theo chủ khách hiện tại):
                // đơn được "giữ lại" cho Sale cũ sau khi đổi Sale vẫn hiển thị đúng người xử lý
                queryable = queryable.Where(o => o.SalesStaffId == salesStaffId.Value);
            }

            if (!string.IsNullOrEmpty(query.SearchQuery))
            {
                var lowerSearch = query.SearchQuery.ToLower();
                queryable = queryable.Where(o => o.OrderCode.ToLower().Contains(lowerSearch) 
                                              || (o.CustomerProfile.Representative != null && o.CustomerProfile.Representative.ToLower().Contains(lowerSearch))
                                              || (o.CustomerProfile.CompanyName != null && o.CustomerProfile.CompanyName.ToLower().Contains(lowerSearch)));
            }

            if (!string.IsNullOrEmpty(query.Status) && query.Status != "all")
            {
                if (Enum.TryParse<OrderStatus>(query.Status, true, out var orderStatus))
                {
                    queryable = queryable.Where(o => o.OrderStatus == orderStatus);
                }
            }

            if (!string.IsNullOrEmpty(query.PaymentMethod) && query.PaymentMethod != "all")
            {
                if (Enum.TryParse<PaymentMethod>(query.PaymentMethod, true, out var paymentMethod))
                {
                    queryable = queryable.Where(o => o.PaymentMethod == paymentMethod);
                }
            }

            var totalCount = await queryable.CountAsync();
            var items = await queryable
                .OrderByDescending(o => o.CreatedAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(o => new SalesOrderListDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    CustomerName = o.CustomerProfile.Representative ?? o.CustomerProfile.CompanyName ?? "Khách hàng",
                    CreatedAt = o.CreatedAt,
                    FinalPayment = o.FinalPayment,
                    PaymentMethod = o.PaymentMethod.ToString(),
                    PaymentStatus = o.PaymentStatus.ToString(),
                    OrderStatus = o.OrderStatus.ToString(),
                    FulfillmentStatus = o.FulfillmentStatus.ToString(),
                    ShippingAddress = o.CustomerProfile.Addresses.Where(a => a.IsDefault)
                        .Select(a => a.SpecificAddress + ", " + a.Ward + ", " + a.District + ", " + a.City)
                        .FirstOrDefault() ?? o.CustomerProfile.CompanyAddress ?? "---",
                    TotalQuantity = o.OrderItems.Sum(i => i.Quantity),
                    PickingStartedAt = o.PickingStartedAt,
                    PickingCompletedAt = o.PickingCompletedAt,
                    InvoicePdfUrl = o.InvoicePdfUrl
                })
                .ToListAsync();

            return new PagedResultDto<SalesOrderListDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / query.PageSize)
            };
        }

        public async Task<SalesOrderDetailDto> GetSalesOrderDetailAsync(Guid orderId)
        {
            var order = await _context.Orders
                .Include(o => o.CustomerProfile)
                .ThenInclude(cp => cp.User)
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
                .Include(o => o.CustomerProfile.Addresses)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            var defaultAddress = order.CustomerProfile.Addresses?.FirstOrDefault(a => a.IsDefault) 
                                 ?? order.CustomerProfile.Addresses?.FirstOrDefault();

            string addressString = "---";
            if (defaultAddress != null)
                addressString = $"{defaultAddress.SpecificAddress}, {defaultAddress.Ward}, {defaultAddress.District}, {defaultAddress.City}";
            else if (!string.IsNullOrEmpty(order.CustomerProfile.CompanyAddress))
                addressString = order.CustomerProfile.CompanyAddress;

            return new SalesOrderDetailDto
            {
                Id = order.Id,
                OrderCode = order.OrderCode,
                CustomerName = order.CustomerProfile.Representative ?? order.CustomerProfile.CompanyName ?? "Khách hàng",
                CustomerPhone = order.CustomerProfile.User?.PhoneNumber ?? order.CustomerProfile.CompanyPhone ?? "---",
                CustomerEmail = order.CustomerProfile.User?.Email,
                CompanyName = order.CustomerProfile.CompanyName,
                ShippingAddress = addressString,
                CreatedAt = order.CreatedAt,
                TotalAmount = order.TotalAmount,
                DiscountAmount = order.DiscountAmount,
                VatAmount = order.VatAmount,
                FinalPayment = order.FinalPayment,
                PaymentMethod = order.PaymentMethod.ToString(),
                PaymentStatus = order.PaymentStatus.ToString(),
                OrderStatus = order.OrderStatus.ToString(),
                FulfillmentStatus = order.FulfillmentStatus.ToString(),
                DeliveryStatus = order.DeliveryStatus.ToString(),
                DeliveryVehicleId = order.DeliveryVehicleId,
                DeliveryShift = order.DeliveryShift,
                ScheduledDeliveryDate = order.ScheduledDeliveryDate,
                DeliveredAt = order.DeliveredAt,
                CustomerSignatureUrl = order.CustomerSignatureUrl,
                DeliveryPhotoUrl = order.DeliveryPhotoUrl,
                FailedDeliveryCount = order.FailedDeliveryCount,
                AmountPaid = order.AmountPaid,
                PickingStartedAt = order.PickingStartedAt,
                PickingCompletedAt = order.PickingCompletedAt,
                InvoicePdfUrl = order.InvoicePdfUrl,
                Items = order.OrderItems.Select(oi => new SalesOrderItemDto
                {
                    ProductId = oi.ProductId,
                    ProductName = oi.Product?.Name ?? "(Sản phẩm đã bị xóa)",
                    ProductSku = oi.Product?.Sku ?? string.Empty,
                    ProductImageUrl = oi.Product?.ImageUrl,
                    Quantity = oi.Quantity,
                    PriceSnapshot = oi.PriceSnapshot,
                    LineTotal = oi.PriceSnapshot * oi.Quantity
                }).ToList()
            };
        }

        public async Task ConfirmOrderAsync(Guid orderId, Guid salesStaffId)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus != OrderStatus.Draft && order.OrderStatus != OrderStatus.PendingConfirmation)
                throw new InvalidOperationException("Chỉ đơn hàng ở trạng thái 'Mới' hoặc 'Chờ xác nhận' mới có thể được xác nhận.");

            if (order.PaymentMethod == PaymentMethod.SePay && order.PaymentStatus != PaymentStatus.Paid)
                throw new InvalidOperationException("Đơn hàng SePay phải được thanh toán trước khi xác nhận.");

            order.OrderStatus = OrderStatus.Confirmed;
            if (order.FulfillmentStatus == FulfillmentStatus.Reserved || order.FulfillmentStatus == FulfillmentStatus.Unallocated)
            {
                order.FulfillmentStatus = FulfillmentStatus.Allocated;
                
                // --- GENERATE PICK TASKS ---
                // Get WH-DEFAULT
                var defaultWarehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Code == "WH-DEFAULT");
                if (defaultWarehouse == null) throw new Exception("Không tìm thấy kho mặc định (WH-DEFAULT).");

                var pickTasksToCreate = new List<PickTask>();
                var warehouseTasks = new Dictionary<Guid, PickTask>();

                PickTask GetOrCreateTask(Guid warehouseId)
                {
                    if (!warehouseTasks.TryGetValue(warehouseId, out var task))
                    {
                        task = new PickTask
                        {
                            Id = Guid.NewGuid(),
                            OrderId = order.Id,
                            WarehouseId = warehouseId,
                            Status = PickTaskStatus.Pending,
                            CreatedAt = DateTime.UtcNow
                        };
                        warehouseTasks[warehouseId] = task;
                        pickTasksToCreate.Add(task);
                    }
                    return task;
                }

                foreach (var item in order.OrderItems)
                {
                    var quantityRemaining = item.Quantity;

                    // 1. Check WH-DEFAULT first
                    var defaultInv = await _context.Inventories
                        .Include(inv => inv.WarehouseLocation)
                        .ThenInclude(wl => wl.Warehouse)
                        .FirstOrDefaultAsync(inv => inv.ProductId == item.ProductId && inv.WarehouseLocation != null && inv.WarehouseLocation.Warehouse!.Id == defaultWarehouse.Id);

                    if (defaultInv != null && defaultInv.OnHandQuantity > 0)
                    {
                        var takeQty = Math.Min(quantityRemaining, defaultInv.OnHandQuantity);
                        var task = GetOrCreateTask(defaultWarehouse.Id);
                        task.Items.Add(new PickTaskItem
                        {
                            PickTaskId = task.Id,
                            ProductId = item.ProductId,
                            QuantityToPick = takeQty,
                            PickedQuantity = 0
                        });
                        quantityRemaining -= takeQty;
                        // To be perfectly accurate in a high-concurrency environment, we might want to deduct allocated quantity from inventory. 
                        // But since we just generate tasks, we assume staff will pick based on tasks.
                    }

                    // 2. If still remaining, check other warehouses
                    if (quantityRemaining > 0)
                    {
                        var otherInvs = await _context.Inventories
                            .Include(inv => inv.WarehouseLocation)
                            .ThenInclude(wl => wl.Warehouse)
                            .Where(inv => inv.ProductId == item.ProductId && inv.WarehouseLocation != null && inv.WarehouseLocation.Warehouse!.Id != defaultWarehouse.Id && inv.OnHandQuantity > 0)
                            .OrderByDescending(inv => inv.OnHandQuantity) // Greedily take from largest stock
                            .ToListAsync();

                        foreach (var inv in otherInvs)
                        {
                            if (quantityRemaining <= 0) break;

                            var takeQty = Math.Min(quantityRemaining, inv.OnHandQuantity);
                            var task = GetOrCreateTask(inv.WarehouseLocation!.WarehouseId);
                            task.Items.Add(new PickTaskItem
                            {
                                PickTaskId = task.Id,
                                ProductId = item.ProductId,
                                QuantityToPick = takeQty,
                                PickedQuantity = 0
                            });
                            quantityRemaining -= takeQty;
                        }

                        if (quantityRemaining > 0)
                        {
                            // Edge case: if absolutely not enough stock everywhere, we still put the remainder in WH-DEFAULT to let them figure it out (or shortage alert).
                            var task = GetOrCreateTask(defaultWarehouse.Id);
                            var existingItem = task.Items.FirstOrDefault(i => i.ProductId == item.ProductId);
                            if (existingItem != null) {
                                existingItem.QuantityToPick += quantityRemaining;
                            } else {
                                task.Items.Add(new PickTaskItem
                                {
                                    PickTaskId = task.Id,
                                    ProductId = item.ProductId,
                                    QuantityToPick = quantityRemaining,
                                    PickedQuantity = 0
                                });
                            }
                        }
                    }
                }

                if (pickTasksToCreate.Any())
                {
                    _context.PickTasks.AddRange(pickTasksToCreate);
                }
            }
            
            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }
        public async Task RejectOrderAsync(Guid orderId, Guid salesStaffId, string reason)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus != OrderStatus.PendingConfirmation)
                throw new InvalidOperationException("Chỉ đơn hàng đang 'Chờ xác nhận' mới có thể bị từ chối.");

            order.OrderStatus = OrderStatus.Cancelled;
            order.FulfillmentStatus = FulfillmentStatus.Unallocated;

            // Log reason logic could go here or as a note on the order if we have a note field.
            // For now, we just cancel it.

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task RequestCancelOrderAsync(Guid orderId, Guid customerId, RequestCancelOrderDto request)
        {
            var customerProfile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(customerId);
            if (customerProfile == null) throw new UnauthorizedAccessException("Không tìm thấy hồ sơ khách hàng.");

            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null || order.CustomerProfileId != customerProfile.Id)
                throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            var validStatuses = new[] { OrderStatus.PendingPayment, OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderStatus.Processing };
            if (!validStatuses.Contains(order.OrderStatus))
                throw new InvalidOperationException($"Không thể yêu cầu hủy đơn hàng ở trạng thái {order.OrderStatus}.");

            order.OrderStatus = OrderStatus.CancelRequested;
            await _unitOfWork.Orders.UpdateOrderAsync(order);

            if (customerProfile.AssignedSalesStaffId.HasValue)
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_24_CustomerRequestedCancel,
                    customerProfile.AssignedSalesStaffId.Value,
                    "Yêu cầu hủy đơn",
                    $"Khách hàng yêu cầu hủy đơn {order.OrderCode}. Lý do: {request.Reason}",
                    order.Id,
                    "Order"
                );
            }

            await _unitOfWork.SaveChangesAsync();
        }

        public async Task ProcessCancelRequestAsync(Guid orderId, Guid salesStaffId, ProcessCancelRequestDto request)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.OrderStatus != OrderStatus.CancelRequested && !request.IsApproved)
                throw new InvalidOperationException("Không thể từ chối hủy khi đơn hàng không ở trạng thái yêu cầu hủy.");

            bool wasCancelRequested = order.OrderStatus == OrderStatus.CancelRequested;

            var profile = await _context.CustomerProfiles.FindAsync(order.CustomerProfileId);
            var customerUserId = profile?.UserId ?? Guid.Empty;

            if (!request.IsApproved)
            {
                // Khôi phục trạng thái
                if (order.PaymentMethod == PaymentMethod.SePay && order.PaymentStatus != PaymentStatus.Paid)
                    order.OrderStatus = OrderStatus.PendingPayment;
                else if (order.PaymentMethod == PaymentMethod.COD && order.FulfillmentStatus == FulfillmentStatus.Unallocated)
                    order.OrderStatus = OrderStatus.PendingConfirmation;
                else if (order.FulfillmentStatus != FulfillmentStatus.Unallocated)
                    order.OrderStatus = OrderStatus.Processing; // hoặc Confirmed tùy logic, lấy chung là Confirmed
                else 
                    order.OrderStatus = OrderStatus.Confirmed;
                
                if (customerUserId != Guid.Empty)
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_25_CancelRequestResult,
                        customerUserId,
                        "Yêu cầu hủy đơn bị từ chối",
                        $"Yêu cầu hủy đơn {order.OrderCode} không được chấp thuận. Lý do: {request.Reason}",
                        order.Id,
                        "Order"
                    );
                }
            }
            else
            {
                order.OrderStatus = OrderStatus.Cancelled;
                order.FulfillmentStatus = FulfillmentStatus.Unallocated;

                // Hoàn lại tiền (nếu đã thanh toán) và Credit đã dùng
                decimal amountToRefund = 0m;
                if (order.PaymentStatus == PaymentStatus.Paid || order.PaymentStatus == PaymentStatus.PartiallyPaid)
                {
                    amountToRefund += order.FinalPayment; // Nếu mới thanh toán một phần thì đáng ra cần check amount paid thực tế, ở đây tạm dùng FinalPayment
                }
                amountToRefund += order.CreditApplied;

                if (amountToRefund > 0)
                {
                    if (profile != null)
                    {
                        profile.AvailableCredit += amountToRefund;
                        // profile đã được track bởi EF Core, không cần gọi Update() thủ công
                        
                        _context.CreditTransactions.Add(new CreditTransaction
                        {
                            CustomerProfileId = profile.Id,
                            Amount = amountToRefund,
                            Description = $"Hoàn tiền từ đơn hàng hủy {order.OrderCode}",
                            OrderId = order.Id,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                // Nếu là COD, trả lại reservation/allocation (TODO)

                if (customerUserId != Guid.Empty)
                {
                    string notifTitle = wasCancelRequested ? "Yêu cầu hủy đơn được chấp thuận" : "Đơn hàng đã bị hủy";
                    string notifBody = wasCancelRequested ? $"Yêu cầu hủy đơn {order.OrderCode} đã được xử lý thành công." : $"Đơn hàng {order.OrderCode} đã bị hủy. Lý do: {request.Reason}";

                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_25_CancelRequestResult,
                        customerUserId,
                        notifTitle,
                        notifBody,
                        order.Id,
                        "Order"
                    );
                }
            }

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 1: LẬP LỊCH XE & PHÂN CÔNG GIAO HÀNG
        // =====================================================================

        public async Task<ScheduleDeliveryResponseDto> ScheduleDeliveryAsync(Guid scheduledByUserId, ScheduleDeliveryRequestDto dto)
        {
            if (dto.OrderIds == null || !dto.OrderIds.Any())
                throw new Exception("Danh sách đơn hàng không được rỗng.");

            if (dto.VehicleId < 1 || dto.VehicleId > 5)
                throw new Exception("Mã xe không hợp lệ (phải từ 1 đến 5).");

            var validShifts = new[] { "Sáng", "Trưa", "Chiều" };
            if (!validShifts.Contains(dto.Shift))
                throw new Exception("Ca giao hàng không hợp lệ. Chọn: Sáng / Trưa / Chiều.");

            // Kiểm tra xung đột xe + ca
            var conflictExists = await _context.Orders
                .AnyAsync(o => o.DeliveryVehicleId == dto.VehicleId
                            && o.DeliveryShift == dto.Shift
                            && o.DeliveryStatus == DeliveryStatus.InDelivery
                            && !dto.OrderIds.Contains(o.Id));

            if (conflictExists)
                throw new InvalidOperationException($"Xe {dto.VehicleId} đang trong ca {dto.Shift}. Không thể xếp thêm đơn.");

            // Kiểm tra ngày và ca giao hàng hết hạn (MGR-06 / BR-DL-02)
            var targetDate = dto.DeliveryDate ?? DateTime.UtcNow.Date;
            var localNow = DateTime.UtcNow.AddHours(7); // Giả định múi giờ GMT+7 Việt Nam
            var localToday = localNow.Date;

            if (targetDate.Date < localToday)
            {
                throw new InvalidOperationException("Không thể lên lịch giao hàng cho ngày trong quá khứ.");
            }

            if (targetDate.Date == localToday)
            {
                var currentHour = localNow.Hour;
                if (dto.Shift == "Sáng" && currentHour >= 10)
                {
                    throw new InvalidOperationException("Đã quá 10:00 AM, không thể thêm/sửa đơn hàng cho Ca sáng ngày hôm nay.");
                }
                if (dto.Shift == "Trưa" && currentHour >= 14)
                {
                    throw new InvalidOperationException("Đã quá 14:00 (2:00 PM), không thể thêm/sửa đơn hàng cho Ca trưa ngày hôm nay.");
                }
                if (dto.Shift == "Chiều" && currentHour >= 22)
                {
                    throw new InvalidOperationException("Đã quá 22:00 (10:00 PM), không thể thêm/sửa đơn hàng cho Ca chiều ngày hôm nay.");
                }
            }

            var orders = await _context.Orders
                .Where(o => dto.OrderIds.Contains(o.Id))
                .ToListAsync();

            int scheduled = 0;
            foreach (var order in orders)
            {
                // Chỉ lập lịch cho đơn đã HandedOver hoặc Ready
                if (order.DeliveryStatus != DeliveryStatus.NotScheduled && order.DeliveryStatus != DeliveryStatus.Rescheduled)
                    continue;

                order.DeliveryVehicleId = dto.VehicleId;
                order.DeliveryShift = dto.Shift;
                order.ScheduledDeliveryDate = targetDate;
                order.DeliveryStatus = DeliveryStatus.Scheduled;
                scheduled++;
            }

            await _context.SaveChangesAsync();

            return new ScheduleDeliveryResponseDto
            {
                VehicleId = dto.VehicleId,
                Shift = dto.Shift,
                DeliveryDate = targetDate,
                OrdersScheduled = scheduled,
                Message = $"Đã lập lịch {scheduled} đơn hàng cho xe {dto.VehicleId} ca {dto.Shift} ngày {targetDate:dd/MM/yyyy}."
            };
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 1: LẤY DANH SÁCH ĐƠN CẦN/ĐANG GIAO
        // =====================================================================

        public async Task<List<DeliveryOrderListDto>> GetDeliveryOrdersAsync(Guid salesStaffId)
        {
            var orders = await _context.Orders
                .Include(o => o.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Include(o => o.CustomerProfile.Addresses)
                .Include(o => o.OrderItems)
                .Where(o => o.CustomerProfile.AssignedSalesStaffId == salesStaffId
                         && (
                             (o.DeliveryStatus == DeliveryStatus.NotScheduled &&
                              (o.FulfillmentStatus == FulfillmentStatus.Ready
                            || o.FulfillmentStatus == FulfillmentStatus.Consolidating
                            || o.FulfillmentStatus == FulfillmentStatus.Consolidated
                            || o.FulfillmentStatus == FulfillmentStatus.HandedOver
                            || o.FulfillmentStatus == FulfillmentStatus.Fulfilled))
                          || o.DeliveryStatus == DeliveryStatus.Scheduled
                          || o.DeliveryStatus == DeliveryStatus.InDelivery
                          || o.DeliveryStatus == DeliveryStatus.Failed
                          || o.DeliveryStatus == DeliveryStatus.Rescheduled
                         ))
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            return orders.Select(o =>
            {
                var defaultAddress = o.CustomerProfile?.Addresses?.FirstOrDefault(a => a.IsDefault)
                                  ?? o.CustomerProfile?.Addresses?.FirstOrDefault();
                string address = defaultAddress != null
                    ? $"{defaultAddress.SpecificAddress}, {defaultAddress.Ward}, {defaultAddress.District}, {defaultAddress.City}"
                    : o.CustomerProfile?.CompanyAddress ?? "---";

                return new DeliveryOrderListDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    CustomerName = o.CustomerProfile?.Representative ?? o.CustomerProfile?.CompanyName ?? "Khách hàng",
                    CustomerPhone = o.CustomerProfile?.User?.PhoneNumber ?? o.CustomerProfile?.CompanyPhone ?? "---",
                    ShippingAddress = address,
                    FinalPayment = o.FinalPayment,
                    AmountPaid = o.AmountPaid,
                    PaymentMethod = o.PaymentMethod.ToString(),
                    OrderStatus = o.OrderStatus.ToString(),
                    DeliveryStatus = o.DeliveryStatus.ToString(),
                    VehicleId = o.DeliveryVehicleId,
                    Shift = o.DeliveryShift,
                    ScheduledDeliveryDate = o.ScheduledDeliveryDate,
                    FailedDeliveryCount = o.FailedDeliveryCount,
                    IsBlocked = o.IsBlockedForDelivery,
                    ItemCount = o.OrderItems?.Sum(oi => oi.Quantity) ?? 0
                };
            }).ToList();
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 2: GHI NHẬN KẾT QUẢ GIAO HÀNG (POD + COD)
        // =====================================================================

        public async Task<DeliveryResultResponseDto> RecordDeliveryResultAsync(Guid orderId, Guid salesStaffId, RecordDeliveryResultDto dto)
        {
            var order = await _context.Orders
                .Include(o => o.CustomerProfile)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
            if (order.IsBlockedForDelivery)
                throw new InvalidOperationException("Đơn hàng đã bị khóa do thất bại quá 3 lần. Vui lòng liên hệ Sales Manager.");

            // Ràng buộc: Không cho phép giao hàng trước ngày hẹn dự kiến
            var localNow = DateTime.UtcNow.AddHours(7);
            var localToday = localNow.Date;
            if (order.ScheduledDeliveryDate.HasValue && order.ScheduledDeliveryDate.Value.Date > localToday)
            {
                throw new InvalidOperationException($"Đơn hàng này có lịch hẹn giao vào ngày {order.ScheduledDeliveryDate.Value.ToString("dd/MM/yyyy")}. Không thể xác nhận giao hàng trước ngày hẹn.");
            }

            var outcome = dto.DeliveryOutcome?.ToLower() ?? "delivered";
            bool debtCreated = false;
            decimal? remainingDebt = null;

            // ─ Lưu ảnh chữ ký lên Cloudinary
            if (!string.IsNullOrEmpty(dto.CustomerSignatureBase64))
            {
                try
                {
                    var sigUrl = await _cloudinaryService.UploadBase64ImageAsync(
                        dto.CustomerSignatureBase64,
                        "signatures",
                        $"{order.OrderCode}-sig"
                    );
                    order.CustomerSignatureUrl = sigUrl;
                }
                catch (Exception ex) { Console.WriteLine($"[Delivery] Cloudinary Sig upload error: {ex.Message}"); }
            }

            // ─ Lưu ảnh hiện trường lên Cloudinary
            if (!string.IsNullOrEmpty(dto.DeliveryPhotoBase64))
            {
                try
                {
                    var photoUrl = await _cloudinaryService.UploadBase64ImageAsync(
                        dto.DeliveryPhotoBase64,
                        "delivery-photos",
                        $"{order.OrderCode}-photo"
                    );
                    order.DeliveryPhotoUrl = photoUrl;
                }
                catch (Exception ex) { Console.WriteLine($"[Delivery] Cloudinary Photo upload error: {ex.Message}"); }
            }

            if (outcome == "delivered" || outcome == "partially_delivered")
            {
                // ─ Cập nhật tiền thu
                order.AmountPaid = dto.AmountCollected;
                order.DeliveredAt = DateTime.UtcNow;
                
                if (outcome == "delivered")
                {
                    order.DeliveryStatus = DeliveryStatus.Delivered;
                }
                else
                {
                    order.DeliveryStatus = DeliveryStatus.PartiallyDelivered;
                }

                var amountDue = order.FinalPayment - dto.AmountCollected;

                if (amountDue <= 0)
                {
                    // Khách trả đủ hoặc thừa → COMPLETED
                    order.PaymentStatus = PaymentStatus.Paid;
                    order.OrderStatus = OrderStatus.Completed;
                }
                else
                {
                    // Khách trả thiếu → PARTIALLY_PAID + tạo DebtRecord, trạng thái đơn vẫn chuyển sang Completed
                    order.PaymentStatus = PaymentStatus.PartiallyPaid;
                    order.OrderStatus = OrderStatus.Completed;

                    var debtRecord = new CustomerDebt
                    {
                        CustomerProfileId = order.CustomerProfileId,
                        OrderId = order.Id,
                        DebtAmount = amountDue,
                        Status = DebtStatus.InDebt,
                        OverdueDays = 0
                    };
                    await _context.CustomerDebts.AddAsync(debtRecord);
                    debtCreated = true;
                    remainingDebt = amountDue;
                }
            }
            else // failed
            {
                order.FailedDeliveryCount++;
                order.DeliveryStatus = DeliveryStatus.Failed;

                if (order.FailedDeliveryCount >= 3)
                {
                    order.IsBlockedForDelivery = true;
                    order.DeliveryStatus = DeliveryStatus.Failed;
                }
                else
                {
                    order.DeliveryStatus = DeliveryStatus.Rescheduled;
                }
            }

            await _context.SaveChangesAsync();

            return new DeliveryResultResponseDto
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                NewDeliveryStatus = order.DeliveryStatus.ToString(),
                NewOrderStatus = order.OrderStatus.ToString(),
                DebtRecordCreated = debtCreated,
                RemainingDebt = remainingDebt,
                IsBlockedByFailures = order.IsBlockedForDelivery,
                Message = order.IsBlockedForDelivery
                    ? "Đơn hàng đã bị khóa do thất bại 3 lần liên tiếp. Hệ thống đã chuyển hồ sơ lên Sales Manager."
                    : debtCreated ? $"Giao hàng thành công. Khách còn nợ {remainingDebt:N0}đ. Đã tạo sổ công nợ."
                    : "Giao hàng và thu tiền thành công."
            };
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 3: YÊU CẦU HỦY ĐƠN PAID (CR-06)
        // =====================================================================

        public async Task RequestCancelPaidOrderAsync(Guid orderId, Guid requestedByUserId, string reason)
        {
            var order = await _unitOfWork.Orders.GetOrderByIdAsync(orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.PaymentStatus != PaymentStatus.Paid)
                throw new InvalidOperationException("Chỉ có thể yêu cầu hủy đơn hàng đã thanh toán (PAID).");

            if (order.OrderStatus == OrderStatus.CancelRequested || order.OrderStatus == OrderStatus.CancelledReallocated)
                throw new InvalidOperationException("Đơn hàng này đã có yêu cầu hủy trước đó.");

            order.OrderStatus = OrderStatus.CancelRequested;
            order.CancelReason = reason;
            order.CancelRequestedAt = DateTime.UtcNow;

            await _unitOfWork.Orders.UpdateOrderAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }

        // =====================================================================
        // LUỒNG 5 – BƯỚC 4: DUYỆT HỦY + TẠO ĐƠN THAY THẾ + CREDIT
        // =====================================================================

        public async Task<ReplacementOrderResponseDto> ApproveCancelAndCreateReplacementAsync(
            Guid originalOrderId, Guid managerId, CreateReplacementOrderDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Lấy đơn gốc
                var originalOrder = await _context.Orders
                    .Include(o => o.CustomerProfile)
                    .Include(o => o.OrderItems)
                    .FirstOrDefaultAsync(o => o.Id == originalOrderId)
                    ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng gốc.");

                if (originalOrder.OrderStatus != OrderStatus.CancelRequested)
                    throw new InvalidOperationException("Đơn hàng không ở trạng thái chờ duyệt hủy.");

                var originalPaidAmount = originalOrder.FinalPayment;

                // 2. Tính giá trị đơn thay thế
                decimal newOrderTotal = 0;
                var newOrderItems = new List<OrderItem>();

                foreach (var item in dto.Items)
                {
                    var product = await _context.Products.FindAsync(item.ProductId)
                        ?? throw new Exception($"Không tìm thấy sản phẩm ID: {item.ProductId}");
                    newOrderTotal += item.Price * item.Quantity;
                    newOrderItems.Add(new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        PriceSnapshot = item.Price,
                        CostSnapshot = 0
                    });
                }

                // 3. Tính phân bổ
                decimal reallocatedToNewOrder = Math.Min(newOrderTotal, originalPaidAmount);
                decimal creditToWallet = Math.Max(0, originalPaidAmount - newOrderTotal);

                // 4. Tạo đơn thay thế
                var replacementCode = $"VT-RP-{DateTime.UtcNow:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
                var replacementOrder = new Order
                {
                    CustomerProfileId = originalOrder.CustomerProfileId,
                    OrderCode = replacementCode,
                    TotalAmount = newOrderTotal,
                    DiscountAmount = 0,
                    VatAmount = 0,
                    FinalPayment = newOrderTotal,
                    PaymentMethod = originalOrder.PaymentMethod,
                    PaymentStatus = reallocatedToNewOrder >= newOrderTotal ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid,
                    OrderStatus = OrderStatus.Confirmed,
                    CreatedAt = DateTime.UtcNow
                };
                foreach (var item in newOrderItems) { item.Order = replacementOrder; }
                replacementOrder.OrderItems = newOrderItems;
                await _context.Orders.AddAsync(replacementOrder);

                // 5. Tạo PaymentReallocation
                var reallocation = new PaymentReallocation
                {
                    OriginalOrderId = originalOrderId,
                    ReplacementOrderId = replacementOrder.Id,
                    Amount = reallocatedToNewOrder,
                    Status = "ReallocatedToOrder",
                    Timestamp = DateTime.UtcNow
                };
                await _context.PaymentReallocations.AddAsync(reallocation);

                // 6. Cập nhật đơn gốc → CancelledReallocated
                originalOrder.OrderStatus = OrderStatus.CancelledReallocated;
                originalOrder.ReplacementOrderId = replacementOrder.Id;

                // 7. Nạp Credit vào ví (nếu còn thừa)
                if (creditToWallet > 0)
                {
                    // Optimistic concurrency: reload profile with current state
                    var profile = await _context.CustomerProfiles.FindAsync(originalOrder.CustomerProfileId)
                        ?? throw new Exception("Không tìm thấy hồ sơ khách hàng.");
                    profile.AvailableCredit += creditToWallet;

                    var creditReallocation = new PaymentReallocation
                    {
                        OriginalOrderId = originalOrderId,
                        ReplacementOrderId = null,
                        Amount = creditToWallet,
                        Status = "RefundedToCredit",
                        Timestamp = DateTime.UtcNow
                    };
                    await _context.PaymentReallocations.AddAsync(creditReallocation);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 8. Lấy số dư Credit hiện tại
                var updatedProfile = await _context.CustomerProfiles.FindAsync(originalOrder.CustomerProfileId);

                return new ReplacementOrderResponseDto
                {
                    ReplacementOrderId = replacementOrder.Id,
                    ReplacementOrderCode = replacementOrder.OrderCode,
                    NewOrderValue = newOrderTotal,
                    OriginalPaidAmount = originalPaidAmount,
                    CreditAllocated = creditToWallet,
                    ReallocatedAmount = reallocatedToNewOrder,
                    CustomerCreditBalance = updatedProfile?.AvailableCredit ?? 0,
                    Message = creditToWallet > 0
                        ? $"Đã tạo đơn thay thế {replacementCode}. {creditToWallet:N0}đ đã được chuyển vào ví Credit của khách hàng."
                        : $"Đã tạo đơn thay thế {replacementCode}. Toàn bộ giá trị đơn gốc đã được chuyển sang đơn mới."
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
