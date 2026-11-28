# نقشه‌ی UI به بک‌اند - سامانه‌ی مدیریت پروژه (POSTBANKPMUI → nexuscore)

برنچ: `Mahdi-PB-fixes-1405-09-07-backup` در هر دو ریپو. مبنای فازهای ۱ تا ۵.

روش: همه‌ی فراخوانی‌های `api.*` فرانت (`services/backend/*.ts`، `directory/*`، `components/*`، `pages/*`) با یک اسکریپت
استخراج شد. ۱۴۷ فراخوانی پیدا شد. با فهرست همه‌ی `MapGet/MapPost/MapPut/MapDelete`های بک‌اند (۵۲۲ مسیر) تطبیق داده شد.
**همه‌ی مسیرهایی که فرانت صدا می‌زند در بک‌اند وجود دارند.** پنج مورد را اسکریپت نتوانست خودکار تطبیق دهد و دستی
بررسی شدند (همه موجودند): `GET /api/identity/assignment-limit`، `GET /api/portfolio?type=Action`،
`POST /api/project-management/projects/check?projectId=`، `POST /api/project-management/{kind}/for-projects`، و
`GET {users}` (مسیر متغیر). پس مشکل اصلی «endpoint ناموجود» نیست. مشکل‌ها در **قواعد رفتاری** هستند. برای پیدا کردنشان
سرویس‌ها خوانده شدند.

وضعیت‌ها: **درست** / **ناقص** (هست ولی قاعده‌ای کم دارد) / **پیاده‌نشده** / **ناهماهنگ** (UI و بک‌اند قاعده‌ی متفاوت دارند).
ستون «بعد از اصلاح» در گزارش نهایی پر می‌شود.

## ۱. پروژه و منشور

| کنش UI | endpoint | سرویس / متد | وضعیت | توضیح | بعد از اصلاح |
|---|---|---|---|---|---|
| فهرست / باز کردن پروژه | `GET /api/portfolio/projects`، `GET /projects/{id}` | `ProjectService.GetAsync` | ناقص | `ProjectRepository.GetByIdAsync` tenant را چک نمی‌کند (M7). | |
| بررسی پیش از ذخیره | `POST /projects/check` | `ProjectService.CheckAsync` | درست | | |
| ذخیره‌ی منشور (جدید) | `POST /projects/` | `ProjectService.CreateAsync` | ناقص | `TenantId` از بدنه‌ی درخواست گرفته می‌شود، نه از کاربر جاری (M7). `WorkCalendarId` اعتبارسنجی نمی‌شود (M2). | |
| به‌روزرسانی منشور | `PUT /projects/{id}` | `ProjectService.UpdateAsync` | ناقص | در `PendingApproval` هم ویرایش آزاد است (H3). بعد از `Approved` تغییر فیلدهای کلیدی بدون تأیید دوباره اعمال می‌شود (تصمیم ۲). مالکیت پروژه چک نمی‌شود (M7). | |
| تغییر وضعیت (فعال/متوقف/تکمیل) | `PUT /projects/{id}/status` | `ProjectService.ChangeStatusAsync` | ناقص | پروژه‌ی تأییدنشده را هم Active/OnHold/Completed می‌کند (H2). | |
| ارسال برای تأیید | `POST /projects/{id}/submit-for-approval` | `ProjectService.SubmitForApprovalAsync` | ناقص | Pending/Approved را دوباره می‌پذیرد (H5). `ScopeType`/`ScopeId` نمی‌فرستد، پس زنجیره‌ی منشورِ مخصوص پروژه اعمال نمی‌شود (M1). شکست ذخیره‌ی پروژه بعد از ساخت instance، instance یتیم می‌گذارد، چون Workflow و PM دو DbContext جدا دارند (H4). | |
| آرشیو / حذف | `POST /projects/{id}/archive`، `DELETE /projects/{id}` | `ArchiveAsync` / `DeleteAsync` | درست | حذف با `IProjectDeletionParticipant` قفل است. | |
| تیم پروژه | `PUT /team/members/set`، `POST /team/members/for-projects` | `ProjectTeamService` | درست | | |
| ذخیره‌ی پروژه‌ی جدید + ثبت‌ها و برنامه | (فرانت) `ProjectsContext.saveProject` | - | ناقص | شکست `syncRegisters` و `syncNewProjectPlan` فقط `console.warn` می‌شود و پروژه با برنامه‌ی ناقص ارسال می‌شود (M3). اگر ایجاد موفق ولی ارسال ناموفق باشد، UI روی فرم «جدید» می‌ماند و تلاش دوباره `project.name_exists` می‌دهد (M4). | |
| نمایش دلیل رد | (فرانت) `approvalComment` | `approval-center/for-subjects` | پیاده‌نشده | `ProjectsContext` توضیح آخرین تصمیم را در `approvalComment` می‌گذارد، ولی هیچ صفحه‌ای آن را نشان نمی‌دهد. | |
| قفل تب‌ها تا ذخیره‌ی منشور | (فرانت) | - | درست | بدون شناسه‌ی پروژه هیچ ثبتی ممکن نیست؛ بک‌اند هم `projectId` می‌خواهد. | |
| قفل تب زمان‌بندی چابک تا تأیید | (فرانت) | `AgileTaskService` | ناهماهنگ | UI تا تأیید قفل می‌کند؛ بک‌اند ساخت تسک را برای پروژه‌ی تأییدنشده رد نمی‌کند. | |
| دکمه‌ی «ارسال» | (فرانت) | - | ناهماهنگ | برای پروژه‌ی `Approved` هم نمایش داده می‌شود. | |

## ۲. چرخه‌ی تأیید (کارتابل / مرکز تأیید)

| کنش UI | endpoint | سرویس / متد | وضعیت | توضیح | بعد از اصلاح |
|---|---|---|---|---|---|
| فهرست کارتابل | `GET /api/workflow/approval-center` | `WorkflowInstanceService.ListPendingForApproverAsync` | درست | | |
| تاریخچه‌ی تأیید آیتم‌ها | `POST /approval-center/for-subjects` | `ListForSubjectsAsync` | درست | | |
| تأیید / رد | `POST /approval-center/{id}/approve` و `/reject` | `WorkflowInstanceService.DecideAsync` | ناقص | concurrency token ندارد؛ دو تصمیم هم‌زمان هر دو ثبت می‌شوند (H5). خودِ ارسال‌کننده می‌تواند منشور را تأیید کند و ارسال‌کننده روی instance ذخیره نمی‌شود (H6). `GetByIdAsync` tenant را چک نمی‌کند (M7). | |
| ساخت instance | (داخلی) `WorkflowApprovalRequester` | `RequestApprovalAsync` | ناقص | instance دوم برای subject در جریان ساخته نمی‌شود (درست). ولی مرحله‌ی اجباری با تأییدکننده‌ی حل‌نشده با تأییدکننده‌ی خالی ساخته می‌شود و هر کسی می‌تواند تأییدش کند (L). | |
| همگام‌سازی پروژه با تصمیم | (داخلی) `ProjectApprovalGrantedHandler` / `RejectedHandler` | رویداد بعد از `SaveChanges` | ناقص | اگر handler بعد از commit تصمیم شکست بخورد، پروژه برای همیشه Pending می‌ماند و راه بازیابی ندارد (H4). | |
| اعلان به تأییدکننده و ارسال‌کننده | - | - | پیاده‌نشده | هیچ اعلان داخلی برای ارسال یا تصمیم ثبت نمی‌شود (M6). | |
| تنظیمات گردش کار (عمومی و پروژه) | `GET/PUT /api/integrations/project-workflow/settings…`، `…/projects/{id}/settings/{key}` | `ProjectWorkflowSettingsService` | ناهماهنگ | گروه `activity_progress` تأیید فعالیت آبشاری و تسک چابک را هم شامل می‌شود. فرانت فقط «تأیید پیشرفت» را صدا می‌زند (`progress-submissions`، `status-submissions`)، نه `activities/{id}/submit-for-approval` یا `agile/tasks/{id}/submit-for-approval` (M5، تصمیم ۵). | |
| جانشینی تأیید | `/api/project-management/delegations…` | `ItemDelegationService` | درست | | |

## ۳. زمان‌بندی آبشاری و چابک، تقویم

| کنش UI | endpoint | سرویس / متد | وضعیت | توضیح | بعد از اصلاح |
|---|---|---|---|---|---|
| برنامه (Gantt) | `GET /waterfall/schedule/plan`، `POST /schedule/plans` | `ScheduleService` | درست | تاریخ‌ها و مدت‌ها در UI از همین پاسخ نگاشت می‌شوند (`planningMappers.ts`). | |
| فعالیت: ایجاد/ویرایش/حذف | `POST/PUT/DELETE /waterfall/activities…` | `ActivityService` | ناقص | قاعده‌ی «مجموع وزن‌ها ۱۰۰» فقط هنگام ارسال پروژه چک می‌شود؛ بعد از ارسال، ویرایش وزن‌ها آزاد است (M9). | |
| پیش‌نیاز | `PUT /waterfall/dependencies/predecessor` | `ActivityDependencyService` | درست | | |
| پیشرفت واقعی / گزارش پیشرفت | `PUT /activities/{id}/actual-progress`، `/progress-submissions…` | `ActivityProgressSubmissionService` | درست | | |
| خط مبنا | `GET/POST/DELETE /waterfall/baselines…` | `ScheduleBaselineService` | درست | | |
| منحنی S، خروجی MS Project | `GET /waterfall/s-curve`، `/msproject/export` | | درست | | |
| تسک چابک، اسپرینت، وضعیت | `/agile/tasks…`، `/agile/sprints…`، `/agile/status-submissions…` | `AgileTaskService` | ناهماهنگ | بالا را ببینید (قفل تا تأیید). | |
| انتخاب تقویم پروژه | `GET /api/calendar/work-calendars` | `ProjectService.Create/Update` | ناقص | شناسه‌ی تقویم بدون بررسی وجود و tenant ذخیره می‌شود (M2). | |
| ویرایش تقویم | `PUT /api/calendar/work-calendars/{id}` | `WorkCalendarService` | ناقص | هیچ هشداری درباره‌ی پروژه‌های تأییدشده‌ای که برنامه‌شان تغییر می‌کند داده نمی‌شود (تصمیم ۴). | |

## ۴. ثبت‌های پروژه

| تب | endpoint | ایجاد/ویرایش | قفل در Pending | ارسال تکراری | وضعیت |
|---|---|---|---|---|---|
| ریسک | `/project-management/risks…` | `RiskService` | فقط حذف قفل است، ویرایش آزاد | پذیرفته می‌شود | ناقص |
| ذی‌نفع | `/project-management/stakeholders…` | `StakeholderService` | فقط حذف | پذیرفته می‌شود | ناقص |
| گزارش وضعیت | `/project-management/progress-updates…` | `ProgressService` | فقط حذف | پذیرفته می‌شود | ناقص |
| دلیل تأخیر | `/project-management/delay-reasons…` | `DelayReasonService` | فقط حذف | پذیرفته می‌شود | ناقص |
| مستند | `/project-management/documents…` | `ProjectDocumentService` | فقط حذف | پذیرفته می‌شود | ناقص |
| قرارداد / الحاقیه / فاکتور | `/project-management/contracts…` | `Contract*Service` | ویرایش و حذف قفل است | رد می‌شود | درست |
| اقدام | `/api/actions…` | `ActionItemService` | فقط حذف | پذیرفته می‌شود | ناقص |

(«ارسال تکراری پذیرفته می‌شود»: `WorkflowApprovalRequester` instance دوم نمی‌سازد، پس خطر دوبار ثبت در کارتابل نیست. ولی آیتم
`Approved` را می‌شود دوباره ارسال کرد و به Pending برگرداند.)

## ۵. داشبورد، گزارش‌ها، پورتفولیو

| کنش UI | endpoint | وضعیت | توضیح | بعد از اصلاح |
|---|---|---|---|---|
| داشبورد مدیریتی | (فرانت) فیلتر `approvalStatus === 'Approved'` | ناهماهنگ | سه معیار مختلف برای «پروژه‌ی فعال» (M8): داشبورد بک‌اند (`Nexus.Reporting`) بر اساس `Status`، آنالیتیکس بر اساس `ApprovalStatus`، فیلترهای UI فقط `approvalStatus === 'Approved'`. | |
| گزارش واحدها / ماتریس وضعیت / ارزیابی مدیران | `/api/reporting/units/*`، `/project-managers/evaluation` | ناهماهنگ | همان M8 | |
| پورتفولیو | `/api/portfolio…` | درست | | |
| هم‌راستایی استراتژی | `/api/integrations/project-strategy-alignment…` | درست | | |

## ۶. امنیت دسترسی (M7)

- هیچ DbContext ماژول‌های پروژه query filter بر اساس tenant ندارد. `GetByIdAsync` در `ProjectRepository`، ثبت‌ها و
  `WorkflowInstanceRepository` با هر شناسه‌ای از هر tenant کار می‌کند.
- endpointهای پروژه فقط مجوز کلی (`Projects.Edit`، `Projects.Submit`) را چک می‌کنند. UI دکمه‌ها را فقط به مدیر، مالک
  یا Admin نشان می‌دهد (`isPMOrOwnerOrAdmin`)، ولی بک‌اند این را چک نمی‌کند.

## خارج از دامنه‌ی UI (ساخته نمی‌شود)

- endpointهای بک‌اندی که فرانت PM صدا نمی‌زند: KPI (`/project-management/kpis`)، Deliverables، History، Meetings، Ticketing،
  governance-roles تیم، `waterfall/msproject/import` از مسیر جدا. این‌ها فقط فهرست شده‌اند.
- ماژول منابع / بودجه‌ی مستقل در UI وجود ندارد.

## وضعیت بعد از اصلاح (فازهای ۱ تا ۵)

| مورد | وضعیت قبل | بعد از اصلاح | کجا |
|---|---|---|---|
| H2 تغییر وضعیت پروژه‌ی تأییدنشده | ناقص | درست: `project.not_approved` (409). فرانت فقط «برنامه‌ریزی» را برای پروژه‌ی تأییدنشده نشان می‌دهد. | `ProjectService.ChangeStatusAsync`، `Forms.tsx` |
| H3 ویرایش منشور در Pending | ناقص | درست: `project.pending_approval` (409). فرانت فرم را فقط‌خواندنی می‌کند و بنر نشان می‌دهد. | `ProjectService.UpdateAsync`، `ProjectPages.tsx` |
| تصمیم ۲ ویرایش بعد از تأیید | پیاده‌نشده | درست: تغییر مدیر/تاریخ/هزینه/هدف/نیازمندی → Pending و شروع دوباره‌ی گردش منشور؛ Status فعال می‌ماند. | `Project.ChangesApprovedCharter`، `StartApprovalAsync` |
| H5 ارسال تکراری | ناقص | درست: `project.already_pending` / `project.already_approved`. | `SubmitForApprovalAsync` |
| H5 تصمیم هم‌زمان | پیاده‌نشده | درست: `WorkflowInstance.RowVersion` (concurrency token) و `workflow.concurrent_decision` (409). | `WorkflowInstanceService.DecideAsync` |
| H4 instance یتیم | ناقص | درست: شکست ذخیره‌ی پروژه → withdraw (Status=3). | `IApprovalTracker`، `WorkflowApprovalTracker` |
| H4 پروژه‌ی گیرکرده در Pending | پیاده‌نشده | درست: همگام‌سازی هنگام خواندن، ارسال، ویرایش، و `POST /projects/{id}/approval/sync`. فرانت هنگام بارگذاری صدا می‌زند. | `SyncWithApprovalAsync`، `ProjectsContext` |
| M1 scope منشور | ناقص | درست: `ScopeType=Project`، fallback به General حفظ شد. | `ProjectService.SubjectOf` |
| H6 / تصمیم ۳ خودتأییدی | پیاده‌نشده | درست: `SubmittedByUserId` روی instance. مرحله‌ی خودِ ارسال‌کننده به OrgManager می‌رود؛ اگر نبود رد. تصمیم ارسال‌کننده رد می‌شود (`workflow.self_approval`). | `WorkflowApprovalRequester`، `DecideAsync` |
| L تأییدکننده‌ی حل‌نشده | ناقص | درست: `workflow.approver_missing` با نام مرحله (برای همه‌ی موجودیت‌ها). | `WorkflowApprovalRequester` |
| M6 اعلان | پیاده‌نشده | درست (برای منشور): اعلان به تأییدکننده‌ی مرحله‌ی بعد و به ارسال‌کننده (با دلیل رد). | `ApprovalNotifications` (Bundles.Platform) |
| نمایش دلیل رد | پیاده‌نشده | درست: بنر روی تب منشور. | `ProjectPages.tsx` |
| M3 / M4 ایجاد پروژه | ناقص | درست: ارسال ناقص انجام نمی‌شود (`project.partially_saved`) و پروژه برای ویرایش باز می‌شود. | `ProjectsContext.saveProject`، `App.tsx` |
| قفل تب زمان‌بندی چابک | ناهماهنگ | برداشته شد (بک‌اند معادلی ندارد و پروژه‌ی چابک جدید تسک‌هایش را قبل از ارسال می‌گیرد). | `ProjectPages.tsx` |
| M2 تقویم | ناقص | درست: `project.calendar_not_found`. | `IProjectCalendarLookup` |
| تصمیم ۴ تغییر تقویم | ناقص | درست: `GET /projects/calendar-usage` و تأیید قبل از ذخیره. | `ProjectService.GetCalendarUsageAsync`، `App.tsx` |
| M9 وزن‌ها بعد از ارسال | ناقص | هشدار (نه رد): `ScheduleDto.WeightsTotalNot100` و بنر در تب زمان‌بندی. | `ScheduleService` |
| M8 پروژه‌ی فعال | ناهماهنگ | درست: `Project.IsActive` در داشبورد و آنالیتیکس؛ `isActiveProject` در فرانت. | `DashboardService`، `AnalyticsService` |
| ثبت‌ها: قفل در Pending | ناقص | درست برای ریسک، ذی‌نفع، گزارش وضعیت، دلیل تأخیر، مستند: `approval.pending_edit` / `approval.already_pending`. اقدام‌ها تغییر نکردند. | سرویس‌های همان ماژول‌ها |
| M5 / تصمیم ۵ | ناهماهنگ | UI از قبل فقط «تأیید پیشرفت» را نشان می‌دهد و submit فعالیت/تسک را صدا نمی‌زند؛ تغییری لازم نبود. | - |
| M7 tenant و مالکیت | ناقص | درست: فیلتر tenant در `GetByIdAsync` مخزن‌ها؛ TenantId ایجاد از کاربر جاری؛ مدیر/مالک/`Projects.ManageAll`. | مخزن‌ها، `ProjectEndpoints`، `CheckCanManage` |
