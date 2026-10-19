using System.ComponentModel.DataAnnotations;

namespace One.Domain.Base
{
    public class BaseFilter
    {
        // Pagination
        [Required]
        public int PageNumber { get; set; } = 1;

        [Required]
        public int PageSize { get; set; } = 10;
    }
}