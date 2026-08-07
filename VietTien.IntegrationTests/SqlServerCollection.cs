namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Khác hạ tầng L3 (IClassFixture&lt;CustomWebApplicationFactory&gt; — mỗi test class một factory + một
    /// InMemory DB riêng): L2 dùng ICollectionFixture để MỘT container SQL Server phục vụ cả suite.
    /// Boot container mất ~30-60s nên không thể mỗi class một cái.
    ///
    /// Hệ quả (mong muốn): xUnit chạy tuần tự các class trong cùng collection → không có 2 test cùng ghi
    /// vào một DB thật, nên ResetAsync() giữa các test là an toàn.
    /// </summary>
    [CollectionDefinition(Name)]
    public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
    {
        public const string Name = "sqlserver";
    }
}
