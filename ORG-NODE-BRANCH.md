# ساختار سازمانی داینامیک — برنچ `feature/1405-08-07-orgNode`

> این فایل وضعیت کار روی این برنچ را برای مرور (CTO، همکاران) نشان می‌دهد و در پایان هر کار یا فاز به‌روز می‌شود.

## هدف
سامانه مدیریت وظایف تا امروز ساختار سازمانی «مسطح» دارد: واحدها (معاونت‌ها) زیر هم نمی‌روند، نوع واحد وجود ندارد و هر پست فقط یک مافوق دارد. برای چارت واقعی بانک (حدود ۴۰ گره، چند سطح، هیئت مدیره و کمیته‌ها با خط نقطه‌چین) این برنچ ساختار را **داینامیک** می‌کند:

- گره‌های تو در تو (`ParentNodeId`) و نوع گره با قاعده‌های خودش (مثلاً «مرز دامنه»)
- رابطه‌ی فرعی (نقطه‌چین / ماتریسی) علاوه بر مافوق اصلی
- قالب برای ساخت سریع ساختار
- **یک الگوریتم واحد** (`OrgGraph`) که همه‌ی بخش‌ها (مدیر، دامنه‌ی تخصیص وظیفه، جانشینی، تیم کاری، چارت) فقط از آن می‌خوانند و ساختار دومی یا فیچرفلگ دائمی وجود ندارد

هدف مهم: برای داده‌ی فعلی (واحدهای مسطح) رفتار سایر بخش‌های سامانه **دقیقاً مثل الان** بماند.

## هر سه ریپوی این کار
- core (مدل داده، الگوریتم، تست‌ها): [nexuscore-taskmanagement](https://github.com/faezehysafikhani/nexuscore-taskmanagement/tree/feature/1405-08-07-orgNode)
- میزبان تجارت (بسته‌ها، `Program.cs`، اسکریپت‌های دیتابیس): [tejaratbank-api](https://github.com/faezehysafikhani/tejaratbank-api/tree/feature/1405-08-07-orgNode)
- فرانت (چارت و پنل مدیریت): [TejaratBank-UI](https://github.com/faezehysafikhani/TejaratBank-UI/tree/feature/1405-08-07-orgNode)

## نقش این ریپو
مدل داده و migration (گره، نوع، رابطه‌ی فرعی، قالب)، سرویس مشترک `OrgGraph`، اصلاح مصرف‌کننده‌ها (`HierarchyResolver`، `HierarchyWorkAssignmentScope`، `SubstitutionService`، `DeputyWorkTeamMemberScope`، چارت)، endpointها و تست‌ها.

## وضعیت فعلی
- **فاز ۰ — پیش‌نیازها:** انجام شده است (تست‌های طلایی روی داده‌ی مسطح، مبنا و قواعد کار، این توضیح).
- `0.0` — تست‌های طلایی (`OrgGoldenTests`، ۵ تست) رفتار فعلی را روی سه چیدمان مسطح ثبت می‌کنند؛ هنوز هیچ تغییری در رفتار برنامه نیست.
- `1.1` — فیلدهای اختیاری گره (`ParentUnitId`، `NodeTypeId`، `ValidFrom/To`، `RowVersion`)، `Company` (`ParentCompanyId`، `TemplateId`، `UiLevel`)، `UnitPosition` (`IsManagerPost`، `IsCompanyHead`، `RowVersion`) و جدول‌های جدید (نوع گره، قاعده، رابطه، قالب، لاگ تغییر) با نگاشت EF و خواندن در ریپازیتوری. هیچ منطقی به آنها وصل نشده؛ migration در ۱.۲ می‌آید.
- `1.3` — `OrgGraph` و `IOrgGraphProvider`: یک گراف واحد با والد واقعی (`ParentOf`، `SubtreeOf`، `DomainOf`، شرکت مالک از گره‌ی بالایی)؛ `HierarchyResolver` همین گراف را به‌کار می‌برد. ریشه ضمنی است (بدون ردیف و بدون مدیر): والد خالی و والد=شناسه شرکت یک گراف مسطح یکسان می‌دهند (تست برابری). مصرف‌کننده‌های دیگر در ۱.۴ وصل می‌شوند.
- `1.3b` — در `docs/OrgStructure.md` سه مفهوم جدا شد: مرز سازمان مالک (شرکت)، ریشه‌ی ضمنی ساختار، و مرز هم‌معاونت (`IsDomainBoundary`).
- `1.4` — مصرف‌کننده‌ها به `OrgGraph` وصل شدند: `HierarchyWorkAssignmentScope` (منطق سطح/رتبه/هم‌معاونت به کلاس قابل‌تست `WorkAssignmentLevels` رفت)، `SubstitutionService`، snapshot در `OrgStructureService` و `DeputyWorkTeamMemberScope` (هم‌معاونت با `DomainOf`). دیگر هیچ `new UnitNode(..., null, ...)` جدا نمانده؛ `TaskPostDirectory` و `CompanyReachScope` دست‌نخورده. تست‌های طلایی روی داده‌ی مسطح بدون تغییر پاس‌اند.
- `1.5` — رئیس شرکت با `IsCompanyHead` (هر دو ملاک: علامت یا پست بدون مافوق، در یک نقطه `CompanyHeadPost`؛ پست تازه‌ی سمت بدون مافوق علامت می‌خورد) و مدیر گره: `ManagerUserId` می‌ماند و فقط وقتی خالی بود دارنده‌ی پست `IsManagerPost` مدیر است (در `OrgGraph.Build`، پس چارت و همه‌ی مصرف‌کننده‌ها یک‌جور می‌بینند). تست‌های طلایی یک اشکال در همین مسیر را هنگام کار گرفتند و اصلاح شد.
- فازهای بعدی: طبق پلن؛ این بخش بعد از هر فاز به‌روز می‌شود.

## تست‌های پایه
۲۴۱ تست، ۲۳۸ پاس (تست‌های جدید: ۳ مدل در ۱.۱، ۹ گراف/سطح در ۱.۳ و ۱.۴ و ۳ رئیس/مدیر گره در ۱.۵)، ۳ فِیل قدیمی و بی‌ربط به این کار:
- `OrgStructureTests.Units_BelongToACompany_ThroughTheirTopUnit`
- `EffectivePermissionTests.AnotherModulesPermission_CannotBeAssigned_ButStaysWhereItIs_AndStillWorks`
- `RecurringTaskReminderTests.IncompleteSmsSettings_AreReported_AndNothingIsSent`

## قواعد کار روی این برنچ
- فقط روی همین برنچ کار می‌شود؛ مبنای هر سه ریپو سر `feature/1405-07-28-Mahdi` است.
- امضای درگاه‌های Core، نام مجوزها و endpointهای فعلی و نام جدول `organization.OrganizationUnits` بدون اجازه عوض نمی‌شود؛ فقط فیلد اختیاری اضافه می‌شود.
- قبل از هر commit تست‌ها اجرا می‌شود و نتیجه نباید بدتر از پایه‌ی بالا باشد.
- push فقط با تأیید صاحب کار.
