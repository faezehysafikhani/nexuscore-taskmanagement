// Bulk sample data for a TEST server of THIS solution (TMTejarat-API host), created through the system's own API
// (same rules and permissions as the UI). Company «شرکت فناوران پارس» with 5 units and posts, many people
// (password Test@Pass#2026), 2 roles, 5 teams, hundreds to thousands of tasks, projects, comments, meetings,
// notes and chat.
//
// Never run it on production. Node 18+:
//   API=http://localhost:5112 ADMIN_PASSWORD='...' node db/seed-demo-data.mjs
//
// Size (environment variables, all optional):
//   EXTRA_STAFF_PER_TEAM  generated people per team on top of the 18 hand-written ones   (default 20  -> 118 people)
//   TASKS_PER_PERSON      generated tasks per non-manager                                 (default 10)
//   COMMENT_RATE          chance that a new task gets 1-3 comments                        (default 0.4)
//   MEETING_ROUNDS        how many times the 8 meetings are repeated on later days        (default 3)
//   CONCURRENCY           parallel requests for tasks and comments                        (default 4)
//   USER_PASSWORD         password of every person made                                   (default Test@Pass#2026)
//   PLAN=1                print what would be made and exit (no network)
//   ONLY_TASKS=1          stop after tasks, comments and the overdue ids (no meetings, notes or chat)
//
// Re-running skips what exists (people by name, teams, tasks and projects by title); comments are added only for new
// tasks. Titles of the generated tasks are fixed by the numbers above: change EXTRA_STAFF_PER_TEAM or TASKS_PER_PERSON
// and the generated titles change with them (the ones already in the database stay).
//
// Limits you will meet with many people:
//  - Sign-in is limited to 30 per 5 minutes per client: each person signs in once, so start the API with
//    Identity__LoginProtection__MaxLoginAttemptsPerClient=2000 on a test server, or the script waits and retries.
//  - AuthenticatedApi (300 requests per minute per user) and ChatSend (60 per minute per user): a 429 is waited out
//    and retried, so a big run is slower, not wrong.
//
// What this solution has and the script uses: identity (users, roles, my teams), org-structure (company, positions,
// units, posts, assignments), task-management (tasks, subtasks, comments, notes), calendar (official holidays,
// meetings) and chat (team and direct messages). Left out because this solution has no such endpoints: scheduling-days
// (the script reads the official holidays and skips Fridays itself), time entries (timesheets) and requests.
//
// Overdue tasks: the API refuses past due dates by design. The script writes overdue-ids.txt (open tasks it made);
// on a test server move their due date back, e.g. per id:
//   UPDATE task_management.Tasks SET DueDate = DATEADD(day, -3, CAST(GETDATE() AS date)) WHERE Id = '<id>' AND Status IN (0, 1);
// It also writes demo-users.csv (username, name, team, title) next to itself.
const API = (process.env.API || 'http://localhost:5112').replace(/\/+$/, '');
const TENANT = '11111111-1111-1111-1111-111111111111';
const PASSWORD = process.env.USER_PASSWORD || 'Test@Pass#2026';
const intEnv = (name, fallback) => { const v = Number.parseInt(process.env[name] ?? '', 10); return Number.isFinite(v) && v >= 0 ? v : fallback; };
const numEnv = (name, fallback) => { const v = Number.parseFloat(process.env[name] ?? ''); return Number.isFinite(v) && v >= 0 ? v : fallback; };
const EXTRA_STAFF_PER_TEAM = intEnv('EXTRA_STAFF_PER_TEAM', 20);
const TASKS_PER_PERSON = intEnv('TASKS_PER_PERSON', 10);
const COMMENT_RATE = Math.min(1, numEnv('COMMENT_RATE', 0.4));
const MEETING_ROUNDS = Math.max(1, intEnv('MEETING_ROUNDS', 3));
const CONCURRENCY = Math.max(1, intEnv('CONCURRENCY', 4));

const log = (...a) => console.log(...a);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
// A step that may be refused for one item (a rule of the server) goes on with the next; each kind of refusal is shown once.
const seenFailures = new Set();
const fail = (what) => (err) => { const key = what + String(err).slice(0, 120); if (!seenFailures.has(key)) { seenFailures.add(key); console.log(`  ${what} skipped: ${String(err.message || err).slice(0, 220)}`); } };

// Runtime choices (which day, which chat line): a seeded generator, so a run is repeatable.
let rnd = 20261124;
const rand = () => ((rnd = (rnd * 1103515245 + 12345) % 2147483648) / 2147483648);
const pick = (arr) => arr[Math.floor(rand() * arr.length)];
const pickN = (arr, n) => [...arr].sort(() => rand() - 0.5).slice(0, n);
const chance = (p) => rand() < p;
// What decides a title or an owner of a generated task: a pure function of the number, never of the run, so a re-run
// finds the same titles and skips them.
const hash = (n) => { let x = (n ^ 0x9e3779b9) >>> 0; x = Math.imul(x ^ (x >>> 16), 0x85ebca6b); x = Math.imul(x ^ (x >>> 13), 0xc2b2ae35); return ((x ^ (x >>> 16)) >>> 0) / 4294967296; };
const hpick = (arr, n) => arr[Math.floor(hash(n) * arr.length)];
// Identity numbers (national code, mobile) come from their own generator so they do not move the runtime one.
let idState = 77123;
const idRand = () => ((idState = (idState * 1103515245 + 12345) % 2147483648) / 2147483648);

async function call(token, method, path, body, form) {
  for (let attempt = 0; ; attempt++) {
    const headers = { Authorization: `Bearer ${token}` };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const res = await fetch(API + path, { method, headers, body: form ?? (body !== undefined ? JSON.stringify(body) : undefined) });
    const text = await res.text();
    if (res.status === 429 && attempt < 15) {
      const wait = Math.min(65, Number(res.headers.get('retry-after')) || 15);
      await sleep(wait * 1000);
      continue;
    }
    if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${text.slice(0, 300)}`);
    return text ? JSON.parse(text) : null;
  }
}
async function login(identifier, password) {
  for (let attempt = 0; attempt < 20; attempt++) {
    try { return await loginOnce(identifier, password); }
    catch (err) { if (!/too_many_requests|429/.test(String(err))) throw err; console.log('login throttled, waiting 30s'); await sleep(30000); }
  }
  throw new Error('login kept being throttled');
}
async function loginOnce(identifier, password) {
  const post = async (path, body) => {
    const res = await fetch(API + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const text = await res.text();
    if (res.status === 429) throw new Error('too_many_requests');
    try { return JSON.parse(text); } catch { throw new Error(`login ${identifier}: ${res.status} ${text.slice(0, 200)}`); }
  };
  const r = await post('/api/identity/auth/login', { identifier, password });
  if (r.accessToken) return r.accessToken;
  const ticket = r.sessionLimit?.ticket;
  if (!ticket) throw new Error(`login ${identifier}: ${JSON.stringify(r).slice(0, 200)}`);
  const c = await post('/api/identity/auth/login/complete', { ticket, closeSessionIds: [], closeOldest: true });
  if (!c.accessToken) throw new Error(`login ${identifier}: ${JSON.stringify(c).slice(0, 200)}`);
  return c.accessToken;
}
async function runPool(items, size, fn) {
  let next = 0;
  const workers = Array.from({ length: Math.min(size, items.length) }, async () => {
    while (next < items.length) { const item = items[next++]; await fn(item); }
  });
  await Promise.all(workers);
}
function nationalCode() {
  for (;;) {
    const d = Array.from({ length: 9 }, () => Math.floor(idRand() * 10));
    if (new Set(d).size === 1) continue;
    const s = d.reduce((acc, v, i) => acc + v * (10 - i), 0) % 11;
    return d.join('') + (s < 2 ? s : 11 - s);
  }
}
const iso = (d) => d.toISOString().slice(0, 10);
const addDays = (d, n) => { const x = new Date(d); x.setUTCDate(x.getUTCDate() + n); return x; };
const todayIran = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Tehran' }).format(new Date());
const today = new Date(todayIran + 'T00:00:00Z');

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
const UNITS = { tech: 'واحد فنی', product: 'واحد محصول', support: 'واحد پشتیبانی', finance: 'واحد مالی و اداری', marketing: 'واحد بازاریابی و فروش' };
// Names for the generated people (a first name and a last name are combined; a pair is never used twice).
const FIRST = ['آرش', 'آرمین', 'آیدا', 'ابوالفضل', 'احسان', 'ارسلان', 'اسماعیل', 'افسانه', 'امید', 'ایمان', 'بابک', 'بنفشه', 'پارسا', 'پریسا', 'تینا', 'جواد', 'حامد', 'حمید', 'خدیجه', 'داریوش',
  'دانیال', 'رحیم', 'روزبه', 'ساناز', 'سجاد', 'سعید', 'سمیرا', 'سهیل', 'شادی', 'شهاب', 'صبا', 'طاها', 'عرفان', 'غزل', 'کاوه', 'کیان', 'ماهان', 'مریم', 'ندا', 'هدی', 'یاسمن', 'یاسر'];
const LAST = ['احمدی', 'اصفهانی', 'امینی', 'انصاری', 'بهرامی', 'پناهی', 'تقوی', 'جلالی', 'حیدری', 'خسروی', 'دانشور', 'ذاکری', 'رستمی', 'زارعی', 'سعیدی', 'سلطانی', 'شفیعی', 'صالحی', 'طباطبایی', 'عباسی',
  'فرهادی', 'قنبری', 'کریمخانی', 'گلشن', 'لطفی', 'مختاری', 'مهدوی', 'میرزایی', 'نادری', 'نجفی', 'هاشمی', 'همتی', 'وکیلی', 'یوسفی', 'آذری', 'بیگی', 'پورمحمدی', 'توکلی', 'سبحانی', 'فتحی'];
const JOB_TITLES = {
  tech: ['توسعه‌دهندهٔ بک‌اند', 'توسعه‌دهندهٔ فرانت‌اند', 'کارشناس تست نرم‌افزار', 'مهندس زیرساخت', 'کارشناس امنیت اطلاعات', 'توسعه‌دهندهٔ ارشد', 'مهندس DevOps'],
  product: ['طراح تجربهٔ کاربری', 'تحلیل‌گر کسب‌وکار', 'کارشناس محصول', 'طراح رابط کاربری', 'پژوهشگر کاربر'],
  support: ['کارشناس پشتیبانی', 'کارشناس آموزش مشتریان', 'کارشناس پایگاه دانش', 'کارشناس پشتیبانی ارشد'],
  finance: ['کارشناس حسابداری', 'کارشناس منابع انسانی', 'کارشناس خرید', 'کارشناس بودجه', 'کارشناس اداری'],
  marketing: ['کارشناس فروش', 'کارشناس محتوا', 'کارشناس ارتباط با مشتری', 'کارشناس بازاریابی دیجیتال', 'کارشناس برگزاری نمایشگاه'],
};
function buildPeople() {
  const all = PEOPLE.map(([first, last, team, isManager, title], i) => ({ first, last, team, isManager, title, code: i }));
  const used = new Set(all.map((p) => `${p.first} ${p.last}`));
  let g = 0;
  for (const team of Object.keys(TEAMS)) {
    for (let j = 0; j < EXTRA_STAFF_PER_TEAM; j++) {
      g++;
      let c = 0; let first; let last;
      do {
        if (c > 5000) throw new Error('not enough names for EXTRA_STAFF_PER_TEAM; add names to FIRST/LAST');
        first = FIRST[(g * 7 + c) % FIRST.length];
        last = LAST[(g * 13 + c * 5 + Math.floor(g / FIRST.length)) % LAST.length];
        c++;
      } while (used.has(`${first} ${last}`));
      used.add(`${first} ${last}`);
      all.push({ first, last, team, isManager: false, title: JOB_TITLES[team][g % JOB_TITLES[team].length], code: 100 + g });
    }
  }
  return all;
}

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
      ['بازطراحی فرم ثبت فعالیت', 'تعداد فیلدها کم شود و دسته‌بندی هوشمند پیشنهاد داده شود.'],
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
      ['تهیهٔ ویدیوی آموزشی ثبت فعالیت', 'ویدیوی ۳ دقیقه‌ای برای کاربران جدید.'],
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
// Building blocks of the generated tasks: «<verb> <object> برای <client> [code]».
const GEN = {
  tech: { verbs: ['بررسی', 'رفع', 'پیاده‌سازی', 'تست', 'مستندسازی', 'بهینه‌سازی', 'بازبینی'],
    objects: ['خطای گزارش‌گیری', 'کندی صفحهٔ بورد', 'سرویس اعلان‌ها', 'دسترسی‌های کاربران', 'همگام‌سازی LDAP', 'پشتیبان‌گیری', 'API فعالیت‌ها', 'ورود با پیامک', 'خروجی Excel'] },
  product: { verbs: ['تحلیل', 'طراحی', 'بازنگری', 'اولویت‌بندی', 'مصاحبه دربارهٔ'], objects: ['درخواست‌های امکانات', 'جریان ثبت فعالیت', 'داشبورد مدیران', 'گزارش‌های دوره‌ای', 'تجربهٔ موبایل', 'سند نیازمندی'] },
  support: { verbs: ['پیگیری', 'پاسخ به', 'آموزش', 'بررسی'], objects: ['تیکت‌های باز', 'مشکل ورود کاربران', 'درخواست نصب نسخهٔ جدید', 'راهنمای کاربری', 'نظرسنجی رضایت'] },
  finance: { verbs: ['صدور', 'پیگیری', 'تنظیم', 'بررسی', 'تسویهٔ'], objects: ['فاکتور قرارداد', 'پرداخت معوق', 'برآورد بودجه', 'لیست بیمه', 'صورت‌حساب'] },
  marketing: { verbs: ['پیگیری', 'تهیهٔ', 'ارسال', 'تحلیل', 'هماهنگی'], objects: ['پیش‌فاکتور', 'پیشنهاد تمدید', 'محتوای کمپین', 'گزارش فروش', 'جلسهٔ دمو'] },
};
const GEN_DESCRIPTIONS = [null, null, 'جزئیات در جلسهٔ هفتگی توضیح داده شد؛ در صورت نیاز با مدیر واحد هماهنگ شود.', 'اولویت با درخواست‌هایی است که مشتری پیگیر آن است.',
  'نتیجه را در پوشهٔ مشترک بگذارید و در گفتگوی فعالیت اطلاع دهید.', 'پیش از شروع، وابستگی‌ها و زمان لازم برآورد شود.'];
const STATUSES = ['Todo', 'Todo', 'Todo', 'InProgress', 'InProgress', 'Paused', 'Completed', 'Completed'];

const PROJECTS = [
  ['tech', 'پروژهٔ راه‌اندازی نسخهٔ ۳ سامانه', ['تحلیل نیازمندی‌ها', 'طراحی معماری', 'پیاده‌سازی ماژول‌های اصلی', 'تست یکپارچه', 'استقرار روی سرور اصلی']],
  ['product', 'پروژهٔ بازطراحی تجربهٔ کاربری', ['مصاحبه با کاربران', 'طراحی وایرفریم', 'طراحی نهایی', 'آزمون کاربردپذیری']],
  ['marketing', 'پروژهٔ حضور در نمایشگاه الکامپ', ['رزرو غرفه', 'طراحی غرفه', 'تهیهٔ کاتالوگ و هدایا', 'هماهنگی تیم حاضر', 'پیگیری سرنخ‌ها']],
  ['finance', 'پروژهٔ استقرار نرم‌افزار حقوق و دستمزد', ['انتخاب نرم‌افزار', 'انتقال اطلاعات پرسنل', 'اجرای آزمایشی', 'اجرای نهایی']],
  ['support', 'پروژهٔ راه‌اندازی مرکز تماس', ['خرید سامانهٔ تلفنی', 'آموزش کارشناسان', 'تدوین اسکریپت پاسخگویی']],
  ['tech', 'پروژهٔ مهاجرت زیرساخت به ابر خصوصی', ['برآورد منابع', 'راه‌اندازی خوشهٔ آزمایشی', 'انتقال سرویس‌های غیرحیاتی', 'انتقال پایگاه داده', 'قطع سرور قدیمی']],
  ['product', 'پروژهٔ سامانهٔ نظرسنجی مشتریان', ['تعریف پرسشنامه', 'طراحی صفحهٔ نظرسنجی', 'اتصال به تیکت‌ها', 'گزارش نتایج']],
  ['marketing', 'پروژهٔ برند و وب‌سایت جدید', ['تحقیق بازار', 'هویت بصری', 'طراحی وب‌سایت', 'تولید محتوا', 'راه‌اندازی و سئو']],
  ['finance', 'پروژهٔ ممیزی داخلی سالانه', ['برنامهٔ ممیزی', 'جمع‌آوری مستندات', 'بررسی ریسک‌ها', 'تهیهٔ گزارش نهایی']],
  ['support', 'پروژهٔ بازطراحی پایگاه دانش', ['بررسی مقاله‌های فعلی', 'ساختار جدید دسته‌ها', 'بازنویسی ۴۰ مقاله', 'راه‌اندازی جستجو']],
];
const COMMENTS = [
  'بررسی کردم، تا فردا نتیجه را اعلام می‌کنم.', 'فایل نهایی را پیوست کنید لطفاً.', 'این مورد به هماهنگی با واحد مالی نیاز دارد.',
  'انجام شد؛ لطفاً بازبینی کنید.', 'آیا موعد قابل تمدید است؟ یک روز دیگر لازم دارم.', 'با مشتری تماس گرفتم، منتظر پاسخشان هستیم.',
  'نسخهٔ اول آماده است، بازخوردتان را بنویسید.', 'این را در جلسهٔ شنبه مطرح کنیم.', 'مشکل از تنظیمات سرور بود، اصلاح شد.',
  'لطفاً اولویت این کار را بالا ببرید، مشتری منتظر است.', 'پیش‌نویس را در پوشهٔ مشترک گذاشتم.', 'موافقم، با همین روش جلو برویم.',
];

// ------------------------------------------------------------------ the plan (no network)
// A task spec is fixed by its number alone (see hash above), so the same numbers give the same titles on every run.
function buildTaskSpecs(people) {
  const managerOf = (team) => people.find((p) => p.team === team && p.isManager);
  const staffOf = (team) => people.filter((p) => p.team === team && !p.isManager);
  const specs = [];
  const seenTitles = new Set();
  let k = 0;
  const spec = (team, title, description, owner, responsible) => {
    const w = WORK[team];
    const a = Math.floor(hash(k * 29 + 3) * w.tags.length);
    return {
      team, title, description, owner, responsible,
      tags: hash(k * 31 + 7) < 0.5 ? [w.tags[a]] : [w.tags[a], w.tags[(a + 1) % w.tags.length]],
      priority: hpick(['High', 'Medium', 'Medium', 'Low'], k * 17 + 1),
      status: hpick(STATUSES, k * 19 + 2),
      dueH: hash(k * 11 + 5),
      dueTime: hash(k * 23 + 9) < 0.4 ? hpick(['10:00:00', '12:00:00', '14:30:00', '16:00:00'], k * 13 + 4) : null,
    };
  };
  // 1. the hand-written titles. Only to one's own level and below (OrgStructure:LimitTaskAssignment): a manager gives
  //    work to the unit's people, a staff member records their own.
  for (const [team, w] of Object.entries(WORK)) {
    const staff = staffOf(team);
    for (const [title, description] of w.titles) {
      if (seenTitles.has(title)) continue; // the same title twice (a client name in two lists) is made once
      seenTitles.add(title);
      k++;
      const byManager = hash(k * 3 + 1) < 0.65;
      const person = staff[Math.floor(hash(k * 5 + 2) * staff.length)];
      const second = staff[Math.floor(hash(k * 7 + 3) * staff.length)];
      const responsible = byManager && hash(k * 9 + 4) < 0.25 && second !== person ? [person, second] : [person];
      specs.push(spec(team, title, description, byManager ? managerOf(team) : person, responsible));
    }
  }
  // 2. generated: TASKS_PER_PERSON for every non-manager, the person being the responsible one
  for (const p of people.filter((x) => !x.isManager)) {
    const g = GEN[p.team];
    for (let n = 0; n < TASKS_PER_PERSON; n++) {
      k++;
      const title = `${hpick(g.verbs, k * 3 + 11)} ${hpick(g.objects, k * 5 + 12)} برای ${hpick(CLIENTS, k * 7 + 13)} [${p.code}-${n + 1}]`;
      const byManager = hash(k * 3 + 1) < 0.65;
      specs.push(spec(p.team, title, hpick(GEN_DESCRIPTIONS, k * 9 + 14), byManager ? managerOf(p.team) : p, [p]));
    }
  }
  return specs;
}

const PEOPLE_ALL = buildPeople();
const TASK_SPECS = buildTaskSpecs(PEOPLE_ALL);

if (process.env.PLAN) {
  const perTeam = (list, key) => Object.keys(TEAMS).map((t) => `${t}=${list.filter((x) => x[key] === t).length}`).join(' ');
  log(`people: ${PEOPLE_ALL.length} (${perTeam(PEOPLE_ALL, 'team')})`);
  log(`tasks: ${TASK_SPECS.length} (${perTeam(TASK_SPECS, 'team')}) + ${PROJECTS.length} projects`);
  log(`titles unique: ${new Set(TASK_SPECS.map((s) => s.title)).size === TASK_SPECS.length}`);
  log(`names unique: ${new Set(PEOPLE_ALL.map((p) => `${p.first} ${p.last}`)).size === PEOPLE_ALL.length}`);
  process.exit(0);
}

async function main() {
  const admin = await login(process.env.ADMIN_USER || 'admin', process.env.ADMIN_PASSWORD || 'Admin@12345');
  log('admin signed in');

  // Working days ahead: no Friday, no official holiday. The server decides in the end (and also refuses days before
  // today in Iran): a day it refuses is replaced by the next one.
  const holidays = await call(admin, 'GET', `/api/calendar/work-calendars/official-holidays?from=${iso(today)}&to=${iso(addDays(today, 60))}`)
    .catch((err) => { fail('official holidays')(err); return []; });
  const off = new Set((Array.isArray(holidays) ? holidays : []).map((h) => String(h.date).slice(0, 10)));
  const isWorking = (d) => d.getUTCDay() !== 5 && !off.has(iso(d));
  const workDays = [];
  for (let i = 0; i <= 45; i++) { const d = addDays(today, i); if (isWorking(d)) workDays.push(iso(d)); }
  const dueSoon = () => workDays[Math.min(workDays.length - 1, Math.floor(rand() * rand() * 30))];
  const dueBy = (h) => workDays[Math.min(workDays.length - 1, Math.floor(h * h * Math.min(30, workDays.length)))];
  const isDayOff = (err) => /تعطیل|جمعه|روز کاری|روزهای گذشته/.test(String(err));
  const onWorkingDay = async (date, send) => {
    let d = date;
    for (let i = 0; i < 15; i++) {
      try { return await send(d); } catch (err) { if (!isDayOff(err)) throw err; d = iso(addDays(new Date(d + 'T00:00:00Z'), 1)); }
    }
    throw new Error('no working day found after ' + date);
  };

  // Roles. A name the permission catalogue does not know is left out (and shown once).
  const catalog = (await call(admin, 'GET', '/api/identity/permissions')).flatMap((m) => m.permissions);
  const permId = (names) => names.map((n) => { const id = catalog.find((p) => p.name === n)?.id; if (!id) fail('permission ' + n)('not in the catalogue'); return id; }).filter(Boolean);
  const roles = await call(admin, 'GET', `/api/identity/roles?tenantId=${TENANT}`);
  const ensureRole = async (name, description, perms) => {
    let role = roles.find((r) => r.name === name);
    if (!role) role = await call(admin, 'POST', '/api/identity/roles', { tenantId: TENANT, name, description });
    await call(admin, 'PUT', `/api/identity/roles/${role.id}/permissions`, { permissionIds: permId(perms) });
    return role.id;
  };
  const staffPerms = ['Tasks.View', 'Tasks.Create', 'Tasks.Edit', 'Tasks.Comment', 'Tasks.UploadFiles', 'Tasks.Assign', 'Notes.Manage',
    'Meetings.View', 'Meetings.Create', 'Workflow.View', 'groups.view_own', 'groups.manage_own', 'work_calendars.view'];
  const managerPerms = [...staffPerms, 'Tasks.Delete', 'Tasks.ManageTags', 'Tasks.ManageRecurring', 'Workflow.Approve', 'Workflow.Reject',
    'Meetings.Edit', 'Meetings.Delete', 'Meetings.InviteHigher', 'users.view'];
  const staffRole = await ensureRole('کارمند', 'کار روزمره: فعالیت، یادداشت، جلسه و گفتگو', staffPerms);
  const managerRole = await ensureRole('مدیر واحد', 'به‌علاوهٔ تأیید و حذف فعالیت و ویرایش جلسه‌ها', managerPerms);
  log('roles ready');

  // Users (skipped when they already exist: same first and last name). The list is paged (100 at most per page).
  const existing = [];
  for (let page = 1; ; page++) {
    const r = await call(admin, 'GET', `/api/identity/users?tenantId=${TENANT}&pageNumber=${page}&pageSize=100`);
    existing.push(...r.items);
    if (existing.length >= r.totalCount || !r.items.length) break;
  }
  const takenPhones = new Set(existing.map((u) => u.phoneNumber).filter(Boolean));
  const newPhone = () => { for (;;) { const p = '0912' + String(Math.floor(1000000 + idRand() * 8999999)); if (!takenPhones.has(p)) { takenPhones.add(p); return p; } } };
  const users = [];
  let madeUsers = 0;
  for (const person of PEOPLE_ALL) {
    const { first, last, team, isManager, title } = person;
    let u = existing.find((x) => x.firstName === first && x.lastName === last);
    if (!u) {
      for (let attempt = 0; ; attempt++) {
        try {
          u = await call(admin, 'POST', '/api/identity/users', {
            address: 'تهران، خیابان ولیعصر، برج فناوری پارس، طبقهٔ ۸', fatherName: hpick(['محمد', 'علی', 'حسن', 'رضا', 'احمد', 'مهدی'], person.code * 3 + 1),
            birthDate: `19${80 + Math.floor(hash(person.code * 5 + 2) * 18)}-0${1 + Math.floor(hash(person.code * 7 + 3) * 9)}-1${Math.floor(hash(person.code * 11 + 4) * 9)}`,
            avatarUrl: '', tenantId: TENANT, username: nationalCode(), firstName: first, lastName: last, phoneNumber: newPhone(), password: PASSWORD, email: null, isActive: true,
          });
          break;
        } catch (err) { if (attempt >= 4 || !/ 40[09] /.test(String(err))) throw err; }
      }
      await call(admin, 'PUT', `/api/identity/users/${u.id}/roles`, { roleIds: [isManager ? managerRole : staffRole] });
      madeUsers++;
    }
    users.push({ id: u.id, username: u.username, name: `${first} ${last}`, team, isManager, title });
  }
  log(`users: ${users.length} (${madeUsers} new)`);
  const personOf = new Map(PEOPLE_ALL.map((p, i) => [p, users[i]]));
  const tokens = new Map();
  const tokenOf = async (u) => { if (!tokens.has(u.id)) tokens.set(u.id, await login(u.username, PASSWORD)); return tokens.get(u.id); };
  const byTeam = (t) => users.filter((u) => u.team === t);
  const managerOf = (t) => byTeam(t).find((u) => u.isManager);

  // Org chart: company, one unit per team, posts (staff under their manager) and appointments
  const OS = '/api/org-structure';
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
    const holderOf = (post) => (post.holders || []).map((h) => h.userId);
    const people = byTeam(key);
    const manager = people.find((u) => u.isManager);
    let managerPost = posts.find((x) => holderOf(x).includes(manager.id));
    if (!managerPost) {
      managerPost = await call(admin, 'POST', `${OS}/units/${node.id}/positions`, { positionId: posManager.id, code: null, parentPostId: null });
      await call(admin, 'PUT', `${OS}/assignments/${manager.id}`, { companyId: company.id, unitId: node.id, positionId: posManager.id, managerUserId: null, unitPositionIds: [managerPost.id] });
    }
    for (const u of people.filter((x) => !x.isManager)) {
      if (posts.some((x) => holderOf(x).includes(u.id))) continue;
      const position = u.title.includes('ارشد') ? posSenior : posStaff;
      const post = await call(admin, 'POST', `${OS}/units/${node.id}/positions`, { positionId: position.id, code: null, parentPostId: managerPost.id });
      await call(admin, 'PUT', `${OS}/assignments/${u.id}`, { companyId: company.id, unitId: node.id, positionId: position.id, managerUserId: manager.id, unitPositionIds: [post.id] });
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

  // Everyone who owns a task signs in now, one after the other (sign-in is throttled), before the parallel part.
  for (const u of users) await tokenOf(u);
  log(`signed in: ${tokens.size}`);

  // Tasks (a title that already exists is not made again)
  const existingTitles = new Set();
  for (let page = 1; ; page++) {
    const r = await call(admin, 'GET', `/api/task-management/tasks?pageNumber=${page}&pageSize=100`);
    r.items.forEach((t) => existingTitles.add(t.title));
    if (page * 100 >= r.totalCount || !r.items.length) break;
  }
  const created = [];
  let skipped = 0;
  const todo = TASK_SPECS.filter((s) => { if (existingTitles.has(s.title)) { skipped++; return false; } return true; });
  let doneTasks = 0;
  await runPool(todo, CONCURRENCY, async (s) => {
    const owner = personOf.get(s.owner);
    const responsible = s.responsible.map((p) => personOf.get(p));
    const people = byTeam(s.team);
    const tk = await tokenOf(owner);
    try {
      const t = await onWorkingDay(dueBy(s.dueH), (dueDate) => call(tk, 'POST', '/api/task-management/tasks', {
        title: s.title, dueDate, dueTime: s.dueTime, priority: s.priority, description: s.description || null, isProject: false,
        assignedUserId: responsible[0].id, responsibleUserIds: responsible.map((r) => r.id), responsiblePostIds: null,
        assignedUserGroupId: teamIds[s.team], assigneeUserIds: people.map((p) => p.id), allowAssigneeStatusUpdate: true,
        subTasks: [], tags: s.tags,
      }));
      if (s.status !== 'Todo') await call(tk, 'PATCH', `/api/task-management/tasks/${t.id}/status`, { status: s.status });
      created.push({ id: t.id, team: s.team, owner, responsible, status: s.status, title: s.title, isNew: true });
    } catch (err) { fail('task')(err); }
    if (++doneTasks % 100 === 0) log(`  tasks ${doneTasks}/${todo.length}`);
  });

  for (const [key, title, steps] of PROJECTS) {
    if (existingTitles.has(title)) { skipped++; continue; }
    const m = managerOf(key); const tk = await tokenOf(m); const people = byTeam(key);
    const start = workDays[1]; const end = workDays[Math.min(workDays.length - 1, 25 + Math.floor(rand() * 10))];
    try {
      const t = await onWorkingDay(end, (dueDate) => call(tk, 'POST', '/api/task-management/tasks', {
        title, dueDate, dueTime: null, priority: 'High', description: `پروژهٔ اصلی ${TEAMS[key]} در این فصل.`, isProject: true,
        assignedUserId: m.id, responsibleUserIds: [m.id], responsiblePostIds: null, assignedUserGroupId: teamIds[key], assigneeUserIds: people.map((p) => p.id),
        allowAssigneeStatusUpdate: true, charterDescription: `هدف: ${title.replace('پروژهٔ ', '')} تا پایان فصل.`, charterProjectManager: m.name,
        charterStartDate: start, charterEndDate: end,
        subTasks: steps.map((s, i) => ({ title: s, importance: i === 0 ? 'High' : 'Medium', startDate: workDays[1 + i * 4] || end, endDate: workDays[4 + i * 4] || end, startTime: null, endTime: null, sortOrder: i, isGeneratedOccurrence: false, assigneeUserIds: [people[(i + 1) % people.length].id] })),
        tags: ['پروژه'],
      }));
      const done = Math.floor(rand() * (steps.length - 1));
      const subs = [...t.subTasks].sort((a, b) => a.sortOrder - b.sortOrder);
      for (let i = 0; i < done; i++) {
        await call(tk, 'PATCH', `/api/task-management/subtasks/${subs[i].id}/status`, { status: 'InProgress' }).catch(fail('subtask status'));
        await call(tk, 'PATCH', `/api/task-management/subtasks/${subs[i].id}/status`, { status: 'Completed' }).catch(fail('subtask status'));
      }
      created.push({ id: t.id, team: key, owner: m, responsible: [m], status: 'InProgress', title, isNew: true, isProject: true });
    } catch (err) { fail('project')(err); }
  }
  log(`tasks made: ${created.length}, already there: ${skipped}`);

  // Comments (owner or a responsible person), on new tasks only
  let commentCount = 0;
  const commented = created.filter((t) => chance(COMMENT_RATE));
  await runPool(commented, CONCURRENCY, async (t) => {
    const n = 1 + Math.floor(rand() * 3);
    for (let i = 0; i < n; i++) {
      const who = i % 2 === 0 ? t.responsible[0] : t.owner;
      await call(await tokenOf(who), 'POST', `/api/task-management/tasks/${t.id}/comments`, { text: pick(COMMENTS) }).then(() => commentCount++).catch(fail('comment'));
    }
  });
  log(`comments: ${commentCount}`);

  if (!process.env.ONLY_TASKS) {
    await meetingsNotesAndChat();
  }
  await overdueStep();
  await writeUsersCsv();
  log('done');

  // ---------------------------------------------------------------- meetings, notes, chat
  async function meetingsNotesAndChat() {
    // Meetings: the 8 below, repeated on later working days
    const MEETINGS = [
      ['جلسهٔ هفتگی تیم فنی', 'tech', '09:30', '10:30', 'اتاق جلسات ۲'], ['بازبینی طراحی داشبورد', 'product', '11:00', '12:00', 'اتاق جلسات ۱'],
      ['جلسهٔ بودجهٔ سال آینده', 'finance', '10:00', '11:30', 'دفتر مدیرعامل'], ['هماهنگی کمپین نسخهٔ جدید', 'marketing', '14:00', '15:00', 'آنلاین'],
      ['مرور تیکت‌های هفته', 'support', '13:00', '13:45', 'اتاق پشتیبانی'], ['برنامه‌ریزی اسپرینت بعدی', 'tech', '10:00', '11:30', 'اتاق جلسات ۲'],
      ['جلسهٔ دمو با مشتری', 'marketing', '15:00', '16:00', 'دفتر مشتری'], ['کمیتهٔ استخدام', 'finance', '12:00', '13:00', 'اتاق جلسات ۱'],
    ];
    let meetings = 0;
    for (let round = 0; round < MEETING_ROUNDS; round++) {
      for (let i = 0; i < MEETINGS.length; i++) {
        const [baseTitle, key, s, e, loc] = MEETINGS[i]; const m = managerOf(key);
        const title = round === 0 ? baseTitle : `${baseTitle} (${round + 1})`;
        const mtk = await tokenOf(m);
        await onWorkingDay(workDays[1 + i + round * 6] || workDays[1], (date) => call(mtk, 'POST', '/api/calendar/meetings', {
          title, type: loc === 'آنلاین' ? 'online' : 'meeting', date, startTime: s, endTime: e, location: loc, organizerUserId: m.id,
          description: '', participantIds: byTeam(key).map((u) => u.id), agenda: [{ title: 'مرور کارهای هفتهٔ گذشته', durationMinutes: 20, presenterUserId: m.id }, { title: 'برنامهٔ هفتهٔ آینده', durationMinutes: 25, presenterUserId: null }],
          minutes: '', actions: [], taskIds: [], sendInvitationSms: false,
        })).then(() => meetings++).catch(fail('meeting'));
      }
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
        await call(tk, 'POST', '/api/task-management/notes', { title, content, color, isPinned }).then(() => notes++).catch(fail('note'));
      }
    }
    log(`notes: ${notes}`);

    // Chat: team threads and direct conversations (60 sends per minute per user: a 429 is waited out)
    const sendForm = (text) => { const f = new FormData(); f.append('text', text); return f; };
    const TEAM_MSGS = ['سلام همگی، صبح بخیر', 'گزارش هفتگی را تا پنجشنبه بفرستید لطفاً', 'جلسهٔ فردا ساعت ۱۰ برگزار می‌شود', 'نسخهٔ جدید روی سرور تست است، لطفاً بررسی کنید',
      'کسی فایل قرارداد آریا را دارد؟', 'من در پوشهٔ مشترک گذاشتم', 'ممنون 🙏', 'امروز بعدازظهر مرخصی هستم، کارهای فوری را به همکارانم بسپارید', 'تبریک بابت تحویل به‌موقع پروژه 👏',
      'لطفاً وضعیت فعالیت‌هایتان را به‌روز کنید', 'جلسهٔ دمو با مشتری به روز بعد منتقل شد', 'یادآوری: فردا ساعت ۹ جلسهٔ هماهنگی داریم'];
    let msgs = 0;
    const perTeam = Math.min(TEAM_MSGS.length, 5 + Math.floor(EXTRA_STAFF_PER_TEAM / 4));
    for (const key of Object.keys(TEAMS)) {
      for (const text of pickN(TEAM_MSGS, perTeam)) {
        const u = pick(byTeam(key));
        await call(await tokenOf(u), 'POST', `/api/chat/teams/${teamIds[key]}/messages`, undefined, sendForm(text)).then(() => msgs++).catch(fail('chat message'));
      }
    }
    const DM = [['سلام، وقت داری امروز دربارهٔ گزارش صحبت کنیم؟', 'سلام، بله ساعت ۱۴ خوب است', 'عالی، می‌بینمت'],
      ['فایل طراحی را دیدی؟', 'بله، دو نکته دارم که در گفتگوی فعالیت نوشتم', 'ممنون، اصلاح می‌کنم'],
      ['مشتری دربارهٔ موعد تحویل پرسید', 'تا آخر هفته تحویل می‌دهیم', 'به او اطلاع می‌دهم']];
    // Direct chat within the chart (OrgStructure:LimitChatToHierarchy): colleagues of a unit, or a person and the manager
    const directCount = 8 + Math.floor(users.length / 4);
    for (let i = 0; i < directCount; i++) {
      const team = pick(Object.keys(TEAMS)); const [a, b] = pickN(byTeam(team), 2); const conv = pick(DM);
      for (let k = 0; k < conv.length; k++) {
        const from = k % 2 === 0 ? a : b; const to = k % 2 === 0 ? b : a;
        await call(await tokenOf(from), 'POST', `/api/chat/direct/${to.id}/messages`, undefined, sendForm(conv[k])).then(() => msgs++).catch(fail('chat message'));
      }
    }
    log(`chat messages: ${msgs}`);
  }

  // ---------------------------------------------------------------- files for the overdue step and the sign-in list
  async function overdueStep() {
    // Ids of open tasks, for the overdue step done in SQL (the API refuses past due dates by design)
    const open = created.filter((t) => t.isNew && !t.isProject && t.status !== 'Completed');
    const overdue = pickN(open, Math.min(Math.max(14, Math.floor(open.length / 12)), open.length)).map((t) => t.id);
    const fs = await import('node:fs');
    fs.writeFileSync(new URL('./overdue-ids.txt', import.meta.url), overdue.join('\n'));
    log(`overdue candidates: ${overdue.length}`);
  }
  async function writeUsersCsv() {
    const fs = await import('node:fs');
    const rows = ['username,name,team,title,manager', ...users.map((u) => [u.username, u.name, TEAMS[u.team], u.title, u.isManager ? 'yes' : ''].join(','))];
    fs.writeFileSync(new URL('./demo-users.csv', import.meta.url), '﻿' + rows.join('\n'));
    log(`demo-users.csv: ${users.length} people, password ${PASSWORD}`);
  }
}

main().catch((err) => { console.error(err); process.exit(1); });
