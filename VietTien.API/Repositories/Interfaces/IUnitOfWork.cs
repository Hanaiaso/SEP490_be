namespace VietTien.API.Repositories.Interfaces
{
    /// <summary>
    /// Unit of Work pattern — gộp nhiều thao tác DB thành 1 transaction duy nhất.
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        IUserRepository Users { get; }
        ICartRepository Carts { get; }
        IOrderRepository Orders { get; }
        IProductRepository Products { get; }
        IAddressRepository Addresses { get; }

        /// <summary>Lưu tất cả thay đổi xuống database</summary>
        Task<int> SaveChangesAsync();
    }
}
