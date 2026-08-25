using VietTien.API.DTOs.Delivery;

namespace VietTien.API.Services.Interfaces
{
    public interface IDeliveryTripService
    {
        Task<DeliveryTripResponseDto> CreateTripAsync(Guid createdByUserId, CreateDeliveryTripRequestDto dto);
        Task<DeliveryTripResponseDto> StartLoadingAsync(Guid tripId, StartLoadingRequestDto dto);
        Task<DeliveryTripResponseDto> AddOrdersToTripAsync(Guid tripId, AddOrdersToTripRequestDto dto);
        Task<DeliveryTripResponseDto> RemoveOrderFromTripAsync(Guid tripId, Guid orderId);
        Task<DeliveryTripResponseDto> CancelTripAsync(Guid tripId);
        Task<DeliveryTripResponseDto> StartTripAsync(Guid tripId);
        Task<DeliveryTripResponseDto> GetTripByIdAsync(Guid tripId);
        Task<List<DeliveryTripResponseDto>> GetTripsAsync(DateTime? date, string? status);
        Task<RecordDeliveryAttemptResponseDto> RecordAttemptAsync(Guid recordedByUserId, RecordDeliveryAttemptRequestDto dto);
        Task<RecordCollectionResponseDto> RecordCollectionAsync(RecordCollectionRequestDto dto);
    }
}
