namespace One.Domain.DTOs
{
    public class CompanySummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int UsersCount { get; set; }
        public int AdminsCount { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}

