using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VietTien.API.Data;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// Dựng <see cref="WarehouseAccessGuard"/> THẬT trên DbContext của test, thay vì mock cho qua.
    /// Guard là hàng rào phân quyền theo kho (SRS NAC-05) nên nếu test dùng mock luôn cho phép thì
    /// mọi test "kho khác không được thao tác" sẽ xanh giả. Ở đây chỉ IServiceScopeFactory là fake,
    /// và nó trả về đúng DbContext của test để nhánh ghi AuditLog vẫn chạy được.
    /// </summary>
    public static class TestWarehouseAccessGuard
    {
        public static IWarehouseAccessGuard Create(ApplicationDbContext db)
            => new WarehouseAccessGuard(
                db,
                new SameContextScopeFactory(db),
                NullLogger<WarehouseAccessGuard>.Instance);

        private sealed class SameContextScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
        {
            private readonly ApplicationDbContext _db;

            public SameContextScopeFactory(ApplicationDbContext db) => _db = db;

            public IServiceScope CreateScope() => this;

            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
                => serviceType == typeof(ApplicationDbContext) ? _db : null;

            public void Dispose() { /* DbContext do test sở hữu, không dispose ở đây. */ }
        }
    }
}
