using VietTien.API.DTOs.Delivery;

namespace VietTien.API.Services.Interfaces
{
    public interface IDeliveryTripService
    {
        Task<DeliveryTripResponseDto> CreateTripAsync(Guid createdByUserId, CreateDeliveryTripRequestDto dto);
        Task<DeliveryTripResponseDto> StartTripAsync(Guid tripId);
        Task<DeliveryTripResponseDto> GetTripByIdAsync(Guid tripId);
        Task<RecordDeliveryAttemptResponseDto> RecordAttemptAsync(Guid recordedByUserId, RecordDeliveryAttemptRequestDto dto);
        Task<RecordCollectionResponseDto> RecordCollectionAsync(RecordCollectionRequestDto dto);
    }
}
