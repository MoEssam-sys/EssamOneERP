# نقل عناصر OneERP المستخدمة في الكود إلى Local

المصدران المطلوبان:

| السيرفر | قاعدة البيانات |
|---|---|
| `db37048.public.databaseasp.net` | `db37048` |
| `db36442.public.databaseasp.net` | `db36442` |

المطلوب نقل **الهيكل والبيانات والإجراءات المرتبطة بالكود فقط**. لذلك هذه الحزمة تستخدم قائمة أسماء محددة وتولّد ملف SQL مستقل لكل قاعدة؛ لا تحتاج Backup/Restore أو Linked Server، ولا تنقل كل جداول الاستضافة. لم يتم الاتصال بأي من المصدرين أو استخراج بياناتهما أثناء إعداد الحزمة.

## الحصر من الكود

| المجموعة | أسماء الجداول | دليل الاستخدام |
|---|---|---|
| أعمال مرتبطة بـ API حاليًا | `Branches`, `Brands`, `Warehouses`, `Suppliers` | `One.Application/Repos/` والـ Controllers المقابلة |
| نماذج أعمال موجودة دون دورة API مكتملة | `Customers`, `Products`, `ProductUnits`, `Categories`, `Units`, `SalesInvoices`, `SalesInvoiceDetails` | `One.Domain/Entities/` والـ DTOs |
| الهوية | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` | `UsersDbContext : IdentityDbContext<UserData>` وملف `UsersDbContextModelSnapshot.cs` |
| الشركة وOTP والسجلات | `Companies`, `OtpChallenges`, `AppLogs` | `UsersDbContext` والـ Repositories والخدمات |
| تتبّع الترحيلات | `__EFMigrationsHistory` | تستخدمه EF Core عند تطبيق الترحيلات؛ ينقل لو موجود |
| أمثلة فقط، مستبعدة افتراضيًا | `TestCustomers`, `TestOrders` | `One.Domain/Services/Attributes/TestAttributes.cs`؛ لا يوجد Repository فعّال لهما |

المحصلة: **٢٢ اسم جدول في القائمة الأساسية**: ١١ أعمال، و١٠ هوية/شركات/سجلات، وجدول تاريخ الترحيلات. وجود الاسم في الكود لا يثبت أن الجدول موجود على السيرفر أو في القاعدتين؛ أداة التصدير تفحص كل قاعدة وتنسخ الموجود منها فقط، وتبلغ عن غير الموجود.

الإجراءات المعرّفة في `One.Application/Base/Procedures.cs`:

```text
Brand_Insert   Brand_Update   Brand_Delete
Brand_Active   Brand_GetById  Brand_GetAll
Test_Insert    Test_Update    Test_Delete
Test_Active    Test_GetById    Test_GetAll
```

هذه **ثوابت أسماء فقط**. لم يُعثر على استدعاء فعلي لها في النسخة الحالية؛ عمليات الـ Repositories الحالية تبني SQL مباشرًا. إجراءات `Brand_*` تدخل التصدير لو موجودة، للحفاظ على العناصر المسماة في الكود. أمثلة `Test_*` مستبعدة افتراضيًا مع جدولي الأمثلة؛ أضف `--include-examples` لو تريد تضمينها. لن يتم اختراع تعريفات لإجراءات غير موجودة أو نسخ إجراءات أخرى لمجرد وجودها على السيرفر.

إعداد `UserConnection` يستخدم جداول الهوية والشركات. الأعمال تستخدم `MainConnection` في Debug و`LiveConnection` في Release. لم يثبت من الإعدادات الحالية أي من `db37048` و`db36442` هو Users وأيهما Main؛ نتائج الحصر ستوضح مكان الجداول. لا تفترض أن اختلاف إعداد الاتصال يعني بالضرورة ثلاث قواعد مختلفة.

## ١. تأكيد الموجود على ONLINE

افتح SSMS واتصل بكل سيرفر بحسابك المعتاد، واختَر قاعدة البيانات المقابلة، ثم شغّل:

[`01_Inventory_Online.sql`](01_Inventory_Online.sql)

السكريبت للقراءة فقط. يعرض الأسماء الموجودة فعلًا والـ schema، والإجراءات المسماة في الكود وتعريفاتها المتاحة، والعلاقات والاعتمادات الخارجية للعناصر المحددة. تحتاج `VIEW DEFINITION` للحصر الكامل، و`SELECT` على الجداول المطلوبة لتصدير البيانات.

شغّل أيضًا الاستعلام التالي على **السيرفر المحلي**؛ ستستخدم القيم الناتجة في أوامر التصدير:

```sql
SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS LocalServer,
    CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) AS LocalMajorVersion;
```

الإصدارات: 13 = SQL Server 2016، و14 = 2017، و15 = 2019، و16 = 2022، و17 = 2025. قيمة `LocalServer` هي الاسم الفعلي من الاستعلام، وليست مجرد alias مثل `.` أو `localhost`.

## ٢. توليد ملف SQL بالهيكل والبيانات

شغّل الأوامر التالية من مجلد المستودع **على جهازك الذي يستطيع الوصول إلى السيرفرين**. تحتاج .NET 10 SDK وInternet لاستعادة مكتبة Microsoft SMO. التصدير لا يستخدم Linked Server ولا صلاحية إنشاء Backup.

أوقف الكتابة من التطبيق إلى المصدرين أثناء التصدير. النسخ يتم جدولًا بعد آخر؛ تساوي أعداد الصفوف لا يكشف تغييرات متزامنة داخل الصفوف. احتفظ بإيقاف الكتابة حتى ينتهي تصدير القاعدتين لو بينهما علاقات منطقية.

مثال PowerShell؛ عدّل `YOUR-PC\SQLEXPRESS` إلى نتيجة الاستعلام المحلي، و`16` إلى الإصدار الفعلي:

```powershell
$localServer = 'YOUR-PC\SQLEXPRESS'
$localMajor = 16
$exportDir = Join-Path $env:USERPROFILE 'Downloads\OneERP-DbCopy'
New-Item -ItemType Directory -Force -Path $exportDir | Out-Null

dotnet run --project Database/Transfer/UsedDbExport -- `
  --server db37048.public.databaseasp.net --database db37048 `
  --target db37048_Local --local-server $localServer --target-major $localMajor `
  --output (Join-Path $exportDir 'db37048.used.sql')

dotnet run --project Database/Transfer/UsedDbExport -- `
  --server db36442.public.databaseasp.net --database db36442 `
  --target db36442_Local --local-server $localServer --target-major $localMajor `
  --output (Join-Path $exportDir 'db36442.used.sql')
```

ستُطلب بيانات الدخول تفاعليًا لكل مصدر: SQL username ثم كلمة المرور المخفية. لا تضع كلمة المرور في الأوامر أو ملفات الإعداد أو المحادثة. الاتصال يستخدم التشفير والتحقق من شهادة السيرفر.

الناتج لكل مصدر:

- `dbNNNNN.used.sql`: استعلامات إنشاء قاعدة محلية جديدة والجداول، وإدخال البيانات، وإضافة المفاتيح والعلاقات والفهارس والإجراءات وTriggers الجداول المختارة.
- `dbNNNNN.used.sql.inventory.json`: أسماء العناصر المنسوخة، وأعداد صفوف الجداول، والأسماء غير الموجودة في هذه القاعدة.

الملفان SQL يحتويان بيانات فعلية، منها بيانات المستخدمين وPassword Hashes وOTP إن كانت موجودة؛ احتفظ بهما خارج المستودع ولا ترفعهما إلى Git. الأداة ترفض استبدال ملفات موجودة.

لو الاسم نفسه موجود في أكثر من schema، تتوقف الأداة وتطلب تحديد الـ schema الصحيح؛ مثال: `--schema dbo`. لا تفترض `dbo` إلا بعد الحصر.

## ٣. تنفيذ ملفات SQL على Local

1. افتح SSMS واتصل بالسيرفر المحلي، وأوقف التطبيق المحلي مؤقتًا.
2. فعّل **Query → SQLCMD Mode**، ثم افتح `db37048.used.sql` وشغّله. كرر مع `db36442.used.sql`.
3. السكريبت يتحقق من اسم السيرفر، ويرفض هدفًا موجودًا. ينشئ `db37048_Local` أو `db36442_Local` ولا يستخدم `DROP` أو `REPLACE` لقاعدة موجودة.
4. نقل الهيكل والبيانات يتم في Transaction واحدة لكل قاعدة. عند الخطأ يوقف SQLCMD التنفيذ؛ الصفوف والعناصر داخل المعاملة لا تُعتمد. إنشاء القاعدة نفسها خارج المعاملة، لذلك قد تبقى قاعدة جديدة فارغة بعد الفشل؛ راجع السبب واختَر اسم هدف جديد عند إعادة المحاولة.
5. راجع تقرير العناصر، وشغّل `01_Inventory_Online.sql` على القاعدتين المحليتين لمقارنة الموجود. سكريبت الاستيراد يتحقق أيضًا من عدد صفوف كل جدول قبل `COMMIT`.

شغّل فحص سلامة على Local بعد الاستيراد:

```sql
DBCC CHECKDB ([db37048_Local]) WITH NO_INFOMSGS, ALL_ERRORMSGS;
DBCC CHECKDB ([db36442_Local]) WITH NO_INFOMSGS, ALL_ERRORMSGS;
```

جهّز حساب اتصال محلي، ثم اضبط `UserConnection` و`MainConnection` و`LiveConnection` خارج الملفات المتعقبة وفق نتائج الحصر. لا تنسَ `LiveConnection` لو ستشغّل Release. الـ API يطبّق ترحيلات EF عند بدء التشغيل؛ راجع النسخة المنسوخة وترحيلاتها قبل تشغيله. كلمات مرور اتصال SQL وصلاحيات logins على مستوى السيرفر لا تُنقل بواسطة هذه الملفات.

## حدود التصدير والتحقق

- النقل انتقائي من القائمة في [`used-objects.json`](used-objects.json)، ويشمل العناصر الموجودة فقط في كل مصدر. لا تُنشأ الجداول الناقصة بتخمين أنواعها من C#.
- العلاقات بين الجداول والإجراءات وTriggers قد تحتاج عناصر أخرى غير مسماة في الكود. الأداة ترفض اعتمادًا مسجلًا خارج القائمة، بما فيه جداول أو دوال أو أنواع مخصصة أو مراجع قواعد أخرى، وتعرض اسمه للمراجعة بدل نسخ عناصر إضافية تلقائيًا أو إنتاج ملف ناقص.
- الاعتمادات داخل Dynamic SQL ليست كلها قابلة للاكتشاف آليًا؛ راجع أجسام الإجراءات الفعلية قبل الاستيراد.
- Temporal وmemory-optimized tables والإجراءات/Triggers المشفرة تحتاج خطة منفصلة. الميزات غير المدعومة في إصدار SQL المحلي قد تمنع التصدير أو الاستيراد؛ لم يتم تعطيل فحوص التوافق.
- لا يُعاد تشغيل Trigger أثناء تحميل البيانات؛ يتم إنشاء Triggers بعد تحميلها. قيم rowversion تُولّد محليًا بواسطة SQL Server، وهي ليست نسخة قابلة للاستخدام من رموز التزامن القديمة.
- تم التحقق من بناء الأداة، ومطابقة الحصر لمصادر C#، وفحوص منع استبدال ملفات موجودة. لم يتم تنفيذ اتصال أو تصدير/استيراد فعلي للقاعدتين ONLINE لعدم توافر بيانات دخولهما في هذه الجلسة. تفاصيل تحقق إضافية مسجلة في `VALIDATION.md`.
