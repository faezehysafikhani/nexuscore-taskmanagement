<#
.SYNOPSIS
  کاربر آزمایشی با نام فارسی واقعی می‌سازد و هر کدام را تصادفی در یک بخش (گره) و یک سمت از چارت سازمانی یک شرکت می‌گذارد.

.DESCRIPTION
  فقط از API خود سامانه استفاده می‌کند (همان کاری که «مدیریت کاربران» و «ثبت جایگاه» در صفحه انجام می‌دهند)، پس رمزها و
  قاعده‌های سامانه همان‌طور اعمال می‌شوند. پیش از اجرا باید شرکت، بخش‌ها (گره‌ها) و سمت‌ها ساخته شده باشند.
  - نام کاربری هر نفر یک کد ملی معتبر (با رقم کنترل درست) و شماره همراه 0912xxxxxxx است؛ هر دو تصادفی و یکتا.
  - مجمع عمومی، هیأت مدیره و کمیته‌ها کسی نمی‌گیرند. سمت‌ها از سمت‌های رده‌ی کارشناسی انتخاب می‌شوند (سمت‌هایی که سمت دیگری
    زیرشان نیست، مثل «کارشناس» یا «رئیس»)؛ رئیس کل، معاون یا رئیس هیأت مدیره به کسی داده نمی‌شود.
  - فهرست کاربران ساخته‌شده (نام، نام کاربری، همراه، بخش، سمت) در فایل CSV کنار اسکریپت ذخیره می‌شود.
  - فقط برای محیط آزمایشی: کاربرها واقعاً ساخته می‌شوند و همه یک رمز دارند.

.EXAMPLE
  .\seed-test-users.ps1 -ApiUrl http://localhost:5000 -AdminPassword 'رمز مدیر' -Count 50
.EXAMPLE
  .\seed-test-users.ps1 -ApiUrl http://api-host:5000 -AdminPassword '...' -CompanyCode PB -Count 120 -UserPassword 'Test@Pass#2026'
#>
param(
  [Parameter(Mandatory = $true)][string]$ApiUrl,
  [string]$AdminUser = 'admin',
  [Parameter(Mandatory = $true)][string]$AdminPassword,
  # کد شرکت (مثلاً PB یا C-001)؛ خالی: اولین شرکت فعال.
  [string]$CompanyCode = '',
  [ValidateRange(1, 2000)][int]$Count = 50,
  # رمز همه‌ی کاربران آزمایشی (حداقل ۱۲ نویسه، سه نوع از حرف کوچک/بزرگ/عدد/نماد).
  [string]$UserPassword = 'Test@Pass#2026',
  # عدد ثابت = همان کاربران در هر بار اجرا (با کد ملی/همراه تازه اگر قبلی گرفته شده باشد)؛ 0 = هر بار تصادفی.
  [int]$Seed = 0
)

$ErrorActionPreference = 'Stop'
$ApiUrl = $ApiUrl.TrimEnd('/')
$random = if ($Seed -ne 0) { New-Object System.Random $Seed } else { New-Object System.Random }

function Invoke-Api([string]$Method, [string]$Path, $Body = $null) {
  $headers = @{ Accept = 'application/json' }
  if ($script:Token) { $headers.Authorization = "Bearer $($script:Token)" }
  $req = @{ Method = $Method; Uri = "$ApiUrl$Path"; Headers = $headers; UseBasicParsing = $true }
  if ($null -ne $Body) {
    # فارسی: بدنه همیشه UTF-8 فرستاده می‌شود (Windows PowerShell 5.1 هم).
    $req.Body = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6 -Compress))
    $req.ContentType = 'application/json; charset=utf-8'
  }
  try {
    $response = Invoke-WebRequest @req
    $text = [System.Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
  } catch {
    $detail = $_.ErrorDetails.Message
    if (-not $detail -and $_.Exception.Response) {
      try { $detail = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch { }
    }
    throw "$Method $Path : $($_.Exception.Message) $detail"
  }
}

function New-NationalCode {
  while ($true) {
    $digits = 1..9 | ForEach-Object { $random.Next(0, 10) }
    if (($digits | Select-Object -Unique).Count -lt 3) { continue }
    $sum = 0
    for ($i = 0; $i -lt 9; $i++) { $sum += $digits[$i] * (10 - $i) }
    $r = $sum % 11
    $check = if ($r -lt 2) { $r } else { 11 - $r }
    return (($digits -join '') + $check)
  }
}

$maleNames = @('علی','محمد','حسین','رضا','مهدی','امیر','حامد','سعید','مجید','احمد','مصطفی','جواد','کاظم','بهروز','فرهاد','کامران','پیمان','آرش','سینا','میلاد','نیما','بهزاد','داوود','یوسف','محسن','حمید','وحید','سجاد','مرتضی','ابوالفضل')
$femaleNames = @('فاطمه','زهرا','مریم','سارا','نرگس','لیلا','الهام','مینا','شیما','نازنین','فرزانه','سمیه','مهسا','نگار','پریسا','ریحانه','آزاده','سپیده','هانیه','کیمیا','الناز','ساناز','زینب','معصومه','طاهره','شیرین','رویا','بهاره','نسرین','مژگان')
$lastNames = @('محمدی','حسینی','احمدی','رضایی','کریمی','موسوی','جعفری','صادقی','کاظمی','رحیمی','ابراهیمی','هاشمی','قاسمی','نوری','عباسی','طاهری','شریفی','اکبری','صالحی','مرادی','حیدری','یوسفی','فرهادی','نجفی','باقری','سلطانی','زارعی','امینی','کرمانی','تهرانی','شیرازی','اصفهانی','نیک‌نام','بهرامی','منصوری','ملکی','فتحی','رستمی','خسروی','پاکزاد')

Write-Host "ورود با $AdminUser ..."
$login = Invoke-Api POST '/api/identity/auth/login' @{ identifier = $AdminUser; password = $AdminPassword }
$script:Token = $login.accessToken
if (-not $script:Token) { throw 'ورود انجام نشد (توکن برنگشت).' }
$me = Invoke-Api GET '/api/identity/auth/me'
$tenantId = if ($me.tenantId) { $me.tenantId } else { $me.user.tenantId }

$companies = @(Invoke-Api GET '/api/org-structure/companies' | Where-Object { $_.isActive })
$company = if ($CompanyCode) { $companies | Where-Object { $_.code -eq $CompanyCode } | Select-Object -First 1 } else { $companies | Select-Object -First 1 }
if (-not $company) { throw "شرکت «$CompanyCode» پیدا نشد. کدهای موجود: $(($companies | ForEach-Object { $_.code }) -join '، ')" }

$governance = 'مجمع|هی[ئأ]ت مدیره|کمیته'
$nodes = @(Invoke-Api GET "/api/org-structure/nodes?companyId=$($company.id)" | Where-Object { $_.isActive -and -not ($_.nodeTypeName -match $governance) -and -not ($_.name -match $governance) })
$positions = @(Invoke-Api GET '/api/org-structure/positions' | Where-Object { $_.isActive -and $_.parentPositionId -and (@($_.companyIds) -contains $company.id) })
# Working-level positions: not governance titles, and (when there are such) the ones no other position reports to
# (e.g. «کارشناس»، «رئیس»), so nobody is made a deputy or a board chair of a random department.
$positions = @($positions | Where-Object { -not ($_.title -match $governance) -and -not ($_.title -match 'مدیرعامل|مدیر عامل|قائم') })
$superiorIds = @($positions | ForEach-Object { $_.parentPositionId })
$leaves = @($positions | Where-Object { $superiorIds -notcontains $_.id })
if ($leaves.Count -gt 0) { $positions = $leaves }
if ($nodes.Count -eq 0) { throw "شرکت «$($company.name)» بخشی (گره‌ای) برای گذاشتن افراد ندارد." }
if ($positions.Count -eq 0) { throw "شرکت «$($company.name)» سمتی با مافوق ندارد؛ اول سمت‌ها را بسازید." }
Write-Host "شرکت «$($company.name)»: $($nodes.Count) بخش و $($positions.Count) سمت. ساخت $Count کاربر ..."

$usedPhones = @{}
$made = New-Object System.Collections.Generic.List[object]
$failed = 0
for ($n = 1; $n -le $Count; $n++) {
  $female = $random.Next(0, 2) -eq 1
  $first = if ($female) { $femaleNames[$random.Next($femaleNames.Count)] } else { $maleNames[$random.Next($maleNames.Count)] }
  $last = $lastNames[$random.Next($lastNames.Count)]
  $node = $nodes[$random.Next($nodes.Count)]
  $position = $positions[$random.Next($positions.Count)]
  $user = $null
  for ($try = 0; $try -lt 5 -and -not $user; $try++) {
    do { $phone = '0912' + ('{0:D7}' -f $random.Next(0, 10000000)) } while ($usedPhones.ContainsKey($phone))
    $usedPhones[$phone] = $true
    $code = New-NationalCode
    try {
      $user = Invoke-Api POST '/api/identity/users/' @{
        tenantId = $tenantId; username = $code; firstName = $first; lastName = $last; phoneNumber = $phone
        password = $UserPassword; isActive = $true; notifySms = $false
      }
    } catch {
      # کد ملی یا همراهِ تکراری: با عدد تازه دوباره.
      if ($try -eq 4) { Write-Warning "کاربر $n ساخته نشد: $_" }
    }
  }
  if (-not $user) { $failed++; continue }
  $userId = if ($user.id) { $user.id } else { $user.userId }
  try {
    Invoke-Api PUT "/api/org-structure/assignments/$userId" @{ companyId = $company.id; unitId = $node.id; positionId = $position.id; managerUserId = $null } | Out-Null
    $placed = $true
  } catch {
    Write-Warning "«$first $last» ساخته شد ولی در «$($node.name)» گذاشته نشد: $_"
    $placed = $false
  }
  $made.Add([pscustomobject]@{ نام = "$first $last"; نام_کاربری = $code; همراه = $phone; بخش = $node.name; سمت = $position.title; جایگاه_ثبت_شد = $placed })
  Write-Host ("[{0}/{1}] {2} {3} → {4} / {5}" -f $n, $Count, $first, $last, $node.name, $position.title)
}

$csv = Join-Path $PSScriptRoot ("test-users-{0}.csv" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$lines = $made | ConvertTo-Csv -NoTypeInformation
[System.IO.File]::WriteAllLines($csv, $lines, (New-Object System.Text.UTF8Encoding $true))
Write-Host ""
Write-Host "تمام: $($made.Count) کاربر ساخته شد$(if ($failed) { "، $failed ساخته نشد" })."
Write-Host "رمز همه: $UserPassword"
Write-Host "فهرست: $csv"
