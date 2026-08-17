using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using VietTien.API.Data;
using VietTien.API.Infrastructure.Middleware;
using VietTien.API.Infrastructure.Security;
using VietTien.API.Repositories.Implementations;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.API.Services.BackgroundServices;
using VietTien.API.Services.ScheduledJobs;
using VietTien.API.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Dùng để mã hoá secret Integrations (SePay/eSMS/Email...) lưu trong SystemConfigVersion — xem SystemConfigService.
builder.Services.AddDataProtection();

// L3-SEC-05: UseHttpsRedirection() mặc định không biết redirect sang cổng nào khi app chạy sau
// reverse proxy/App Service (không tự bind cổng HTTPS riêng) — phải khai báo cổng tường minh, kết
// hợp UseForwardedHeaders() ở dưới để nhận diện đúng request gốc là http hay https.
builder.Services.AddHttpsRedirection(o => o.HttpsPort = 443);

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));
builder.Services.Configure<AzureFormRecognizerSettings>(builder.Configuration.GetSection("AzureFormRecognizer"));



builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICartRepository, CartRepository>();
builder.Services.AddScoped<IAddressRepository, AddressRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();


builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IInventoryReservationService, InventoryReservationService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IQuotationRepository, QuotationRepository>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<IProductPriceUpdateService, ProductPriceUpdateService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();
builder.Services.AddScoped<IStockTransferService, StockTransferService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IWarehouseManagementService, WarehouseManagementService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddHttpClient<ISmsService, eSmsService>();

builder.Services.AddSignalR();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IAddressService, AddressService>();
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IOcrService, DocumentIntelligenceOcrService>();
builder.Services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();
builder.Services.AddScoped<ISalesAllocationService, SalesAllocationService>();
builder.Services.AddScoped<IManualPaymentService, ManualPaymentService>(); // MGR-05
builder.Services.AddScoped<ISalesChangeRequestService, SalesChangeRequestService>(); // LUỒNG 7 (WF-07)
// SRS NAC-05: hàng rào phân quyền theo kho được phân công, dùng chung cho mọi service ghi tồn kho.
builder.Services.AddScoped<IWarehouseAccessGuard, WarehouseAccessGuard>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IGoodsIssueService, GoodsIssueService>();
builder.Services.AddScoped<IMaterialService, MaterialService>();
builder.Services.AddScoped<IStockAdjustmentService, StockAdjustmentService>(); // P0-1
builder.Services.AddScoped<IDeliveryTripService, DeliveryTripService>(); // Nhóm C (DEL-01..07)
builder.Services.AddScoped<IInventoryCountSessionService, InventoryCountSessionService>(); // DEF-L4-003

// AI Marketing Studio & Make.com Webhook
builder.Services.AddHttpClient<IMakeWebhookService, MakeWebhookService>();
builder.Services.AddScoped<IAiGeneratorService, AiGeneratorService>();
builder.Services.AddScoped<IMarketingPostService, MarketingPostService>();

// Admin module (Phase 1): Audit Log, Cấu hình hệ thống versioned, RBAC quản lý User
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ISystemConfigService, SystemConfigService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();

// Admin module (Phase 2): Scheduled Jobs + System Health
builder.Services.AddScoped<IJobRunService, JobRunService>();
builder.Services.AddScoped<IWebhookLogService, WebhookLogService>();
builder.Services.AddScoped<IScheduledJob, OrderSlaJob>();
builder.Services.AddScoped<IScheduledJob, SePayReservationExpiryJob>();
builder.Services.AddScoped<IScheduledJob, LowStockAlertJob>();
builder.Services.AddScoped<IScheduledJob, SePayWebhookRetryJob>();
builder.Services.AddScoped<IScheduledJob, MarketingPostMakeScheduleJob>();
builder.Services.AddScoped<IScheduledJob, QuotationExpiryJob>();
builder.Services.AddScoped<IScheduledJob, UpcomingDeliveryReminderJob>();

// Admin module (Phase 3): KPI engine + dashboard theo role
builder.Services.AddScoped<ISalesTargetService, SalesTargetService>();
builder.Services.AddScoped<IKpiService, KpiService>();
builder.Services.AddScoped<ISalesStaffDashboardService, SalesStaffDashboardService>();
builder.Services.AddScoped<ISalesManagerDashboardService, SalesManagerDashboardService>();
builder.Services.AddScoped<ICeoDashboardService, CeoDashboardService>();
builder.Services.AddScoped<IWarehouseDashboardService, WarehouseDashboardService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();

// Admin module: Master Data (Vehicle, DiscountTier)
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<IDiscountTierService, DiscountTierService>();

builder.Services.AddScoped<IReviewService, ReviewService>();

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;

if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
{
    throw new InvalidOperationException(
        "JwtSettings:SecretKey chưa được cấu hình. Thêm giá trị vào appsettings.Development.json (file này nằm trong .gitignore) hoặc biến môi trường JwtSettings__SecretKey.");
}

var key = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidIssuer              = jwtSettings.Issuer,
        ValidateAudience         = true,
        ValidAudience            = jwtSettings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey         = new SymmetricSecurityKey(key),
        ValidateLifetime         = true,
        ClockSkew                = TimeSpan.Zero  // Không cho phép gia hạn thêm
    };

    options.Events = new JwtBearerEvents
    {
        // Cần thiết cho SignalR: đọc token từ query string thay vì header
        // Vì WebSocket không hỗ trợ Authorization header
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("{\"message\":\"Bạn chưa đăng nhập hoặc token đã hết hạn.\"}");
        },
        OnForbidden = async context =>
        {
            // Ghi security log cho mọi truy cập bị từ chối 403 (Admin có thể tìm kiếm phát hiện bất thường).
            // Bọc try/catch để lỗi ghi log không bao giờ làm hỏng phản hồi 403 gốc.
            try
            {
                var auditLogService = context.HttpContext.RequestServices.GetService<IAuditLogService>();
                if (auditLogService != null)
                {
                    var userIdString = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    Guid? actorUserId = Guid.TryParse(userIdString, out var parsedId) ? parsedId : null;
                    var actorEmail = context.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
                    var actorRole = context.HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;

                    await auditLogService.LogAsync(
                        entityName: "Endpoint",
                        entityId: context.HttpContext.Request.Path.ToString(),
                        action: "PERMISSION_DENIED",
                        actorUserId: actorUserId,
                        actorEmail: actorEmail,
                        actorRole: actorRole,
                        before: null,
                        after: null,
                        reason: $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
                        ipAddress: context.HttpContext.Connection.RemoteIpAddress?.ToString());
                }
            }
            catch
            {
                // Không throw: ghi security log là best-effort, không được chặn response 403.
            }

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"Bạn không có quyền truy cập tài nguyên này.\"}");
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("StaffOnly", policy => policy.RequireRole("Admin", "SalesStaff", "WarehouseStaff", "AccountingStaff"));
    options.AddPolicy("CustomerOnly", policy => policy.RequireRole("Customer"));
});


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        // Production frontend domain(s) đọc từ config "AllowedOrigins" (comma-separated,
        // set qua Azure App Service Application settings), cộng thêm origin dev cố định.
        var productionOrigins = (builder.Configuration["AllowedOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var origins = productionOrigins.Concat(new[]
        {
            "http://localhost:3000",
            "http://localhost:5173",
            "https://localhost:5173"
        }).Distinct().ToArray();

        policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});


builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
    });
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "VietTien API",
        Version     = "v1",
        Description = "API cho hệ thống quản lý bán hàng VietTien"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Nhập JWT token vào đây. Ví dụ: Bearer {token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});


builder.Services.AddHostedService<ScheduledJobRunnerBackgroundService>();

var app = builder.Build();

// Tự động áp dụng migration còn thiếu mỗi khi app khởi động (single-instance deploy,
// không có bước "dotnet ef database update" riêng trong pipeline CI/CD).
// Guard IsRelational(): VietTien.IntegrationTests thay ApplicationDbContext bằng EF InMemory
// (CustomWebApplicationFactory) — provider đó không hỗ trợ Migrate() và sẽ throw nếu gọi thẳng.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
}

// Lưới an toàn cuối cùng cho exception chưa được controller tự bắt -> phải đứng trước mọi
// middleware khác trong pipeline để bọc được toàn bộ downstream (static files, CORS, auth, controllers).
app.UseMiddleware<ExceptionHandlingMiddleware>();

// L3-SEC-15: gắn header bảo mật cho mọi response (kể cả Development, để test/scan local phản ánh đúng).
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "VietTien API v1");
        c.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsDevelopment())
{
    // L3-SEC-05: phải đứng TRƯỚC UseHttpsRedirection() để middleware sau đọc đúng scheme gốc
    // (X-Forwarded-Proto) khi app chạy sau reverse proxy/Azure App Service — nơi TLS bị terminate
    // ở tầng platform và request tới Kestrel luôn là HTTP thuần dù client gọi https://.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseCors("AllowFrontend");

app.UseAuthentication(); 
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<WarehouseHub>("/hubs/warehouse");
app.MapHub<SalesHub>("/hubs/sales");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

// Cho phép WebApplicationFactory<Program> (VietTien.IntegrationTests) truy cập được lớp Program —
// top-level statements mặc định sinh ra class Program internal, khai báo partial ở đây để public hoá.
public partial class Program { }

public class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrEmpty(str)) return DateTime.MinValue;

        // Nếu chuỗi chứa thông tin múi giờ hoặc kết thúc bằng Z, ta chuyển sang UTC
        if (str.EndsWith("Z") || str.Contains("+") || (str.Contains("T") && str.Length > 10))
        {
            return DateTime.Parse(str).ToUniversalTime();
        }

        // Nếu chỉ là ngày (yyyy-MM-dd) hoặc không chỉ định múi giờ, ta giữ nguyên giá trị
        return DateTime.Parse(str);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Force the Kind to Utc so it formats with 'Z' suffix
        var utcVal = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utcVal.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }
}
