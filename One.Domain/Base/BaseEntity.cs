using One.Domain.Services.Attributes;

namespace One.Domain.Base
{
    public class BaseEntity<T>
    {
        [DeleteField, ActiveField, UpdateField(required: true)]
        public T Id { get; set; } = default!;

        // متى تم إنشاء السجل
        [InsertField(required: true)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // متى تم تعديل السجل آخر مرة
        [UpdateField(required: true)]
        public DateTime? UpdatedAt { get; set; } = null;

        // معرف المستخدم الذي أنشأ السجل
        [InsertField(required: true)]
        public Guid CreatedBy { get; set; }

        // معرف آخر من عدّل السجل
        [UpdateField(required: true)]
        public Guid? UpdatedBy { get; set; } = null;
    }
}