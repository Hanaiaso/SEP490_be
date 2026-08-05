using System.Threading.Tasks;
using VietTien.API.DTOs.Marketing;

namespace VietTien.API.Services.Interfaces
{
    public interface IAiGeneratorService
    {
        Task<GenerateAiContentResponseDto> GenerateMarketingOptionsAsync(GenerateAiContentRequestDto request);
    }
}
