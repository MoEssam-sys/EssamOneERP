# نقل OneERP من ONLINE إلى SQL Server 2022 المحلي

الفحص التفصيلي لكل الجداول والإجراءات والتوصية بخصوص القاعدتين في [REPORT.ar.md](REPORT.ar.md). الحزمة تولّد SQL من تعريفات المصدر الفعلية وبياناته؛ لا تحتوي بيانات ONLINE بالفعل. الاتصال المحلي الافتراضي `localhost`، وقد تحتاج اسم instance مثل `localhost\SQLEXPRESS` حسب التثبيت.

## ١. تحديد اسم السيرفر المحلي

في SSMS اتصل بـ `localhost` وشغّل [00_Local_Server.sql](00_Local_Server.sql). لو التثبيت instance باسم محدد استخدم اسمه للاتصال. احتفظ بقيمة `ExactLocalServer`؛ هذه هي اسم السيرفر الفعلي الذي سيحمي سكربت الاستيراد من التنفيذ على instance آخر. SQL Server 2022 رقمه الرئيسي **16**.

## ٢. حصر ONLINE وتحديد وظيفة كل مصدر

افتح اتصالًا بكل مصدر بحسابك المعتاد واختَر قاعدة التطبيق، ثم شغّل [01_Inventory_Online.sql](01_Inventory_Online.sql). الاستعلام للقراءة فقط؛ يعرض الجداول وschemas والأعمدة والعلاقات والإجراءات وتعريفاتها والـ Triggers والاعتمادات.

المصدران المذكوران في ملف النقل الذي جاء مع المشروع:

| السيرفر | قاعدة المصدر | اسم النسخة المحلية الافتراضي |
|---|---|---|
| `db37048.public.databaseasp.net` | `db37048` | `db37048_Local` |
| `db36442.public.databaseasp.net` | `db36442` | `db36442_Local` |

تأكد من هذه الأسماء من اتصالاتك الفعلية؛ لم يتم الاتصال بهما في هذه الجلسة. الملاحظة التاريخية في `AI_HANDOFF.md` تربط UsersDb بـ `db37048`، لكن إعدادات ONLINE الحالية غير موجودة في الملفات. حدّد أي نسخة تحتوي `AspNetUsers` و`Companies` و`__EFMigrationsHistory` وأيهما تحتوي جداول الأعمال. لو نفس الجداول موجودة في المصدرين، حدد المصدر الذي يتصل به التطبيق فعلًا قبل ضبط الإعدادات؛ لا تخلط بياناتهما تلقائيًا.

تحتاج `VIEW DEFINITION` على قاعدة المصدر و`SELECT` على الجداول المطلوبة. قائمة النقل الأساسية ٢٢ اسم جدول في [used-objects.json](used-objects.json). الأداة تنقل الموجود فقط في كل مصدر وتكتب الأسماء غير الموجودة في تقريره؛ غياب جداول Users في قاعدة الأعمال مثلًا قد يكون متوقعًا، وليس نجاحًا أو فشلًا تلقائيًا.

## ٣. توليد Query النقل بالهيكل والبيانات

تحتاج **.NET 10 SDK** على جهاز يستطيع الوصول إلى ONLINE، واتصال Internet لاستعادة مكتبة Microsoft SMO. افتح PowerShell في مجلد هذه الحزمة. أوقف الكتابة من كل تطبيق أو خدمة تتعامل مع المصدرين طوال تصديرهما، لتحصل على نسختين متوافقتين زمنيًا. الأداة تقرأ جدولًا بعد آخر؛ تطابق عدد الصفوف لا يكشف تحديثًا متزامنًا داخل صف.

استبدل `YOUR-PC` بقيمة `ExactLocalServer` من الخطوة الأولى؛ لا تستخدم `localhost` هنا إلا لو كانت هي القيمة الفعلية الناتجة:

```powershell
.\02_Export-Local2022.ps1 -LocalServer 'YOUR-PC'
```

إذا أسماء المصادر مختلفة، عدّلها عبر المعاملات:

```powershell
.\02_Export-Local2022.ps1 -LocalServer 'YOUR-PC' `
  -Source1Server 'ONLINE_USERS_HOST' -Source1Database 'USERS_DB' -Source1Target 'OneERP_Users_Local' `
  -Source2Server 'ONLINE_MAIN_HOST' -Source2Database 'MAIN_DB' -Source2Target 'OneERP_Main_Local'
```

لو سياسة PowerShell عندك لا تسمح بتشغيل السكربت، استخدم الأمرين المباشرين من مجلد الحزمة بدل تغيير السياسة:

```powershell
New-Item -ItemType Directory -Force -Path '.\exports' | Out-Null
$localServer = 'YOUR-PC' # ExactLocalServer from 00_Local_Server.sql

dotnet run --configuration Release --project .\UsedDbExport -- `
  --server db37048.public.databaseasp.net --database db37048 `
  --target db37048_Local --local-server $localServer --target-major 16 `
  --output .\exports\source1.used.sql
if ($LASTEXITCODE -ne 0) { throw 'Source 1 export failed' }

dotnet run --configuration Release --project .\UsedDbExport -- `
  --server db36442.public.databaseasp.net --database db36442 `
  --target db36442_Local --local-server $localServer --target-major 16 `
  --output .\exports\source2.used.sql
if ($LASTEXITCODE -ne 0) { throw 'Source 2 export failed' }
```

لكل مصدر ستُطلب SQL username وكلمة المرور تفاعليًا، وكلمة المرور مخفية. لا تكتب بيانات الدخول في الأوامر أو المحادثة. اتصال ONLINE مشفر مع التحقق من شهادة السيرفر.

الناتج داخل `exports`:

- `source1.used.sql` و`source2.used.sql`: إنشاء قاعدتين جديدتين، وإنشاء الجداول ونسخ البيانات والمفاتيح والفهارس والعلاقات وإجراءات `Brand_*` الموجودة وTriggers الجداول المختارة.
- ملف `.inventory.json` بجانب كل SQL: المصدر والهدف والعناصر المنسوخة وعدد صفوف كل جدول والأسماء غير الموجودة.

لو نفس الاسم موجود في أكثر من schema، تتوقف الأداة. راجع الحصر ثم مرر `-Schema 'ACTUAL_SCHEMA'` للسكربت، أو `--schema ACTUAL_SCHEMA` للأمر المباشر. الأمثلة `TestCustomers` و`TestOrders` وإجراءات `Test_*` مستبعدة افتراضيًا؛ `-IncludeExamples` / `--include-examples` يضمها عند الحاجة.

الأداة ترفض اعتمادًا مسجلًا على عنصر خارج القائمة، مثل جدول إضافي أو دالة أو نوع مخصص أو مرجع لقاعدة أخرى. راجع الاسم المطلوب قبل توسيع خطة النقل. لا يتم تخمين عنصر ناقص أو تجاهل العلاقة. Dynamic SQL قد يحتوي اعتمادات لا تظهر في metadata؛ راجع تعريفات الإجراءات والـ Triggers من الحصر.

ملفات SQL الناتجة تحتوي بيانات التطبيق والمستخدمين وPassword Hashes وأي OTP موجود؛ احفظها محليًا خارج Git. لا يتم استبدال ملف SQL أو تقرير موجود؛ اختر `-OutputDirectory` جديدًا عند إعادة التصدير.

## ٤. تنفيذ ملفات SQL على Local

1. افتح SSMS على السيرفر المحلي الذي فحصته، وأوقف التطبيق المحلي.
2. فعّل **Query → SQLCMD Mode**. افتح `exports/source1.used.sql` وشغله، ثم `source2.used.sql`.
3. الملفات تتحقق من اسم السيرفر وإصدار SQL، وترفض قاعدة هدف موجودة؛ لا يوجد `DROP DATABASE` أو `WITH REPLACE`.
4. نقل الهيكل والبيانات لكل قاعدة في Transaction واحدة، ويتوقف SQLCMD عند الخطأ. إنشاء القاعدة خارج المعاملة؛ قد تبقى قاعدة جديدة فارغة بعد فشل الاستيراد. راجع السبب واستخدم اسم هدف جديد لإعادة المحاولة.
5. شغّل [03_Verify_Local.sql](03_Verify_Local.sql)، وعدّل أسماء قواعده لو اخترت أهدافًا مختلفة. شغّل `01_Inventory_Online.sql` على النسختين المحليتين وقارن النتائج مع المصدرين وتقارير التصدير. الاستيراد يفحص عدد صفوف كل جدول قبل `COMMIT`؛ `DBCC CHECKDB` يفحص سلامة النسخ.

الـ logins وصلاحيات السيرفر لا تُنسخ. استخدم حساب Windows المحلي أو جهّز حساب SQL مناسبًا. قيم `rowversion` يولدها SQL Server من جديد. Temporal وmemory-optimized tables والإجراءات المشفرة تحتاج معالجة منفصلة؛ ميزات المصدر الأحدث من SQL 2022 قد تمنع النقل.

## ٥. ربط البرنامج بالنسختين المحليتين

الاختيار الموصى به هو الحفاظ على قاعدتين لو المصدران منفصلان. اربط `UserConnection` بنسخة الهوية، و`MainConnection` و`LiveConnection` بنسخة الأعمال. `MainConnection` للبناء Debug، و`LiveConnection` للبناء Release؛ لا تترك Release متصلًا بالمصدر القديم.

من مجلد مشروع OneERP، في نفس نافذة PowerShell التي ستشغّل منها الـ API، استبدل الاسمين بقواعدك المؤكدة:

```powershell
$usersCopy = 'REPLACE_WITH_VERIFIED_USERS_COPY'
$mainCopy = 'REPLACE_WITH_VERIFIED_MAIN_COPY'
$localInstance = 'localhost' # Or the actual named instance used in SSMS

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__UserConnection = "Server=$localInstance;Database=$usersCopy;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
$env:ConnectionStrings__MainConnection = "Server=$localInstance;Database=$mainCopy;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
$env:ConnectionStrings__LiveConnection = $env:ConnectionStrings__MainConnection

dotnet run --project One.API --no-launch-profile
```

متغيرات البيئة تتغلب على `appsettings.Development.json` الحالي، الذي يوجه الاتصالين إلى `EssamOneERP` على `localhost\SQLEXPRESS`. المثال لا يغيّر الملفات المتعقبة أو إعدادات ONLINE. الـ API يطبق EF migrations ويزرع الأدوار عند البدء؛ راجع `__EFMigrationsHistory` ونتيجة الاستيراد قبل تشغيله.

نجاح تصدير إحدى القاعدتين وحده لا يثبت أن كل احتياجات البرنامج نُقلت؛ راجع جرد المصدرين معًا. البناء وفحص الصياغة لا يعادلان اختبار استيراد فعلي؛ نتائج الاختبارات في [VALIDATION.md](VALIDATION.md).
