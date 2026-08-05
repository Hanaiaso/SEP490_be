using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VietTien.API.Data;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// Tạo ApplicationDbContext chạy trên EF Core InMemory — mỗi lần gọi là một DB mới, cô lập giữa các test.
    /// Ignore TransactionIgnoredWarning vì một số service dùng BeginTransactionAsync (InMemory không hỗ trợ).
    /// </summary>
    public static class TestDbFactory
    {
        public static ApplicationDbContext Create()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .EnableSensitiveDataLogging()
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
