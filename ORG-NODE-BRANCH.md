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
- `1.2` (core) — دو migration: `AddUnitNodeFields` (ستون‌های واحد در `organization.OrganizationUnits`، نام جدول همان) و `AddOrgNodes` (ستون‌های شرکت/پست، جدول‌های جدید و پر کردن داده: انواع «شرکت» و «واحد» برای هر سازمان، واحدهای فعلی = «واحد» بدون والد، علامت رئیس روی پست رئیس هر شرکت). اسکریپت SQL در ریپوی میزبان است. **اجرا روی SQL Server محلی راستی‌آزمایی شد** (۱۴۰۵/۰۸/۰۷): شمار ردیف‌ها و checksum همه‌ی جدول‌ها قبل و بعد یکسان؛ همه‌ی بررسی‌های بخش B صفر؛ اجرای دوباره بی‌اثر؛ rollback و اجرای دوباره بدون خطا.
- `M6` (نفر دوم) — سه قالب آماده به‌صورت داده (ساده، معاونتی، بانکی بر اساس نمودار پست بانک، ۵۲ گره) با شِما در `Nexus.OrgStructure/Templates`؛ داخل اسمبلی جاسازی می‌شوند و تست `StructureTemplateFileTests` (۷ تست) سازگاری‌شان را بررسی می‌کند. بارگذاری در فاز ۴. رفتار برنامه تغییری نکرده. تست‌ها بعد از آن: ۲۳۳ تست، ۲۳۰ پاس، همان ۳ فِیل قدیمی.
- `M3` — `OrgStructureValidator` (کلاس خالص، بدون دیتابیس): حلقه، والد ناموجود، کد تکراری/خالی، نوع ناشناخته، قاعده‌ی «چه زیر چه» و لینک شرکت فقط روی گره‌ی بالایی؛ همه‌ی خطاها را با شماره‌ی سطر برمی‌گرداند (نه فقط اولی). ۱۲ تست واحد در `OrgStructureValidatorTests`. API گره و اکسل (M4) از همین استفاده می‌کنند. رفتار برنامه تغییری نکرده. تست‌ها: ۲۶۰ تست، ۲۵۷ پاس، همان ۳ فِیل قدیمی.
- `M4` — `OrgExcelParser` (خالص، بدون کتابخانه و دیتابیس): ردیف‌های برگه (کد، نام، کد والد، نوع، سمت مدیر اختیاری) را به درخت معتبر یا فهرست خطا با شماره‌ی سطر خود برگه تبدیل می‌کند؛ همه‌یاهیچ؛ ردیف کاملاً خالی نادیده گرفته می‌شود؛ قاعده‌ها از `OrgStructureValidator` (M3). خواندن خود فایل xlsx کار API (فاز ۲) است. نمونه‌ها: `Nexus.CompositionTests/Samples/bank-structure.csv` (۴۰ سطر از چارت بانک) و `bank-structure-faulty.csv` (شش خطای شناخته‌شده با سطر درست). ۸ تست. تست‌ها: ۲۶۸ تست، ۲۶۵ پاس، همان ۳ فِیل قدیمی.
- `M5` — داده‌ی آزمایشی ۴۱۲ گره‌ای بانک (مجمع، هیأت مدیره، ۵ کمیته، مدیریت عامل با واحدهای ستادی، معاونت‌ها و اداره‌های کل، مدیریت‌های امور، ۳۱ مدیریت شعب استان و ۳۳۱ شعبه) در `Nexus.CompositionTests/Samples/large-bank-structure.json` و `.csv` (همان ردیف‌ها، با قالب M4) و `LargeBankSeed.Rows()` برای تست‌های حجم. نام‌ها طبق قرارداد قالب بانکی (اداره کل …، مدیریت شعب استان …، شعبه …)؛ سه تست نشان می‌دهد با قاعده‌های خود قالب بانکی ساختار معتبر است. تست‌ها: ۲۷۱ تست، ۲۶۸ پاس، همان ۳ فِیل قدیمی (توجه: تست‌های `TaskSmsTests` گاهی ناپایدارند و جدا از این کار؛ در اجرای کامل پاس شدند).
- **فاز ۲ (نفر دوم) — API ساختار:** `OrgNodeService` و `/api/org-structure/nodes` (ساخت، ویرایش، مدیر، انتقال، ترتیب هم‌سطح، غیرفعال/فعال، جست‌وجو، مسیر اجداد)، `/node-types` (با رنگ و آیکون) و `/node-type-rules` (تغییر قاعده فقط فهرست ناسازگار برمی‌گرداند)، چارت نسخه‌ی ۲ (`children` تودرتو، `parentNodeId`، `nodeType`، `links`، `uiLevel`) و `/impact-preview` (روکش در حافظه؛ هیچ نوشتنی ممکن نیست). فلگ `OrgStructure:AllowNesting` پیش‌فرض خاموش؛ بدون قاعده فقط مسطح. هر تغییر در نوبت سازمان (روی SQL Server با `sp_getapplock`)، با `RowVersion` و ثبت در `OrgChangeLogs`. مجوزهای جدا `org_structure.node.*` و `manage_rules`، هر کدام با مجوز قدیمی معادل هم پذیرفته می‌شود. ستون‌های تازه: `OrganizationUnits.SortOrder` و `NodeTypes.Color/Icon` (migrationهای `AddUnitSortOrder` و `AddNodeTypeStyle`، اسکریپت `upgrade-11` در میزبان؛ شماره‌ی اسکریپت فاز ۶ و ۹ یکی جلو رفت). لینک شرکت فقط روی گره‌ی بالایی (`LinkUnitAsync` دوباره رد می‌کند) و تست `Units_BelongToACompany_ThroughTheirTopUnit` با والد واقعی پاس است. تست‌ها: `OrgNodeApiTests` (۱۱) و `OrgNodeConcurrencyTests` (۲، فقط با SQL Server واقعی: `NEXUS_TEST_SQLSERVER`؛ دو ویرایش هم‌زمان و دو انتقال هم‌زمانِ حلقه‌ساز). Composition ‏۲۷۳ تست، ۲۷۱ پاس، ۲ فِیل قدیمی؛ NexusCore.Tests ‏۱۸۱ و TaskManagement.Tests ‏۱۰۵ پاس.
- **فاز ۴ — رابطه‌ی فرعی و قالب:** نوع رابطه (`/link-types`؛ خط نقطه‌چین هیچ‌وقت مدیر حساب نمی‌شود)، قاعده‌ی خودکار (`/auto-link-rules`؛ یک هدف: وصل در لحظه‌ی ساخت؛ چند هدف: `autoLinkChoices` برای انتخاب کاربر؛ بدون هدف: اعمال نمی‌شود؛ گره‌های موجود فقط با preview و سپس apply همان طرح)، رابطه‌ها (`/links`)، سطح نمایش هر شرکت (`/companies/{id}/ui-level`) و مدیر اضافه از رابطه‌ی «مدیر حساب‌شونده» (`ExtraManagersOf` و `/users/{id}/extra-managers`؛ مدیر اصلی و بقیه‌ی رفتار عوض نمی‌شود). قالب‌ها: سه قالب آماده (M6، شناسه‌ی ثابت)، ذخیره‌ی شرکت به‌عنوان قالب (ستون تازه‌ی `StructureTemplates.Content`، migration `AddStructureTemplateContent`، اسکریپت `upgrade-12` در میزبان)، و ساخت شرکت با `templateId` (کپی؛ نوع، رابطه و سمت موجود دوباره ساخته نمی‌شود؛ اگر سازمان قاعده نداشت، جای فعلی گره‌ها اول قاعده می‌شود تا ناسازگاری پیش نیاید؛ پست ساخته نمی‌شود چون کار فاز ۶ است). preview حالا `link` و `unlink-link` و تغییر مدیر اضافه را هم نشان می‌دهد. مجوز تازه: `org_structure.manage_templates`. تست‌ها: ۹ تست تازه در `OrgNodeApiTests.Phase4.cs`. Composition ‏۲۹۳ تست، ۲۹۱ پاس، ۲ فِیل قدیمی؛ NexusCore.Tests ‏۱۸۱ و TaskManagement.Tests ‏۱۰۵ پاس.
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
