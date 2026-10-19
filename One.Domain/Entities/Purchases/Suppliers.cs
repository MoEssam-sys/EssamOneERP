using One.Domain.Base;
using One.Domain.Services.Attributes;

namespace One.Domain.Entities.Purchases
{
    [DbTable("Suppliers")]
    public class Suppliers : BaseEntity<Guid>
    {
        [InsertField(required: true)]
        public string Code { get; set; }

        [InsertField(required: true), UpdateField(required: true)]
        public string Name { get; set; }

        [InsertField, UpdateField]
        public string? Address { get; set; }

        [InsertField, UpdateField]
        public string? Email { get; set; }

        [InsertField, UpdateField]
        public string? Phone { get; set; }

        // هل السجل مفعّل؟
        [InsertField(required: true), UpdateField(required: true)]
        public bool IsActive { get; set; } = true;
    }
}