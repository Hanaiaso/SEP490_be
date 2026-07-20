using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface IStockTransferService
    {
        Task<IEnumerable<StockTransferDto>> GetAllAsync();
        Task<StockTransferDto> GetByIdAsync(Guid id);
        Task<StockTransferDto> CreateAsync(CreateStockTransferDto dto, Guid createdByUserId);
        Task<StockTransferDto> UpdateAsync(Guid id, UpdateStockTransferDto dto);
        Task<StockTransferDto> DispatchAsync(Guid id);
        Task<StockTransferDto> ReceiveAsync(Guid id, ReceiveStockTransferDto dto);
        Task<StockTransferDto> CancelAsync(Guid id);
    }
}
