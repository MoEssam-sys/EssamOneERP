using System.ComponentModel.DataAnnotations;

namespace One.Domain.DTOs.Identity
{
    public class CreateCompanyRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;
    }
}
