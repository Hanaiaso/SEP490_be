using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Material
{
    public class MaterialDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double CurrentStock { get; set; }
        public double SafetyThreshold { get; set; }
        public DateTime? LastAlertSentDate { get; set; }
        public bool IsBelowSafetyThreshold { get; set; }
    }

    public class CreateMaterialDto
    {
        [Required(ErrorMessage = "Tên nguyên liệu không được để trống")]
        [StringLength(100, ErrorMessage = "Tên nguyên liệu tối đa 100 ký tự")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Đơn vị tính không được để trống")]
        public string Unit { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "Ngưỡng an toàn phải lớn hơn hoặc bằng 0")]
        public double SafetyThreshold { get; set; }
    }

    public class UpdateMaterialDto
    {
        [Required(ErrorMessage = "Tên nguyên liệu không được để trống")]
        [StringLength(100, ErrorMessage = "Tên nguyên liệu tối đa 100 ký tự")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Đơn vị tính không được để trống")]
        public string Unit { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "Ngưỡng an toàn phải lớn hơn hoặc bằng 0")]
        public double SafetyThreshold { get; set; }
    }
}
