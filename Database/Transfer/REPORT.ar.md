# نتيجة فحص OneERP — ٢ نوفمبر ٢٠٢٦

الفحص على نسخة الكود `fa9202aa16e57e20e247d31bb7b26692a0329edf`. الأسماء التالية مؤكدة من الملفات، ولم يتم الاتصال بقواعد ONLINE. ملف `OneERP-DbTransfer.zip` الموجود مع المشروع يحتوي أداة نقل سابقة؛ تمت مراجعتها وتجهيز نسخة مناسبة لـ SQL Server 2022 المحلي.

## كل الجداول المذكورة في البرنامج

| الجدول | الاستخدام المؤكد في النسخة الحالية | مصدر الاسم |
|---|---|---|
| `Branches` | Repository وAPI | `One.Domain/Entities/Sales/Branches.cs` |
| `Brands` | Repository وAPI | `One.Domain/Entities/Sales/Brands.cs` |
| `Warehouses` | Repository وAPI | `One.Domain/Entities/Stocks/Warehouses.cs` |
| `Suppliers` | Repository وAPI | `One.Domain/Entities/Purchases/Suppliers.cs` |
| `Customers` | Entity وDTO؛ بدون Repository خاص حاليًا | `One.Domain/Entities/Sales/Customers.cs` |
| `Products` | Entity وDTO؛ بدون Repository خاص حاليًا | `One.Domain/Entities/Sales/Products.cs` |
| `ProductUnits` | Entity وDTO؛ بدون Repository خاص حاليًا | `One.Domain/Entities/Sales/ProductUnits.cs` |
| `Categories` | Entity وDTO؛ بدون Repository خاص حاليًا | `One.Domain/Entities/Stocks/Categories.cs` |
| `Units` | Entity وDTO؛ بدون Repository خاص حاليًا | `One.Domain/Entities/Stocks/Units.cs` |
| `SalesInvoices` | Entity وDTO؛ واجهة الفواتير تستخدم بيانات نموذجية حاليًا | `One.Domain/Entities/Sales/SalesInvoices.cs` |
| `SalesInvoiceDetails` | Entity وDTO؛ بدون API للفواتير حاليًا | `One.Domain/Entities/Sales/SalesInvoiceDetails.cs` |
| `AspNetUsers` | حسابات المستخدمين | `UsersDbContextModelSnapshot.cs` |
| `AspNetRoles` | الأدوار | `UsersDbContextModelSnapshot.cs` |
| `AspNetUserRoles` | ربط المستخدمين بالأدوار | `UsersDbContextModelSnapshot.cs` |
| `AspNetUserClaims` | Claims المستخدمين | `UsersDbContextModelSnapshot.cs` |
| `AspNetRoleClaims` | Claims الأدوار | `UsersDbContextModelSnapshot.cs` |
| `AspNetUserLogins` | تسجيل الدخول الخارجي ضمن Identity | `UsersDbContextModelSnapshot.cs` |
| `AspNetUserTokens` | Tokens ضمن Identity | `UsersDbContextModelSnapshot.cs` |
| `Companies` | الشركات وتبعيتها للمستخدمين | `UsersDbContext.cs` |
| `OtpChallenges` | تحديات التحقق OTP | `UsersDbContext.cs` |
| `AppLogs` | سجلات التطبيق | `UsersDbContext.cs` |
| `__EFMigrationsHistory` | تتبّع EF migrations؛ يُنقل إذا كان موجودًا | EF Core migrations |

المجموع الأساسي **٢٢ اسمًا**: ١١ أعمال، و١٠ هوية وشركات وسجلات، وجدول تاريخ الترحيلات. قد تتوزع بين المصدرين؛ ليست ٢٢ جدولًا مؤكدة في كل مصدر. جداول Identity السبعة جزء من مخطط EF حتى لو لم تستخدم كل ميزاتها في الشاشات الحالية.

يوجد أيضًا `TestCustomers` و`TestOrders` في `One.Domain/Services/Attributes/TestAttributes.cs` لأمثلة الـ attributes؛ مستبعدان افتراضيًا من النقل. لا توجد سكربتات إنشاء أصلية لجداول الأعمال في نسخة المشروع، لذلك لا يصح تخمين تعريفاتها أو مفاتيحها من C#؛ تُقرأ من القاعدة الفعلية.

## الـ stored procedures

كل الأسماء التالية في `One.Application/Base/Procedures.cs`:

| المجموعة | الأسماء | نتيجة تتبّع الاستخدام |
|---|---|---|
| Brands | `Brand_Insert`, `Brand_Update`, `Brand_Delete`, `Brand_Active`, `Brand_GetById`, `Brand_GetAll` | ثوابت أسماء؛ لم يُعثر على استدعاء لها في الكود الحالي |
| أمثلة | `Test_Insert`, `Test_Update`, `Test_Delete`, `Test_Active`, `Test_GetById`, `Test_GetAll` | ثوابت أمثلة؛ لم يُعثر على استدعاء لها |

عمليات `BaseRepository` و`AttributeCrudEngine` تبني SQL مباشرًا. يوجد helper باسم `DapperParamBuilder` يستخدم `CommandType.StoredProcedure` لاستخراج تعريفات المعاملات، لكن لم يُعثر على مستدعٍ له خارج ملفه. لذلك النتيجة: **١٢ اسمًا معرّفًا، وصفر استدعاءات stored procedure مثبتة من كود التطبيق الحالي**. قد توجد Triggers تستدعي إجراءات على السيرفر؛ هذا يحتاج فحص القاعدة نفسها.

أداة النقل تحتفظ بإجراءات `Brand_*` لو كانت موجودة، للحفاظ على الأسماء المعرّفة في المشروع. إجراءات `Test_*` تحتاج اختيار `-IncludeExamples`. الاستعلام `01_Inventory_Online.sql` يعرض أيضًا كل إجراءات القاعدة مع توضيح هل اسمها مذكور في الكود؛ وجود إجراء على السيرفر لا يثبت أن التطبيق يستخدمه.

## عدد قواعد البيانات والاختيار الأفضل

| إعداد الاتصال | وظيفته في الكود |
|---|---|
| `UserConnection` | EF Core / Identity والشركات وOTP والسجلات |
| `MainConnection` | جداول الأعمال عند بناء Debug |
| `LiveConnection` | جداول الأعمال عند بناء Release |

هذه **ثلاثة مفاتيح إعداد، وليست دليلًا على ثلاث قواعد مختلفة**. إعدادات Development الحالية توجّه `UserConnection` و`MainConnection` إلى نفس القاعدة المحلية `EssamOneERP` على `localhost\SQLEXPRESS`، بينما المستخدم ذكر أن وجهته SQL Server 2022 Local؛ يلزم تأكيد اسم الـ instance بواسطة `00_Local_Server.sql`.

ملف النقل المرفق يذكر مصدرين: `db37048.public.databaseasp.net / db37048` و`db36442.public.databaseasp.net / db36442`. ويذكر `AI_HANDOFF.md` أن UsersDb نُقلت سابقًا إلى `db37048`. هذه قرائن تاريخية؛ إعدادات الـ Online الحالية غير متاحة في الملفات، وبالتالي ربط كل مصدر بوظيفته يحتاج تأكيدًا بنتيجة الحصر أو إعدادات التشغيل الفعلية.

**التوصية: لو المصدران قاعدتان منفصلتان فعلًا، انقل كل واحدة إلى قاعدة محلية مستقلة أولًا** (`db37048_Local` و`db36442_Local`). ده يحافظ على توزيع الجداول والمعرّفات وتاريخ migrations، ويجعل المقارنة مع الـ Online أبسط. اضبط `UserConnection` على النسخة التي تحتوي جداول الهوية، و`MainConnection` و`LiveConnection` على نسخة الأعمال بعد التأكيد.

الدمج ممكن تقنيًا، وإعدادات Development الحالية مثال على توجيه الاتصالين لنفس القاعدة. لكنه يحتاج مراجعة تعارض أسماء الجداول والإجراءات والـ schemas والمفاتيح والتعريفات والمراجع بين القواعد؛ استيراد المصدرين إلى قاعدة واحدة مباشرة قد يفشل أو يخلط البيانات. لا توجد فائدة ضرورية من الدمج لمجرد تشغيل Local. فصل الهوية عن الأعمال وحده لا يثبت عزل بيانات الشركات؛ الـ factory الحالي لا يختار قاعدة تلقائيًا لكل شركة.

## ما تم تجهيزه وما يتطلب المصدر

الحزمة تحتوي استعلام حصر ONLINE، واستعلام تحديد السيرفر المحلي، وأداة توليد ملفات SQL بالهيكل والبيانات والعلاقات والفهارس والإجراءات المحددة وTriggers الجداول، ثم استعلام تحقق Local. النقل لا يحتاج Linked Server أو BACKUP على الاستضافة.

**الحزمة ليست نسخة من البيانات الفعلية بعد.** تعريفات الأعمال وأجسام الإجراءات والبيانات تُستخرج عند تشغيل الأداة من جهاز يستطيع الوصول إلى المصدرين بحسابك المعتاد. بيانات دخول المصدر غير متاحة في الجلسة، والوصول الشبكي لـ SQL Server الخارجي غير مهيأ هنا. راجع `README.md` للتنفيذ و`VALIDATION.md` لحدود الاختبارات.
