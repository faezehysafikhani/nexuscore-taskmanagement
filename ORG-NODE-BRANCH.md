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
- `M6` (نفر دوم) — سه قالب آماده به‌صورت داده (ساده، معاونتی، بانکی بر اساس نمودار پست بانک، ۵۲ گره) با شِما در `Nexus.OrgStructure/Templates`؛ داخل اسمبلی جاسازی می‌شوند و تست `StructureTemplateFileTests` (۷ تست) سازگاری‌شان را بررسی می‌کند. بارگذاری در فاز ۴. رفتار برنامه تغییری نکرده. تست‌ها بعد از آن: ۲۳۳ تست، ۲۳۰ پاس، همان ۳ فِیل قدیمی.
- فازهای بعدی: طبق پلن؛ این بخش بعد از هر فاز به‌روز می‌شود.

## تست‌های پایه
۲۲۶ تست، ۲۲۳ پاس، ۳ فِیل قدیمی و بی‌ربط به این کار:
- `OrgStructureTests.Units_BelongToACompany_ThroughTheirTopUnit`
- `EffectivePermissionTests.AnotherModulesPermission_CannotBeAssigned_ButStaysWhereItIs_AndStillWorks`
- `RecurringTaskReminderTests.IncompleteSmsSettings_AreReported_AndNothingIsSent`

## قواعد کار روی این برنچ
- فقط روی همین برنچ کار می‌شود؛ مبنای هر سه ریپو سر `feature/1405-07-28-Mahdi` است.
- امضای درگاه‌های Core، نام مجوزها و endpointهای فعلی و نام جدول `organization.OrganizationUnits` بدون اجازه عوض نمی‌شود؛ فقط فیلد اختیاری اضافه می‌شود.
- قبل از هر commit تست‌ها اجرا می‌شود و نتیجه نباید بدتر از پایه‌ی بالا باشد.
- push فقط با تأیید صاحب کار.
