// Realistic sample data for a TMPB TEST server, created through the system's own API (same rules and
// permissions as the UI): company «شرکت فناوران پارس» with 5 units and posts, 18 people (password Test@Pass#2026),
// two roles, 5 teams, ~120 tasks and 5 projects, comments, time entries, meetings, notes, chat, requests.
// Never run it on production. Node 18+:
//   API=http://localhost:5199 ADMIN_PASSWORD='...' node seed-demo-data.mjs
// Re-running skips what exists (people, teams, tasks by title); comments and time are added only for new tasks.
// The login limit (30 per 5 minutes per client) is exceeded by one run: on a test server start the API with
// Identity__LoginProtection__MaxLoginAttemptsPerClient=2000, or the script waits and retries.
// Overdue tasks: the API refuses past due dates by design. The script writes overdue-ids.txt (open tasks it made);
// on a test server move their due date back, e.g. per id:
//   UPDATE task_management.Tasks SET DueDate = DATEADD(day, -3, CAST(GETDATE() AS date)) WHERE Id = '<id>' AND Status IN (0, 1);
const API = process.env.API || 'http://localhost:5199';
const TENANT = '11111111-1111-1111-1111-111111111111';
const PASSWORD = 'Test@Pass#2026';
const log = (...a) => console.log(...a);
let rnd = 20261124;
const rand = () => ((rnd = (rnd * 1103515245 + 12345) % 2147483648) / 2147483648);
const pick = (arr) => arr[Math.floor(rand() * arr.length)];
const pickN = (arr, n) => [...arr].sort(() => rand() - 0.5).slice(0, n);
const chance = (p) => rand() < p;

async function call(token, method, path, body, form) {
  const headers = { Authorization: `Bearer ${token}` };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(API + path, { method, headers, body: form ?? (body !== undefined ? JSON.stringify(body) : undefined) });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${text.slice(0, 300)}`);
  return text ? JSON.parse(text) : null;
}
async function login(identifier, password) {
  for (let attempt = 0; attempt < 20; attempt++) {
    try { return await loginOnce(identifier, password); }
    catch (err) { if (!String(err).includes('too_many_requests')) throw err; console.log('login throttled, waiting 30s'); await new Promise((r) => setTimeout(r, 30000)); }
  }
  throw new Error('login kept being throttled');
}
async function loginOnce(identifier, password) {
  const r = await fetch(`${API}/api/identity/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ identifier, password }) }).then((x) => x.json());
  if (r.accessToken) return r.accessToken;
  const ticket = r.sessionLimit?.ticket;
  if (!ticket) throw new Error(`login ${identifier}: ${JSON.stringify(r).slice(0, 200)}`);
  const c = await fetch(`${API}/api/identity/auth/login/complete`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ticket, closeSessionIds: [], closeOldest: true }) }).then((x) => x.json());
  return c.accessToken;
}
function nationalCode() {
  for (;;) {
    const d = Array.from({ length: 9 }, () => Math.floor(rand() * 10));
    if (new Set(d).size === 1) continue;
    const s = d.reduce((acc, v, i) => acc + v * (10 - i), 0) % 11;
    return d.join('') + (s < 2 ? s : 11 - s);
  }
}
const iso = (d) => d.toISOString().slice(0, 10);
const addDays = (d, n) => { const x = new Date(d); x.setUTCDate(x.getUTCDate() + n); return x; };
const today = new Date(new Date().toISOString().slice(0, 10) + 'T00:00:00Z');

// ------------------------------------------------------------------ people
const PEOPLE = [
  // [first, last, team, isManager, title]
  ['رضا', 'کریمی', 'tech', true, 'مدیر فنی'],
  ['سارا', 'رضایی', 'product', true, 'مدیر محصول'],
  ['علی', 'مرادی', 'tech', false, 'توسعه‌دهندهٔ ارشد بک‌اند'],
  ['نگار', 'سلیمانی', 'product', false, 'طراح تجربهٔ کاربری'],
  ['محمد', 'حسینی', 'tech', false, 'توسعه‌دهندهٔ فرانت‌اند'],
  ['فاطمه', 'جعفری', 'tech', false, 'کارشناس تست نرم‌افزار'],
  ['حسین', 'نوری', 'tech', false, 'مهندس زیرساخت'],
  ['زهرا', 'محمدی', 'support', true, 'سرپرست پشتیبانی'],
  ['امیر', 'صادقی', 'support', false, 'کارشناس پشتیبانی'],
  ['مهسا', 'کاظمی', 'support', false, 'کارشناس پشتیبانی'],
  ['بهروز', 'یزدانی', 'finance', true, 'مدیر مالی و اداری'],
  ['لیلا', 'شریفی', 'finance', false, 'کارشناس حسابداری'],
  ['کامران', 'افشار', 'finance', false, 'کارشناس منابع انسانی'],
  ['شیرین', 'طاهری', 'marketing', true, 'مدیر بازاریابی و فروش'],
  ['پویا', 'رحمانی', 'marketing', false, 'کارشناس فروش'],
  ['الناز', 'قاسمی', 'marketing', false, 'کارشناس محتوا و شبکه‌های اجتماعی'],
  ['مهدی', 'اکبری', 'product', false, 'تحلیل‌گر کسب‌وکار'],
  ['نرگس', 'باقری', 'marketing', false, 'کارشناس ارتباط با مشتری'],
];
const TEAMS = { tech: 'تیم فنی', product: 'تیم محصول', support: 'تیم پشتیبانی', finance: 'تیم مالی و اداری', marketing: 'تیم بازاریابی و فروش' };

// ------------------------------------------------------------------ work per team
const WORK = {
  tech: {
    tags: ['بک‌اند', 'فرانت‌اند', 'زیرساخت', 'امنیت', 'باگ', 'بهینه‌سازی'],
    titles: [
      ['رفع خطای ورود با شمارهٔ همراه در اندروید', 'کاربران گوشی‌های اندروید ۱۲ بعد از وارد کردن کد تأیید به صفحهٔ ورود برمی‌گردند. لاگ سرور و نسخهٔ مرورگر بررسی شود.'],
      ['مهاجرت پایگاه داده به SQL Server 2022', 'نسخهٔ پشتیبان کامل، اجرای آزمایشی روی سرور تست و برنامهٔ زمان‌بندی قطعی کوتاه برای سرور اصلی.'],
      ['پیاده‌سازی صفحه‌بندی در گزارش فعالیت‌ها', 'گزارش با بیش از ۵۰۰۰ ردیف کند است؛ صفحه‌بندی سمت سرور و ایندکس روی تاریخ اضافه شود.'],
      ['به‌روزرسانی گواهی SSL دامنهٔ اصلی', 'گواهی فعلی تا آخر ماه معتبر است. گواهی جدید تهیه و روی IIS و load balancer نصب شود.'],
      ['بهینه‌سازی زمان بارگذاری بورد', 'هدف: بارگذاری کمتر از ۲ ثانیه برای ۳۰۰ فعالیت.'],
      ['راه‌اندازی پشتیبان‌گیری شبانه خودکار', 'اسکریپت backup.ps1 در Task Scheduler ساعت ۲ بامداد و نگهداری ۱۴ نسخه.'],
      ['افزودن احراز هویت دومرحله‌ای برای مدیران', 'برای همهٔ حساب‌های مدیر، ورود با کد پیامکی اجباری شود.'],
      ['بازنویسی سرویس ارسال پیامک', 'جایگزینی کتابخانهٔ قدیمی و افزودن تلاش مجدد در صورت خطای سرویس‌دهنده.'],
      ['بررسی هشدارهای امنیتی وابستگی‌ها', 'خروجی npm audit و dotnet list package --vulnerable بررسی و موارد بحرانی رفع شوند.'],
      ['طراحی API گزارش ساعت کار تیم', 'خروجی هفتگی بر اساس تیم و پروژه، با امکان دریافت Excel.'],
      ['رفع کندی جستجو در گفتگوها', null],
      ['نوشتن تست خودکار برای فرم ثبت فعالیت', 'پوشش حالت‌های موعد تعطیل، مسئول چندنفره و پیوست.'],
      ['پایش مصرف حافظهٔ سرور برنامه', 'از هفتهٔ گذشته مصرف حافظه رو به افزایش است؛ احتمال نشت حافظه بررسی شود.'],
      ['ارتقای Node.js سرور فرانت به نسخهٔ ۲۲', null],
      ['مستندسازی API برای تیم یکپارچه‌سازی', 'Swagger کامل با نمونهٔ درخواست و پاسخ برای سرویس‌های فعالیت و تقویم.'],
    ],
  },
  product: {
    tags: ['طراحی', 'نیازمندی', 'تحقیق کاربر', 'نقشهٔ راه'],
    titles: [
      ['طراحی صفحهٔ جدید داشبورد مدیران', 'شاخص‌های کلیدی: فعالیت‌های عقب‌افتاده، بار کاری تیم‌ها و روند تکمیل هفتگی.'],
      ['مصاحبه با ۵ مشتری دربارهٔ گزارش‌ها', 'هدف: فهمیدن اینکه مدیران کدام گزارش را هر هفته باز می‌کنند و چه چیزی کم دارند.'],
      ['تدوین نقشهٔ راه سه‌ماههٔ محصول', 'اولویت‌بندی بر اساس درخواست مشتریان و ظرفیت تیم فنی.'],
      ['نهایی‌کردن سند نیازمندی ماژول قراردادها', null],
      ['بازطراحی فرم ثبت درخواست', 'تعداد فیلدها کم شود و دسته‌بندی هوشمند پیشنهاد داده شود.'],
      ['تحلیل بازخورد کاربران از نسخهٔ ۲.۴', 'دسته‌بندی ۱۲۰ بازخورد ثبت‌شده و استخراج ۱۰ مشکل پرتکرار.'],
      ['طراحی حالت تیرهٔ برنامهٔ موبایل', null],
      ['آماده‌سازی دموی محصول برای نمایشگاه', 'سناریوی ۱۰ دقیقه‌ای با دادهٔ نمونه و نسخهٔ آفلاین.'],
      ['بررسی رقبا در حوزهٔ مدیریت پروژه', 'مقایسهٔ قیمت، امکانات و تجربهٔ کاربری سه رقیب اصلی.'],
      ['تعریف شاخص‌های موفقیت نسخهٔ جدید', 'نرخ استفادهٔ هفتگی، زمان ثبت فعالیت و رضایت کاربران.'],
    ],
  },
  support: {
    tags: ['تیکت', 'آموزش', 'مشتری'],
    titles: [
      ['پاسخ به تیکت‌های باز شرکت آریا', '۷ تیکت باز دربارهٔ دسترسی کاربران و گزارش‌ها.'],
      ['تهیهٔ ویدیوی آموزشی ثبت ساعت کار', 'ویدیوی ۳ دقیقه‌ای برای کاربران جدید.'],
      ['آموزش حضوری کاربران واحد مالی', 'جلسهٔ دو ساعته در دفتر مشتری؛ هماهنگی با آقای یزدانی.'],
      ['به‌روزرسانی راهنمای کاربری نسخهٔ جدید', null],
      ['پیگیری مشکل همگام‌سازی LDAP مشتری', 'کاربران غیرفعال در Active Directory هنوز وارد سامانه می‌شوند.'],
      ['گزارش ماهانهٔ رضایت مشتریان', 'جمع‌بندی نظرسنجی بعد از بستن تیکت‌ها.'],
      ['تماس با مشتریانی که بیش از ۳۰ روز وارد نشده‌اند', null],
      ['تکمیل پایگاه دانش پرسش‌های پرتکرار', '۲۰ پرسش پرتکرار با پاسخ کوتاه و تصویر.'],
      ['نصب نسخهٔ جدید روی سرور شرکت پارسیان', 'هماهنگی زمان قطعی با واحد فناوری مشتری.'],
    ],
  },
  finance: {
    tags: ['مالی', 'منابع انسانی', 'خرید', 'قرارداد'],
    titles: [
      ['تهیهٔ گزارش مالی سه‌ماههٔ پاییز', 'صورت سود و زیان و جریان نقدی برای جلسهٔ هیئت‌مدیره.'],
      ['تمدید قرارداد اجارهٔ دفتر مرکزی', 'مذاکره برای افزایش حداکثر ۲۰ درصدی و تمدید دوساله.'],
      ['پرداخت حقوق و مزایای آذرماه', null],
      ['جذب کارشناس پشتیبانی جدید', 'آگهی، غربالگری رزومه‌ها و هماهنگی مصاحبهٔ فنی.'],
      ['خرید ۴ دستگاه لپ‌تاپ برای تیم فنی', 'استعلام از سه تأمین‌کننده و تأیید مدیر فنی.'],
      ['تسویهٔ صورت‌حساب‌های معوق مشتریان', 'پیگیری ۹ فاکتور با بیش از ۶۰ روز تأخیر.'],
      ['برگزاری دورهٔ آموزشی ایمنی کار', null],
      ['تنظیم بودجهٔ سال آینده', 'دریافت برآورد هزینهٔ هر واحد تا پایان ماه.'],
      ['ارسال لیست بیمهٔ تأمین اجتماعی', null],
    ],
  },
  marketing: {
    tags: ['فروش', 'محتوا', 'کمپین', 'نمایشگاه'],
    titles: [
      ['کمپین معرفی نسخهٔ جدید در لینکدین', 'سه پست، یک ویدیوی کوتاه و یک وبینار معرفی.'],
      ['پیگیری پیش‌فاکتور شرکت آریا', 'پیش‌فاکتور ۱۲۰ کاربره؛ پاسخ تا پنجشنبه.'],
      ['تهیهٔ کاتالوگ محصول برای نمایشگاه', null],
      ['جلسهٔ دمو با بانک ملت', 'نمایش ماژول تأیید و گزارش‌ها برای واحد فناوری بانک.'],
      ['نوشتن مقالهٔ مدیریت زمان در تیم‌های توزیع‌شده', null],
      ['به‌روزرسانی صفحهٔ قیمت‌گذاری سایت', 'افزودن پلن سازمانی و پرسش‌های پرتکرار.'],
      ['تحلیل نتایج کمپین ایمیلی آبان', 'نرخ باز شدن، کلیک و ثبت‌نام آزمایشی.'],
      ['تماس با ۲۰ سرنخ فروش نمایشگاه', null],
      ['تهیهٔ گزارش فروش ماهانه برای مدیرعامل', 'مقایسه با ماه قبل و پیش‌بینی ماه آینده.'],
    ],
  },
};
const CLIENTS = ['شرکت آریا', 'بانک ملت', 'هلدینگ پارسیان', 'گروه صنعتی سپاهان', 'بیمهٔ آسیا', 'شرکت نفت پاسارگاد', 'دانشگاه شریف', 'فروشگاه‌های رفاه'];
const PER_CLIENT = {
  tech: [['رفع گزارش خطای {c} در خروجی Excel', 'خروجی گزارش برای بیش از ۱۰۰۰ ردیف ناقص است.'], ['تنظیم دسترسی کاربران جدید {c}', null], ['بررسی کندی سامانه در سرور {c}', 'زمان پاسخ صفحهٔ بورد در ساعات اوج بیش از ۵ ثانیه است.']],
  support: [['پاسخ به تیکت‌های هفتهٔ {c}', null], ['آموزش آنلاین کاربران {c}', 'وبینار یک ساعته برای مدیران واحدها.'], ['نصب به‌روزرسانی روی سرور {c}', null]],
  finance: [['صدور فاکتور قرارداد {c}', null], ['پیگیری پرداخت معوق {c}', 'دو فاکتور با بیش از ۴۵ روز تأخیر.']],
  marketing: [['پیگیری پیش‌فاکتور {c}', 'پاسخ مشتری تا پایان هفته لازم است.'], ['ارسال پیشنهاد تمدید قرارداد به {c}', null]],
  product: [['جمع‌بندی درخواست‌های امکانات {c}', 'درخواست‌های ثبت‌شده در سه ماه اخیر دسته‌بندی شود.']],
};
for (const [team, list] of Object.entries(PER_CLIENT)) {
  for (const [t, d] of list) for (const c of CLIENTS.slice(0, team === 'product' ? 4 : 6)) WORK[team].titles.push([t.replace('{c}', c), d]);
}

const PROJECTS = [
  ['tech', 'پروژهٔ راه‌اندازی نسخهٔ ۳ سامانه', ['تحلیل نیازمندی‌ها', 'طراحی معماری', 'پیاده‌سازی ماژول‌های اصلی', 'تست یکپارچه', 'استقرار روی سرور اصلی']],
  ['product', 'پروژهٔ بازطراحی تجربهٔ کاربری', ['مصاحبه با کاربران', 'طراحی وایرفریم', 'طراحی نهایی', 'آزمون کاربردپذیری']],
  ['marketing', 'پروژهٔ حضور در نمایشگاه الکامپ', ['رزرو غرفه', 'طراحی غرفه', 'تهیهٔ کاتالوگ و هدایا', 'هماهنگی تیم حاضر', 'پیگیری سرنخ‌ها']],
  ['finance', 'پروژهٔ استقرار نرم‌افزار حقوق و دستمزد', ['انتخاب نرم‌افزار', 'انتقال اطلاعات پرسنل', 'اجرای آزمایشی', 'اجرای نهایی']],
  ['support', 'پروژهٔ راه‌اندازی مرکز تماس', ['خرید سامانهٔ تلفنی', 'آموزش کارشناسان', 'تدوین اسکریپت پاسخگویی']],
];
const COMMENTS = [
  'بررسی کردم، تا فردا نتیجه را اعلام می‌کنم.', 'فایل نهایی را پیوست کنید لطفاً.', 'این مورد به هماهنگی با واحد مالی نیاز دارد.',
  'انجام شد؛ لطفاً بازبینی کنید.', 'آیا موعد قابل تمدید است؟ یک روز دیگر لازم دارم.', 'با مشتری تماس گرفتم، منتظر پاسخشان هستیم.',
  'نسخهٔ اول آماده است، بازخوردتان را بنویسید.', 'این را در جلسهٔ شنبه مطرح کنیم.', 'مشکل از تنظیمات سرور بود، اصلاح شد.',
  'لطفاً اولویت این کار را بالا ببرید، مشتری منتظر است.', 'پیش‌نویس را در پوشهٔ مشترک گذاشتم.', 'موافقم، با همین روش جلو برویم.',
];

async function main() {
  const admin = await login(process.env.ADMIN_USER || 'admin', process.env.ADMIN_PASSWORD || 'Admin@12345');
  log('admin signed in');

  // Working days ahead (the server refuses days off for new due dates)
  const sched = await call(admin, 'GET', `/api/calendar/scheduling-days?from=${iso(today)}&to=${iso(addDays(today, 60))}`);
  const off = new Set((sched.offDays || []).map((d) => d.date.slice(0, 10)));
  const isWorking = (d) => (sched.usesWorkCalendar ? !off.has(iso(d)) : d.getUTCDay() !== 5);
  const workDays = [];
  for (let i = 0; i <= 45; i++) { const d = addDays(today, i); if (isWorking(d)) workDays.push(iso(d)); }
  const dueSoon = () => workDays[Math.min(workDays.length - 1, Math.floor(rand() * rand() * 30))];

  // Roles
  const catalog = (await call(admin, 'GET', '/api/identity/permissions')).flatMap((m) => m.permissions);
  const permId = (names) => names.map((n) => catalog.find((p) => p.name === n)?.id).filter(Boolean);
  const roles = await call(admin, 'GET', `/api/identity/roles?tenantId=${TENANT}`);
  const ensureRole = async (name, description, perms) => {
    let role = roles.find((r) => r.name === name);
    if (!role) role = await call(admin, 'POST', '/api/identity/roles', { tenantId: TENANT, name, description });
    await call(admin, 'PUT', `/api/identity/roles/${role.id}/permissions`, { permissionIds: permId(perms) });
    return role.id;
  };
  const staffPerms = ['Tasks.View', 'Tasks.Create', 'Tasks.Edit', 'Tasks.Comment', 'Tasks.UploadFiles', 'Tasks.Assign', 'Timesheets.Log', 'Requests.Submit',
    'Notes.Manage', 'Meetings.View', 'Meetings.Create', 'Workflow.View', 'groups.view_own', 'groups.manage_own', 'work_calendars.view', 'Membership.View'];
  const managerPerms = [...staffPerms, 'Tasks.Delete', 'Tasks.ManageTags', 'Tasks.ManageRecurring', 'Workflow.Approve', 'Workflow.Reject', 'Timesheets.ViewAll',
    'Requests.Handle', 'Meetings.Edit', 'Meetings.Delete', 'Meetings.InviteHigher', 'users.view'];
  const staffRole = await ensureRole('کارمند', 'کار روزمره: فعالیت، ساعت کار، درخواست، یادداشت و جلسه', staffPerms);
  const managerRole = await ensureRole('مدیر واحد', 'به‌علاوهٔ تأیید، رسیدگی به درخواست‌ها و دیدن ساعت کار تیم', managerPerms);
  log('roles ready');

  // Users (skipped when they already exist: same name)
  const existing = (await call(admin, 'GET', '/api/identity/users?pageSize=200')).items;
  const users = [];
  for (const [first, last, team, isManager, title] of PEOPLE) {
    let u = existing.find((x) => x.firstName === first && x.lastName === last);
    if (!u) {
      const phone = '0912' + String(Math.floor(1000000 + rand() * 8999999));
      u = await call(admin, 'POST', '/api/identity/users', {
        address: 'تهران، خیابان ولیعصر، برج فناوری پارس، طبقهٔ ۸', fatherName: pick(['محمد', 'علی', 'حسن', 'رضا', 'احمد', 'مهدی']), birthDate: `19${80 + Math.floor(rand() * 18)}-0${1 + Math.floor(rand() * 9)}-1${Math.floor(rand() * 9)}`,
        avatarUrl: '', tenantId: TENANT, username: nationalCode(), firstName: first, lastName: last, phoneNumber: phone, password: PASSWORD, email: null, isActive: true,
      });
      await call(admin, 'PUT', `/api/identity/users/${u.id}/roles`, { roleIds: [isManager ? managerRole : staffRole] });
    }
    users.push({ id: u.id, username: u.username, name: `${first} ${last}`, team, isManager, title });
  }
  log(`users: ${users.length}`);
  const tokens = new Map();
  const tokenOf = async (u) => { if (!tokens.has(u.id)) tokens.set(u.id, await login(u.username, PASSWORD)); return tokens.get(u.id); };
  const byTeam = (t) => users.filter((u) => u.team === t);
  const managerOf = (t) => byTeam(t).find((u) => u.isManager);

  // Org chart: company, one unit per team, posts (staff under their manager) and appointments
  const OS = '/api/org-structure';
  const todayIran = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Tehran' }).format(new Date());
  const UNITS = { tech: 'واحد فنی', product: 'واحد محصول', support: 'واحد پشتیبانی', finance: 'واحد مالی و اداری', marketing: 'واحد بازاریابی و فروش' };
  let company = (await call(admin, 'GET', `${OS}/companies`)).find((c) => c.name === 'شرکت فناوران پارس');
  if (!company) company = await call(admin, 'POST', `${OS}/companies`, { name: 'شرکت فناوران پارس', code: 'FP', description: 'شرکت نرم‌افزاری فعال در حوزهٔ مدیریت پروژه و فرآیند' });
  const positions = await call(admin, 'GET', `${OS}/positions`);
  // Titles of this company only. A title of that name used by other companies is left alone (each company has a
  // single top title, and names are unique in the organization): this company gets «<title> (فناوران پارس)».
  const forCompany = (x) => (x.companyIds || []).includes(company.id);
  const ensurePosition = async (title, parentId) => {
    const mine = positions.find((x) => forCompany(x) && (x.title === title || x.title === `${title} (فناوران پارس)`));
    if (mine) return mine;
    const name = positions.some((x) => x.title === title) ? `${title} (فناوران پارس)` : title;
    const made = await call(admin, 'POST', `${OS}/positions`, { title: name, parentPositionId: parentId, companyIds: [company.id] });
    positions.push(made);
    return made;
  };
  const posManager = await ensurePosition('مدیر واحد', null);
  const posSenior = await ensurePosition('کارشناس ارشد', posManager.id);
  const posStaff = await ensurePosition('کارشناس', posManager.id);
  const nodes = await call(admin, 'GET', `${OS}/nodes?companyId=${company.id}`);
  for (const [key, unitName] of Object.entries(UNITS)) {
    const node = nodes.find((n) => n.name === unitName) || await call(admin, 'POST', `${OS}/nodes`, { name: unitName, parentNodeId: null, companyId: company.id });
    const posts = await call(admin, 'GET', `${OS}/unit-positions?unitId=${node.id}`);
    const holderOf = (post) => (post.holders || post.users || []).map((h) => h.userId || h.id);
    const people = byTeam(key);
    const manager = people.find((u) => u.isManager);
    let managerPost = posts.find((x) => holderOf(x).includes(manager.id));
    if (!managerPost) {
      managerPost = await call(admin, 'POST', `${OS}/units/${node.id}/positions`, { positionId: posManager.id, code: null, parentPostId: null });
      await call(admin, 'PUT', `${OS}/assignments/${manager.id}`, { companyId: company.id, unitId: node.id, positionId: posManager.id, managerUserId: null, unitPositionIds: [managerPost.id] });
    }
    for (const u of people.filter((x) => !x.isManager)) {
      if (posts.some((x) => holderOf(x).includes(u.id))) continue;
      const post = await call(admin, 'POST', `${OS}/units/${node.id}/positions`, { positionId: (u.title.includes('ارشد') ? posSenior : posStaff).id, code: null, parentPostId: managerPost.id });
      await call(admin, 'PUT', `${OS}/assignments/${u.id}`, { companyId: company.id, unitId: node.id, positionId: (u.title.includes('ارشد') ? posSenior : posStaff).id, managerUserId: manager.id, unitPositionIds: [post.id] });
    }
  }
  log('org chart ready');

  // Teams: each manager's own team with its people
  const teamIds = {};
  for (const [key, name] of Object.entries(TEAMS)) {
    const m = managerOf(key); const tk = await tokenOf(m);
    const mine = await call(tk, 'GET', '/api/identity/groups/mine');
    let g = mine.find((x) => x.name === name);
    if (!g) g = await call(tk, 'POST', '/api/identity/groups/mine', { name });
    await call(tk, 'PUT', `/api/identity/groups/mine/${g.id}/members`, { userIds: byTeam(key).map((u) => u.id) });
    teamIds[key] = g.id;
  }
  log('teams ready');

  // Tasks (a title that already exists is not made again; its id is reused for comments and time)
  const existingTasks = [];
  for (let page = 1; ; page++) {
    const r = await call(admin, 'GET', `/api/task-management/tasks?pageNumber=${page}&pageSize=200`);
    existingTasks.push(...r.items);
    if (existingTasks.length >= r.totalCount || !r.items.length) break;
  }
  const existingByTitle = new Map(existingTasks.map((t) => [t.title, t]));
  const created = [];
  const statuses = ['Todo', 'Todo', 'Todo', 'InProgress', 'InProgress', 'Paused', 'Completed', 'Completed'];
  for (const [key, w] of Object.entries(WORK)) {
    const people = byTeam(key);
    for (const [title, description] of w.titles) {
      const owner = chance(0.6) ? managerOf(key) : pick(people);
      const responsible = chance(0.25) ? pickN(people, 2) : [pick(people)];
      const tk = await tokenOf(owner);
      const prior = existingByTitle.get(title);
      if (prior) {
        const pRes = (prior.responsibleUsers || (prior.assignedUser ? [prior.assignedUser] : [])).map((r) => users.find((u) => u.id === r.id)).filter(Boolean);
        created.push({ id: prior.id, team: key, owner: users.find((u) => u.id === prior.owner?.id) || owner, responsible: pRes.length ? pRes : responsible, status: prior.status, title });
        continue;
      }
      const t = await call(tk, 'POST', '/api/task-management/tasks', {
        title, dueDate: dueSoon(), dueTime: chance(0.4) ? pick(['10:00:00', '12:00:00', '14:30:00', '16:00:00']) : null,
        priority: pick(['High', 'Medium', 'Medium', 'Low']), description: description || null, isProject: false, parentTaskId: null,
        assignedUserId: responsible[0].id, responsibleUserIds: responsible.map((r) => r.id), responsiblePostIds: null,
        assignedUserGroupId: teamIds[key], assigneeUserIds: people.map((p) => p.id), allowAssigneeStatusUpdate: true,
        subTasks: [], tags: pickN(w.tags, 1 + Math.floor(rand() * 2)),
      });
      const status = pick(statuses);
      if (status !== 'Todo') await call(tk, 'PATCH', `/api/task-management/tasks/${t.id}/status`, { status });
      created.push({ id: t.id, team: key, owner, responsible, status, title, isNew: true });
    }
  }
  for (const [key, title, steps] of PROJECTS) {
    const m = managerOf(key); const tk = await tokenOf(m); const people = byTeam(key);
    if (existingByTitle.has(title)) { created.push({ id: existingByTitle.get(title).id, team: key, owner: m, responsible: [m], status: 'InProgress', title }); continue; }
    const start = workDays[1]; const end = workDays[Math.min(workDays.length - 1, 25 + Math.floor(rand() * 10))];
    const t = await call(tk, 'POST', '/api/task-management/tasks', {
      title, dueDate: end, dueTime: null, priority: 'High', description: `پروژهٔ اصلی ${TEAMS[key]} در این فصل.`, isProject: true, parentTaskId: null,
      assignedUserId: m.id, responsibleUserIds: [m.id], responsiblePostIds: null, assignedUserGroupId: teamIds[key], assigneeUserIds: people.map((p) => p.id),
      allowAssigneeStatusUpdate: true, charterDescription: `هدف: ${title.replace('پروژهٔ ', '')} تا پایان فصل.`, charterProjectManager: m.name,
      charterStartDate: start, charterEndDate: end, charterStartTime: null, charterEndTime: null,
      subTasks: steps.map((s, i) => ({ title: s, importance: i === 0 ? 'High' : 'Medium', startDate: workDays[1 + i * 4] || end, endDate: workDays[4 + i * 4] || end, startTime: null, endTime: null, sortOrder: i, isGeneratedOccurrence: false, assigneeUserIds: [people[(i + 1) % people.length].id] })),
      tags: ['پروژه'],
    });
    const done = Math.floor(rand() * (steps.length - 1));
    const subs = [...t.subTasks].sort((a, b) => a.sortOrder - b.sortOrder);
    for (let i = 0; i < done; i++) await call(tk, 'PATCH', `/api/task-management/subtasks/${subs[i].id}/status`, { status: 'Completed' }).catch(() => {});
    created.push({ id: t.id, team: key, owner: m, responsible: [m], status: 'InProgress', title, isNew: true });
  }
  log(`tasks: ${created.length}`);

  // Comments (owner or a responsible person)
  let commentCount = 0;
  for (const t of created) {
    if (!t.isNew || !chance(0.55)) continue;
    const n = 1 + Math.floor(rand() * 3);
    for (let i = 0; i < n; i++) {
      const who = i % 2 === 0 ? t.responsible[0] : t.owner;
      await call(await tokenOf(who), 'POST', `/api/task-management/tasks/${t.id}/comments`, { text: pick(COMMENTS) }).then(() => commentCount++).catch(() => {});
    }
  }
  log(`comments: ${commentCount}`);

  // Time logged this week and last week, on each person's own tasks
  let entries = 0;
  for (const u of users) {
    const mine = created.filter((t) => t.isNew && t.responsible.some((r) => r.id === u.id) && t.status !== 'Todo');
    if (!mine.length) continue;
    const tk = await tokenOf(u);
    for (let back = 1; back <= 10; back++) {
      const d = addDays(today, -back);
      if (d.getUTCDay() === 5 || !chance(0.75)) continue;
      const parts = 1 + Math.floor(rand() * 2);
      for (let k = 0; k < parts; k++) {
        await call(tk, 'POST', '/api/task-management/timesheets/entries', { taskId: pick(mine).id, date: iso(d), minutes: pick([45, 60, 90, 120, 150, 180, 240]), note: pick([null, 'جلسهٔ هماهنگی', 'پیاده‌سازی', 'بررسی و تست', 'مستندسازی', 'تماس با مشتری']) })
          .then(() => entries++).catch(() => {});
      }
    }
  }
  log(`time entries: ${entries}`);

  if (process.env.ONLY_TASKS) { await overdueStep(); return; }
  // Meetings in the next two weeks
  const MEETINGS = [
    ['جلسهٔ هفتگی تیم فنی', 'tech', '09:30', '10:30', 'اتاق جلسات ۲'], ['بازبینی طراحی داشبورد', 'product', '11:00', '12:00', 'اتاق جلسات ۱'],
    ['جلسهٔ بودجهٔ سال آینده', 'finance', '10:00', '11:30', 'دفتر مدیرعامل'], ['هماهنگی کمپین نسخهٔ جدید', 'marketing', '14:00', '15:00', 'آنلاین'],
    ['مرور تیکت‌های هفته', 'support', '13:00', '13:45', 'اتاق پشتیبانی'], ['برنامه‌ریزی اسپرینت بعدی', 'tech', '10:00', '11:30', 'اتاق جلسات ۲'],
    ['جلسهٔ دمو با مشتری', 'marketing', '15:00', '16:00', 'دفتر مشتری'], ['کمیتهٔ استخدام', 'finance', '12:00', '13:00', 'اتاق جلسات ۱'],
  ];
  let meetings = 0;
  for (let i = 0; i < MEETINGS.length; i++) {
    const [title, key, s, e, loc] = MEETINGS[i]; const m = managerOf(key);
    await call(await tokenOf(m), 'POST', '/api/calendar/meetings', {
      title, type: loc === 'آنلاین' ? 'online' : 'meeting', date: workDays[1 + i] || workDays[1], startTime: s, endTime: e, location: loc, organizerUserId: m.id,
      description: '', participantIds: byTeam(key).map((u) => u.id), agenda: [{ title: 'مرور کارهای هفتهٔ گذشته', durationMinutes: 20, presenterUserId: m.id }, { title: 'برنامهٔ هفتهٔ آینده', durationMinutes: 25, presenterUserId: null }],
      minutes: '', actions: [], taskIds: [], sendInvitationSms: false,
    }).then(() => meetings++).catch((err) => log('meeting skipped:', String(err).slice(0, 160)));
  }
  log(`meetings: ${meetings}`);

  // Personal notes
  const NOTES = [
    ['کارهای این هفته', '- تماس با مشتری آریا\n- بازبینی گزارش فروش\n- آماده‌سازی جلسهٔ شنبه', 'amber', true],
    ['ایده برای بهبود فرآیند', 'جلسه‌های روزانه کوتاه‌تر شود (حداکثر ۱۰ دقیقه) و هر کس فقط مانع‌ها را بگوید.', 'indigo', false],
    ['شماره‌های مهم', 'پشتیبانی سرور: ۰۲۱-۸۸۷۷۶۶۵۵\nحسابداری ساختمان: ۰۲۱-۸۸۲۲۳۳۴۴', 'slate', false],
    ['یادآوری', 'تمدید بیمهٔ تکمیلی تا پانزدهم ماه', 'rose', true],
    ['کتاب‌هایی که باید بخوانم', 'مدیریت زمان برای مدیران\nطراحی محصولات دوست‌داشتنی', 'emerald', false],
  ];
  let notes = 0;
  for (const u of users.filter((x) => x.isManager || chance(0.4))) {
    const tk = await tokenOf(u);
    for (const [title, content, color, isPinned] of pickN(NOTES, 2 + Math.floor(rand() * 2))) {
      await call(tk, 'POST', '/api/task-management/notes', { title, content, color, isPinned }).then(() => notes++).catch(() => {});
    }
  }
  log(`notes: ${notes}`);

  // Chat: team threads and direct conversations
  const sendForm = (text) => { const f = new FormData(); f.append('text', text); return f; };
  const TEAM_MSGS = ['سلام همگی، صبح بخیر', 'گزارش هفتگی را تا پنجشنبه بفرستید لطفاً', 'جلسهٔ فردا ساعت ۱۰ برگزار می‌شود', 'نسخهٔ جدید روی سرور تست است، لطفاً بررسی کنید',
    'کسی فایل قرارداد آریا را دارد؟', 'من در پوشهٔ مشترک گذاشتم', 'ممنون 🙏', 'امروز بعدازظهر مرخصی هستم، کارهای فوری را به علی بسپارید', 'تبریک بابت تحویل به‌موقع پروژه 👏'];
  let msgs = 0;
  for (const key of Object.keys(TEAMS)) {
    for (const text of pickN(TEAM_MSGS, 5)) {
      const u = pick(byTeam(key));
      await call(await tokenOf(u), 'POST', `/api/chat/teams/${teamIds[key]}/messages`, undefined, sendForm(text)).then(() => msgs++).catch(() => {});
    }
  }
  const DM = [['سلام، وقت داری امروز دربارهٔ گزارش صحبت کنیم؟', 'سلام، بله ساعت ۱۴ خوب است', 'عالی، می‌بینمت'],
    ['فایل طراحی را دیدی؟', 'بله، دو نکته دارم که در گفتگوی فعالیت نوشتم', 'ممنون، اصلاح می‌کنم'],
    ['مشتری دربارهٔ موعد تحویل پرسید', 'تا آخر هفته تحویل می‌دهیم', 'به او اطلاع می‌دهم']];
  for (let i = 0; i < 8; i++) {
    const [a, b] = pickN(users, 2); const conv = pick(DM);
    for (let k = 0; k < conv.length; k++) {
      const from = k % 2 === 0 ? a : b; const to = k % 2 === 0 ? b : a;
      await call(await tokenOf(from), 'POST', `/api/chat/direct/${to.id}/messages`, undefined, sendForm(conv[k])).then(() => msgs++).catch(() => {});
    }
  }
  log(`chat messages: ${msgs}`);

  // Requests: categories, submissions, some handled
  const cats = await call(admin, 'GET', '/api/task-management/requests/categories').catch(() => []);
  const ensureCat = async (name, routeTo) => cats.find((c) => c.name === name) || call(admin, 'POST', '/api/task-management/requests/categories', { name, routeToUserId: routeTo.id, defaultPriority: 'Medium', isActive: true });
  const catIT = await ensureCat('پشتیبانی فناوری اطلاعات', managerOf('tech'));
  const catBuy = await ensureCat('خرید و تدارکات', managerOf('finance'));
  const catHR = await ensureCat('منابع انسانی', managerOf('finance'));
  const REQS = [['دسترسی به پوشهٔ مشترک واحد مالی', catIT], ['تعویض ماوس و صفحه‌کلید', catBuy], ['نصب نرم‌افزار طراحی روی لپ‌تاپ', catIT],
    ['درخواست گواهی اشتغال به کار', catHR], ['خرید هدست برای تماس‌های آنلاین', catBuy], ['مشکل اتصال VPN از منزل', catIT],
    ['تمدید قرارداد همکاری', catHR], ['ارتقای حافظهٔ رم سیستم', catBuy], ['ساخت حساب ایمیل برای همکار جدید', catIT], ['درخواست مرخصی ساعتی هفتگی', catHR]];
  let reqs = 0;
  for (const [title, cat] of REQS) {
    const u = pick(users.filter((x) => !x.isManager));
    const r = await call(await tokenOf(u), 'POST', '/api/task-management/requests', { title, description: 'لطفاً در اولین فرصت رسیدگی شود.', categoryId: cat.id, priority: pick(['Medium', 'High', 'Low']), neededBy: null })
      .then((x) => { reqs++; return x; }).catch(() => null);
    if (r && chance(0.4)) {
      const handler = cat === catIT ? managerOf('tech') : managerOf('finance');
      await call(await tokenOf(handler), 'POST', `/api/task-management/requests/${r.id}/convert`, { dueDate: dueSoon(), assignedUserId: pick(byTeam(handler.team)).id }).catch(() => {});
    }
  }
  log(`requests: ${reqs}`);

  await overdueStep();
  log('done');
  async function overdueStep() {
  // Ids of open tasks, for the overdue step done in SQL (the API refuses past due dates by design)
  const open = created.filter((t) => t.isNew && t.status !== 'Completed' && !PROJECTS.some((p) => p[1] === t.title));
  const overdue = pickN(open, Math.min(14, open.length)).map((t) => t.id);
  const fs = await import('node:fs');
  fs.writeFileSync(new URL('./overdue-ids.txt', import.meta.url), overdue.join('\n'));
  log(`overdue candidates: ${overdue.length}`);
  }
}

main().catch((err) => { console.error(err); process.exit(1); });
