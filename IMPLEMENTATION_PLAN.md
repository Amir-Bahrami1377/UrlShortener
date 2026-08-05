# راهنمای پیاده‌سازی گام‌به‌گام — سرویس کوتاه‌کننده لینک دانلود اسناد

> **مخاطب:** ایجنت توسعه‌دهنده (Coding Agent)
> **هدف:** پیاده‌سازی کامل پروژه از صفر تا استقرار، بدون نیاز به تصمیم‌گیری معماری اضافه.
> **قانون کلی:** هر گام یک «تعریف اتمام» (DoD) دارد. تا DoD یک گام سبز نشده، به گام بعد نرو.

---

## فهرست

- [۰. خلاصه اجرایی](#۰-خلاصه-اجرایی)
- [۱. مشخصات قطعی‌شده](#۱-مشخصات-قطعیشده)
- [۲. معماری](#۲-معماری)
- [M0 — پی‌ریزی](#m0--پیریزی)
- [M1 — لایه داده و ذخیره‌سازی](#m1--لایه-داده-و-ذخیرهسازی)
- [M2 — پنل مدیریت](#m2--پنل-مدیریت)
- [M3 — API بارگذاری](#m3--api-بارگذاری)
- [M4 — دانلود با OTP](#m4--دانلود-با-otp)
- [M5 — صف پیامک](#m5--صف-پیامک)
- [M6 — گزارشات](#m6--گزارشات)
- [M7 — نگهداشت و پاکسازی](#m7--نگهداشت-و-پاکسازی)
- [M8 — رصدپذیری، تست و استقرار](#m8--رصدپذیری-تست-و-استقرار)
- [پیوست A — کلیدهای Redis](#پیوست-a--کلیدهای-redis)
- [پیوست B — کدهای خطا](#پیوست-b--کدهای-خطا)
- [پیوست C — چک‌لیست نهایی](#پیوست-c--چکلیست-نهایی)

---

## ۰. خلاصه اجرایی

سامانه‌ای که یک سیستم بیرونی (سامانه شهرسازی) با آن ارتباط می‌گیرد:

1. سامانه مبدأ یک **فایل** (PDF یا تصویر) به‌همراه متادیتای پرونده ارسال می‌کند.
2. سرویس فایل را روی دیسک ذخیره می‌کند، یک **لینک کوتاه شش‌کاراکتری** می‌سازد و آن را در پاسخ برمی‌گرداند.
3. یک **پیامک حاوی لینک** برای شماره متقاضی ارسال می‌شود (متن از قالب مخصوص همان `reportId`).
4. متقاضی روی لینک کلیک می‌کند → صفحه احراز هویت → درخواست **کد یک‌بارمصرف (OTP)** → دریافت پیامک OTP → ورود کد → **دانلود فایل**.
5. فایل‌ها پس از **۹۰ روز** منقضی، پس از ۷ روز مهلت (Grace) **فیزیکی حذف** و لینک‌ها غیرفعال می‌شوند.

**تمرکز ویژه:** جداسازی کامل مسیر پیامک OTP از مسیر پیامک انبوه. این مهم‌ترین تصمیم معماری پروژه است — بدون آن، در روز پیک کد OTP بعد از انقضای دو دقیقه‌ای می‌رسد و سرویس از کار می‌افتد.

---

## ۱. مشخصات قطعی‌شده

| پارامتر | مقدار |
|---|---|
| پلتفرم | .NET 10 (LTS)، C# 14 |
| دیتابیس | SQL Server 2019+ / EF Core 10 |
| کش | Redis 7+ (StackExchange.Redis) |
| صف | **Redis Streams** با Consumer Group |
| ذخیره فایل | دیسک محلی، ۱ TB SSD |
| Sharding مسیر | `{root}/{yyyy}/{MM}/{dd}/{shard}/{guid}.bin` |
| حداکثر حجم فایل | ۳ MB |
| فرمت مجاز | PDF, JPEG, PNG, TIFF |
| بار عادی | ۵۰۰ فایل/روز |
| بار پیک | ۱۰۰٬۰۰۰ فایل — **سالی یک بار** |
| تفکیک بارگذاری/ارسال | بله (`sendSmsImmediately`) |
| نگهداشت | ۹۰ روز + ۷ روز Grace، حذف فیزیکی |
| فرمت لینک | `https://{domain}/s/{6chars}` (Base62) |
| احراز هویت دانلود | OTP شش‌رقمی، عمر ۲ دقیقه |
| منبع OTP | پنل پیامکی **همان کلاینت** |
| چندمستأجری | بله |

---

## ۲. معماری

### ۲.۱ ساختار Solution

```
UrlShortener.sln
├─ src/
│  ├─ Shortener.Domain/           # Entity، Enum، Exception دامنه — بدون وابستگی
│  ├─ Shortener.Application/      # Interface، DTO، Service، Validator
│  ├─ Shortener.Infrastructure/   # EF Core، Redis، FileStorage، SmsProviders
│  ├─ Shortener.Api/              # Minimal API — بارگذاری فایل
│  ├─ Shortener.Admin/            # MVC — پنل مدیریت
│  ├─ Shortener.PublicWeb/        # Razor Pages — /s/{code} و /d/{token}
│  └─ Shortener.Worker/           # BackgroundService — صف پیامک و Job ها
└─ tests/
   ├─ Shortener.UnitTests/
   └─ Shortener.IntegrationTests/
```

**قواعد وابستگی (اجباری):**

```
Domain        ← هیچ وابستگی
Application   ← Domain
Infrastructure← Application, Domain
Api/Admin/PublicWeb/Worker ← Infrastructure, Application, Domain
```

هیچ‌گاه `Domain` به `Infrastructure` وابسته نشود. اگر لازم شد، اینترفیس را در `Application` تعریف کن.

### ۲.۲ جریان داده

```
سامانه مبدأ ──POST /api/v1/links (multipart)──▶ Shortener.Api
                                                  │
                                    ┌─────────────┼──────────────┐
                                    ▼             ▼              ▼
                            دیسک: فایل    SQL: StoredFile   Redis: link:{code}
                                          + ShortLink
                                          + OutboxMessage
                                                  │
                                    Worker ◀──────┘
                                       │
                          ┌────────────┴────────────┐
                          ▼                         ▼
                  Stream sms:bulk           Stream sms:otp
                          │                         │
                  BulkSmsWorker              OtpSmsWorker
                  (نرخ محدود)                (اولویت بالا)
                          │                         │
                          ▼                         ▼
                    پنل پیامکی کلاینت (خط انبوه / الگوی OTP)

متقاضی ──/s/{code}──▶ PublicWeb ──▶ OTP ──▶ /d/{token} ──▶ استریم فایل از دیسک
```

### ۲.۳ متغیرهای محیطی و پیکربندی

`appsettings.json` مشترک (بخش‌های اختصاصی هر پروژه جداگانه):

```jsonc
{
  "ConnectionStrings": {
    "Default": "Server=.;Database=UrlShortener;Trusted_Connection=True;TrustServerCertificate=True",
    "Redis": "localhost:6379,abortConnect=false"
  },
  "FileStorage": {
    "RootPath": "D:\\ShortenerFiles",
    "MaxFileSizeBytes": 3145728,
    "AllowedExtensions": [ ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff" ],
    "TempFileMaxAgeHours": 6,
    "DiskWarningThresholdPercent": 70,
    "DiskRejectThresholdPercent": 90
  },
  "ShortLink": {
    "BaseUrl": "https://links.example.ir",
    "CodeLength": 6,
    "MaxGenerationRetries": 5,
    "CacheTtlMinutes": 60
  },
  "Otp": {
    "Length": 6,
    "TtlSeconds": 120,
    "ResendCooldownSeconds": 90,
    "MaxRequestsPerLinkPerHour": 3,
    "MaxRequestsPerIpPerHour": 10,
    "MaxVerifyAttempts": 5,
    "LockoutMinutes": 15
  },
  "Download": {
    "TokenTtlSeconds": 300
  },
  "Retention": {
    "DefaultRetentionDays": 90,
    "DefaultGraceDays": 7,
    "DeleteBatchSize": 5000,
    "DeleteBatchDelayMs": 500,
    "JobWindowStartHour": 1,
    "JobWindowEndHour": 5
  },
  "Queue": {
    "BulkStream": "sms:bulk",
    "OtpStream": "sms:otp",
    "DeadStream": "sms:dead",
    "BulkConsumerGroup": "bulk-workers",
    "OtpConsumerGroup": "otp-workers",
    "ClaimIdleMinutes": 5,
    "MaxDeliveryAttempts": 5,
    "BulkDefaultRatePerMinute": 3000,
    "StreamMaxLen": 100000
  },
  "Serilog": { }
}
```

**نکته:** رشته اتصال و کلیدهای حساس در Production از `User Secrets` یا متغیر محیطی خوانده شوند، نه از فایل.

---

## M0 — پی‌ریزی

**حجم:** کوچک · **وابستگی:** ندارد

### گام M0.1 — ساخت Solution

```bash
dotnet new sln -n UrlShortener

dotnet new classlib -n Shortener.Domain        -o src/Shortener.Domain        -f net10.0
dotnet new classlib -n Shortener.Application   -o src/Shortener.Application   -f net10.0
dotnet new classlib -n Shortener.Infrastructure -o src/Shortener.Infrastructure -f net10.0
dotnet new webapi   -n Shortener.Api           -o src/Shortener.Api           -f net10.0
dotnet new mvc      -n Shortener.Admin         -o src/Shortener.Admin         -f net10.0
dotnet new webapp   -n Shortener.PublicWeb     -o src/Shortener.PublicWeb     -f net10.0
dotnet new worker   -n Shortener.Worker        -o src/Shortener.Worker        -f net10.0
dotnet new xunit    -n Shortener.UnitTests     -o tests/Shortener.UnitTests   -f net10.0
dotnet new xunit    -n Shortener.IntegrationTests -o tests/Shortener.IntegrationTests -f net10.0

dotnet sln add (ls -r **/*.csproj)   # PowerShell
# یا: find . -name "*.csproj" -exec dotnet sln add {} \;
```

### گام M0.2 — ارجاعات پروژه

```bash
dotnet add src/Shortener.Application reference src/Shortener.Domain
dotnet add src/Shortener.Infrastructure reference src/Shortener.Application
dotnet add src/Shortener.Api reference src/Shortener.Infrastructure
dotnet add src/Shortener.Admin reference src/Shortener.Infrastructure
dotnet add src/Shortener.PublicWeb reference src/Shortener.Infrastructure
dotnet add src/Shortener.Worker reference src/Shortener.Infrastructure
dotnet add tests/Shortener.UnitTests reference src/Shortener.Application
dotnet add tests/Shortener.IntegrationTests reference src/Shortener.Infrastructure
```

### گام M0.3 — پکیج‌های NuGet

| پروژه | پکیج‌ها |
|---|---|
| **Domain** | — (خالی نگه‌دار) |
| **Application** | `FluentValidation`، `Microsoft.Extensions.Logging.Abstractions` |
| **Infrastructure** | `Microsoft.EntityFrameworkCore.SqlServer`، `Microsoft.EntityFrameworkCore.Design`، `StackExchange.Redis`، `Polly`، `Microsoft.AspNetCore.DataProtection`، `Microsoft.Extensions.Http` |
| **Api** | `Serilog.AspNetCore`، `Swashbuckle.AspNetCore` یا `Scalar.AspNetCore`، `FluentValidation.AspNetCore`، `AspNetCore.HealthChecks.SqlServer`، `AspNetCore.HealthChecks.Redis` |
| **Admin** | `Microsoft.AspNetCore.Identity.EntityFrameworkCore`، `Serilog.AspNetCore`، `ClosedXML` (خروجی Excel) |
| **PublicWeb** | `Serilog.AspNetCore` |
| **Worker** | `Serilog.Extensions.Hosting`، `Polly` |
| **Tests** | `FluentAssertions`، `Moq`، `Testcontainers.MsSql`، `Testcontainers.Redis`، `Microsoft.AspNetCore.Mvc.Testing` |

### گام M0.4 — Docker Compose برای توسعه

`docker-compose.dev.yml`:

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Dev_Passw0rd!"
    ports: [ "1433:1433" ]
    volumes: [ "sqldata:/var/opt/mssql" ]

  redis:
    image: redis:7-alpine
    command: redis-server --appendonly yes --appendfsync everysec
    ports: [ "6379:6379" ]
    volumes: [ "redisdata:/data" ]

volumes:
  sqldata:
  redisdata:
```

### گام M0.5 — Directory.Build.props

در ریشه:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`InvariantGlobalization` باید `false` باشد چون تاریخ شمسی و `PersianCalendar` استفاده می‌شود.

### گام M0.6 — CI پایه

`.github/workflows/ci.yml` یا معادل Azure DevOps: `restore` → `build` → `test` → انتشار Artifact.

### ✅ DoD مایلستون M0

- [ ] `dotnet build` بدون خطا و بدون Warning
- [ ] `docker compose -f docker-compose.dev.yml up -d` بالا می‌آید
- [ ] اتصال به SQL Server و Redis از کد تست شده
- [ ] CI سبز است

---

## M1 — لایه داده و ذخیره‌سازی

**حجم:** متوسط · **وابستگی:** M0

### گام M1.1 — Enum ها

`src/Shortener.Domain/Enums/`:

```csharp
public enum FileStatus : byte
{
    Active = 0,
    Expired = 1,
    PendingDelete = 2,
    Deleted = 3
}

public enum SmsStatus : byte
{
    Pending = 0,      // منتظر فرمان ارسال (sendSmsImmediately=false)
    Queued = 1,
    Sending = 2,
    Sent = 3,
    Delivered = 4,
    Failed = 5,
    Undelivered = 6,
    Cancelled = 7
}

public enum SmsMessageType : byte
{
    DownloadLink = 0,
    Otp = 1
}

public enum TemplateType : byte
{
    DownloadLink = 0,
    Otp = 1
}

public enum LinkAccessType : byte
{
    View = 0,
    OtpRequested = 1,
    VerifySuccess = 2,
    VerifyFailed = 3,
    Locked = 4,
    Download = 5,
    DownloadFailed = 6
}

public enum FileDeletionReason : byte
{
    RetentionPolicy = 0,
    Orphan = 1,
    Manual = 2
}

public enum OutboxStatus : byte
{
    Pending = 0,
    Published = 1,
    Failed = 2
}
```

### گام M1.2 — Entity ها

`src/Shortener.Domain/Entities/`. مشخصات دقیق ستون‌ها:

#### Client

| ستون | نوع | قید |
|---|---|---|
| `Id` | `int` | PK, Identity |
| `Name` | `nvarchar(200)` | NOT NULL |
| `Code` | `nvarchar(50)` | UNIQUE, NOT NULL |
| `IsActive` | `bit` | default 1 |
| `CreatedAt` | `datetime2(3)` | default `SYSUTCDATETIME()` |

#### ApiKey

| ستون | نوع | قید |
|---|---|---|
| `Id` | `int` | PK |
| `ClientId` | `int` | FK → Client, Restrict |
| `KeyHash` | `char(64)` | UNIQUE (SHA256 hex) |
| `KeyPrefix` | `nvarchar(12)` | برای نمایش در پنل |
| `Title` | `nvarchar(200)` | |
| `ExpiresAt` | `datetime2(3)?` | |
| `LastUsedAt` | `datetime2(3)?` | |
| `IsActive` | `bit` | default 1 |
| `RevokedAt` | `datetime2(3)?` | |
| `CreatedAt` | `datetime2(3)` | |

#### SmsProvider

| ستون | نوع | قید |
|---|---|---|
| `Id` | `int` | PK |
| `Code` | `nvarchar(50)` | UNIQUE — مثل `kavenegar` |
| `Name` | `nvarchar(200)` | |
| `SupportsPattern` | `bit` | |
| `SupportsDlr` | `bit` | |
| `IsActive` | `bit` | |

#### SmsAccount

| ستون | نوع | قید |
|---|---|---|
| `Id` | `int` | PK |
| `ClientId` | `int` | FK → Client |
| `SmsProviderId` | `int` | FK → SmsProvider |
| `Title` | `nvarchar(200)` | |
| `Purpose` | `tinyint` | `0=Bulk, 1=Otp` |
| `ApiKeyEnc` | `nvarchar(500)?` | رمزنگاری‌شده |
| `UsernameEnc` | `nvarchar(500)?` | رمزنگاری‌شده |
| `PasswordEnc` | `nvarchar(500)?` | رمزنگاری‌شده |
| `SenderNumber` | `nvarchar(30)?` | |
| `BaseUrl` | `nvarchar(500)?` | |
| `SettingsJson` | `nvarchar(max)?` | تنظیمات اختصاصی هر Provider |
| `RatePerMinute` | `int` | default 3000 |
| `IsDefault` | `bit` | |
| `IsActive` | `bit` | |

**ایندکس:** `UX_SmsAccounts_Client_Purpose_Default` روی `(ClientId, Purpose)` با فیلتر `IsDefault = 1`.

#### MessageTemplate

| ستون | نوع | قید |
|---|---|---|
| `Id` | `int` | PK |
| `ClientId` | `int` | FK → Client |
| `ReportId` | `int?` | NULL = سراسری (برای OTP) |
| `TemplateType` | `tinyint` | |
| `Title` | `nvarchar(200)` | |
| `Body` | `nvarchar(1000)` | با placeholder |
| `PatternCode` | `nvarchar(100)?` | کد الگو در پنل |
| `IsActive` | `bit` | |
| `CreatedAt`, `UpdatedAt` | `datetime2(3)` | |

**ایندکس:** `UX_Templates` روی `(ClientId, ReportId, TemplateType)` با فیلتر `IsActive = 1`.

**Placeholder های مجاز:**

| Placeholder | توضیح |
|---|---|
| `{shortUrl}` | لینک کامل |
| `{code}` | فقط کد ۶ کاراکتری |
| `{reportName}` | نام چاپ |
| `{shop}` `{shod}` `{radif}` | شماره‌های پرونده |
| `{expireDate}` | تاریخ انقضا (شمسی) |
| `{otp}` | فقط در قالب نوع `Otp` |
| `{otpMinutes}` | عمر کد به دقیقه |

#### RetentionPolicy

| ستون | نوع | قید |
|---|---|---|
| `Id` | `int` | PK |
| `ClientId` | `int?` | NULL = سراسری |
| `ReportId` | `int?` | NULL = همه انواع |
| `BatchTag` | `nvarchar(64)?` | برای بارگذاری انبوه |
| `Title` | `nvarchar(200)` | |
| `RetentionDays` | `int` | default 90 |
| `GraceDays` | `int` | default 7 |
| `Priority` | `int` | عدد بزرگ‌تر = اولویت بالاتر |
| `IsActive` | `bit` | |
| `CreatedAt` | `datetime2(3)` | |

#### StoredFile

| ستون | نوع | قید |
|---|---|---|
| `Id` | `bigint` | PK, Identity |
| `FileGuid` | `uniqueidentifier` | UNIQUE, default `NEWID()` |
| `ClientId` | `int` | FK → Client |
| `RetentionPolicyId` | `int?` | FK → RetentionPolicy |
| `StorageKey` | `nvarchar(400)` | مسیر نسبی |
| `OriginalFileName` | `nvarchar(300)` | |
| `ContentType` | `nvarchar(100)` | |
| `Extension` | `nvarchar(10)` | |
| `SizeBytes` | `bigint` | |
| `Sha256` | `char(64)` | صحت‌سنجی |
| `Status` | `tinyint` | `FileStatus` |
| `StoredAt` | `datetime2(3)` | **تاریخ ذخیره** |
| `FileExpiresAt` | `datetime2(3)` | `StoredAt + RetentionDays` |
| `PendingDeleteAt` | `datetime2(3)?` | |
| `DeletedAt` | `datetime2(3)?` | |
| `DeletedBy` | `nvarchar(100)?` | |

**ایندکس‌ها:**

```sql
CREATE INDEX IX_StoredFiles_Retention
    ON StoredFiles(Status, FileExpiresAt) INCLUDE (StorageKey, SizeBytes);
CREATE INDEX IX_StoredFiles_Client_StoredAt ON StoredFiles(ClientId, StoredAt);
```

#### ShortLink

| ستون | نوع | قید |
|---|---|---|
| `Id` | `bigint` | PK |
| `Code` | `char(6)` | UNIQUE |
| `RequestId` | `uniqueidentifier` | UNIQUE |
| `ClientRequestId` | `nvarchar(64)?` | |
| `StoredFileId` | `bigint` | FK → StoredFile |
| `ClientId` | `int` | FK → Client |
| `ApiKeyId` | `int?` | FK → ApiKey |
| `Shop` | `nvarchar(50)` | شماره پرونده |
| `Shod` | `nvarchar(50)` | شماره درخواست |
| `Radif` | `nvarchar(50)` | شماره ردیف |
| `ReportId` | `int` | کد چاپ |
| `ReportName` | `nvarchar(300)` | اسم چاپ |
| `PhoneNumber` | `nvarchar(15)` | |
| `BatchTag` | `nvarchar(64)?` | |
| `ExpiresAt` | `datetime2(3)` | هم‌راستا با `FileExpiresAt` |
| `DownloadCount` | `int` | default 0 |
| `OtpRequestCount` | `int` | default 0 |
| `LastOtpRequestedAt` | `datetime2(3)?` | |
| `FailedVerifyCount` | `int` | default 0 |
| `LockedUntil` | `datetime2(3)?` | |
| `FirstVerifiedAt` | `datetime2(3)?` | |
| `IsActive` | `bit` | default 1 |
| `CreatedAt` | `datetime2(3)` | |

**ایندکس‌ها:**

```sql
CREATE UNIQUE INDEX UX_ShortLinks_Code ON ShortLinks(Code);
CREATE UNIQUE INDEX UX_ShortLinks_RequestId ON ShortLinks(RequestId);
CREATE UNIQUE INDEX UX_ShortLinks_Business
    ON ShortLinks(ClientId, Shop, Shod, Radif, ReportId) WHERE IsActive = 1;
CREATE INDEX IX_ShortLinks_ClientRequestId ON ShortLinks(ClientId, ClientRequestId);
CREATE INDEX IX_ShortLinks_BatchTag ON ShortLinks(BatchTag) WHERE BatchTag IS NOT NULL;
CREATE INDEX IX_ShortLinks_Expiry ON ShortLinks(IsActive, ExpiresAt);
```

#### SmsMessage

| ستون | نوع | قید |
|---|---|---|
| `Id` | `bigint` | PK |
| `ShortLinkId` | `bigint?` | FK → ShortLink |
| `SmsAccountId` | `int` | FK → SmsAccount |
| `TemplateId` | `int?` | FK → MessageTemplate |
| `MessageType` | `tinyint` | `SmsMessageType` |
| `PhoneNumber` | `nvarchar(15)` | |
| `Body` | `nvarchar(1000)` | متن رندرشده |
| `Status` | `tinyint` | `SmsStatus` |
| `ProviderMessageId` | `nvarchar(100)?` | |
| `TryCount` | `int` | |
| `NextRetryAt` | `datetime2(3)?` | |
| `LastError` | `nvarchar(1000)?` | |
| `Cost` | `decimal(18,2)?` | |
| `CreatedAt`, `SentAt`, `DeliveredAt`, `StatusCheckedAt` | `datetime2(3)` | |

**ایندکس‌ها:**

```sql
CREATE INDEX IX_SmsMessages_Status_Created ON SmsMessages(Status, CreatedAt);
CREATE INDEX IX_SmsMessages_ShortLink ON SmsMessages(ShortLinkId);
CREATE INDEX IX_SmsMessages_ProviderMsgId ON SmsMessages(ProviderMessageId)
    WHERE ProviderMessageId IS NOT NULL;
```

#### SmsStatusHistory

`Id (bigint PK)`, `SmsMessageId (FK)`, `Status (tinyint)`, `ProviderStatusCode (nvarchar 50)`, `Description (nvarchar 500)`, `CreatedAt`.

#### OutboxMessage

| ستون | نوع |
|---|---|
| `Id` | `uniqueidentifier` PK (GUID v7) |
| `Type` | `nvarchar(200)` |
| `PayloadJson` | `nvarchar(max)` |
| `Status` | `tinyint` (`OutboxStatus`) |
| `TryCount` | `int` |
| `CreatedAt`, `ProcessedAt` | `datetime2(3)` |
| `Error` | `nvarchar(1000)?` |

**ایندکس:** `IX_Outbox_Pending ON OutboxMessages(Status, CreatedAt) WHERE Status = 0`

#### LinkAccessLog

`Id (bigint)`, `ShortLinkId (FK)`, `AccessType (tinyint)`, `IpAddress (nvarchar 45)`, `UserAgent (nvarchar 500)`, `IsSuccess (bit)`, `ErrorCode (nvarchar 50)`, `CreatedAt`.

#### FileDeletionLog

`Id (bigint)`, `StoredFileId (bigint)`, `StorageKey (nvarchar 400)`, `SizeBytes (bigint)`, `Reason (tinyint)`, `TriggeredBy (nvarchar 100)`, `PhysicalDeleteOk (bit)`, `ErrorMessage (nvarchar 1000)`, `CreatedAt`.

#### AuditLog

`Id (bigint)`, `UserId (nvarchar 450)`, `EntityName`, `EntityId`, `Action`, `OldValueJson`, `NewValueJson`, `IpAddress`, `CreatedAt`.

### گام M1.3 — DbContext

`src/Shortener.Infrastructure/Persistence/AppDbContext.cs`

- از `IdentityDbContext<AppUser>` ارث‌بری کن (کاربران پنل).
- تمام `IEntityTypeConfiguration<T>` ها را در پوشه `Persistence/Configurations/` بنویس و با `ApplyConfigurationsFromAssembly` ثبت کن.
- `SaveChangesAsync` را override کن تا `CreatedAt`/`UpdatedAt` را خودکار پر کند.
- تمام `DateTime` ها **UTC** ذخیره شوند. تبدیل به وقت محلی و شمسی فقط در لایه نمایش.

### گام M1.4 — Migration اولیه

```bash
dotnet ef migrations add InitialCreate \
  -p src/Shortener.Infrastructure -s src/Shortener.Api -o Persistence/Migrations
dotnet ef database update -p src/Shortener.Infrastructure -s src/Shortener.Api
```

### گام M1.5 — Seed

`src/Shortener.Infrastructure/Persistence/DbSeeder.cs`:

1. نقش‌ها: `SuperAdmin`, `Operator`, `Viewer`
2. کاربر `admin` با رمز اولیه (اجبار تغییر در اولین ورود)
3. `SmsProviders`: حداقل `kavenegar`, `melipayamak`, `farazsms`
4. `RetentionPolicy` سراسری: `RetentionDays = 90`, `GraceDays = 7`, `Priority = 0`

### گام M1.6 — لایه ذخیره‌سازی فایل

`src/Shortener.Application/Abstractions/IFileStorage.cs`:

```csharp
public interface IFileStorage
{
    Task<StoredFileResult> SaveAsync(
        Stream source, string extension, CancellationToken ct);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);

    Task<bool> DeleteAsync(string storageKey, CancellationToken ct);

    Task<bool> ExistsAsync(string storageKey, CancellationToken ct);

    Task<int> CleanupTempFilesAsync(TimeSpan maxAge, CancellationToken ct);

    DiskSpaceInfo GetDiskSpace();
}

public sealed record StoredFileResult(
    Guid FileGuid, string StorageKey, long SizeBytes, string Sha256);

public sealed record DiskSpaceInfo(
    long TotalBytes, long FreeBytes, double UsedPercent);
```

**`FileSystemStorage` — الگوریتم `SaveAsync`:**

1. `fileGuid = Guid.CreateVersion7()`
2. `shard = SHA256(fileGuid).Substring(0,2)` (hex → ۲۵۶ حالت)
3. مسیر: `{root}/{utcNow:yyyy}/{MM}/{dd}/{shard}/`
4. `Directory.CreateDirectory` (idempotent)
5. نوشتن در `{guid}.tmp` با `FileStream(bufferSize: 81920, useAsync: true)`
6. همزمان `IncrementalHash.CreateHash(SHA256)` روی همان بافر — **بدون خواندن دوباره فایل**
7. اگر حجم از `MaxFileSizeBytes` گذشت → توقف، حذف `.tmp`، پرتاب `FileTooLargeException`
8. `File.Move(tmp, final)` اتمیک
9. بازگرداندن `StoredFileResult`

**در صورت خطای بعدی در ثبت DB:** فراخوانی `DeleteAsync` در `catch` تا فایل یتیم نماند.

### گام M1.7 — سرویس نگهداشت

`IRetentionResolver.ResolveAsync(clientId, reportId, batchTag)` — انتخاب سیاست با ترتیب اولویت:

1. تطابق `BatchTag` (دقیق)
2. تطابق `ClientId + ReportId`
3. تطابق `ClientId` (با `ReportId = NULL`)
4. سیاست سراسری (`ClientId = NULL`)

در صورت تساوی، `Priority` بالاتر برنده است. اگر هیچ سیاستی نبود → خطا (سیاست سراسری همیشه در Seed هست، پس نباید رخ دهد).

### ✅ DoD مایلستون M1

- [ ] `dotnet ef database update` جدول‌ها را می‌سازد
- [ ] تمام ایندکس‌های ذکرشده در دیتابیس موجودند
- [ ] Seed اجرا شده و کاربر ادمین قابل ورود است
- [ ] تست واحد `FileSystemStorage`: ذخیره، خواندن، حذف، رد فایل >3MB
- [ ] تست: فایل `.tmp` پس از خطا باقی نمی‌ماند
- [ ] تست واحد `RetentionResolver` برای هر چهار سطح اولویت

---

## M2 — پنل مدیریت

**حجم:** بزرگ · **وابستگی:** M1 · **قابل موازی‌سازی با M3/M4**

### گام M2.1 — Identity و نقش‌ها

- `AppUser : IdentityUser` با فیلدهای `FullName`, `IsActive`, `MustChangePassword`, `LastLoginAt`.
- سیاست رمز: حداقل ۱۰ کاراکتر، حروف بزرگ و کوچک و رقم.
- Lockout: ۵ تلاش ناموفق → قفل ۱۵ دقیقه.
- نقش‌ها و دسترسی‌ها:

| نقش | دسترسی |
|---|---|
| `SuperAdmin` | همه چیز، از جمله سیاست نگهداشت و مدیریت کاربران |
| `Operator` | کلاینت، API Key، حساب پیامکی، قالب‌ها، گزارشات |
| `Viewer` | فقط گزارشات (خواندنی) |

**قانون:** تغییر `RetentionPolicy` فقط برای `SuperAdmin`.

### گام M2.2 — مدیریت کلاینت

`ClientsController`: List / Create / Edit / Toggle Active.
حذف کلاینت ممنوع (فقط غیرفعال‌سازی) چون رکوردهای تاریخی به آن ارجاع دارند.

### گام M2.3 — مدیریت API Key

`ApiKeysController`:

**تولید کلید:**

```csharp
// 32 بایت تصادفی → Base64Url → 43 کاراکتر
var bytes = RandomNumberGenerator.GetBytes(32);
var rawKey = "sk_" + Base64UrlEncode(bytes);
var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLower();
var prefix = rawKey[..12];
```

**قوانین اجباری:**
- فقط `KeyHash` و `KeyPrefix` در DB ذخیره شوند. `rawKey` هرگز ذخیره نشود.
- `rawKey` **فقط یک بار** پس از ساخت در صفحه نمایش داده شود، با هشدار «این مقدار دیگر قابل بازیابی نیست».
- ابطال کلید: `IsActive = 0` + `RevokedAt` + **حذف کلید کش از Redis** (`apikey:{hash}`).

### گام M2.4 — مدیریت حساب پیامکی

`SmsAccountsController`:

- فرم داینامیک: با انتخاب `SmsProvider`، فیلدهای مورد نیاز آن Provider نمایش داده شوند (از `ISmsProvider.GetRequiredFields()`).
- **هر کلاینت باید دو حساب داشته باشد:** یکی با `Purpose = Bulk` و یکی با `Purpose = Otp`. اگر یکی از این دو نبود، در صفحه کلاینت هشدار قرمز نمایش داده شود.
- فیلدهای حساس با `IDataProtector` رمزنگاری شوند (Purpose string: `"SmsAccountCredentials"`).
- در فرم ویرایش، مقادیر رمزنگاری‌شده **ماسک‌شده** نمایش داده شوند (`••••••`). خالی گذاشتن = بدون تغییر.
- دکمه **«تست ارسال»**: شماره تست بگیرد و یک پیامک آزمایشی بفرستد، نتیجه را نمایش دهد.
- دکمه **«بررسی اعتبار»**: فراخوانی `GetCreditAsync`.

### گام M2.5 — مدیریت قالب متن

`MessageTemplatesController`:

- ایجاد قالب به‌ازای هر `ReportId` با `TemplateType = DownloadLink`.
- ایجاد قالب `TemplateType = Otp` با `ReportId = NULL` (سراسری برای کلاینت).
- **پیش‌نمایش زنده:** placeholder ها با داده نمونه جایگزین و متن نهایی + تعداد کاراکتر و تعداد پارت پیامک نمایش داده شود.
- **اعتبارسنجی:** قالب `Otp` حتماً باید `{otp}` داشته باشد؛ قالب `DownloadLink` حتماً `{shortUrl}` یا `{code}`.
- هشدار اگر متن فارسی از ۷۰ کاراکتر (یک پارت UCS-2) بیشتر شد.

### گام M2.6 — مدیریت سیاست نگهداشت (فقط SuperAdmin)

`RetentionPoliciesController`:

- CRUD سیاست‌ها با فیلدهای `ClientId`, `ReportId`, `BatchTag`, `RetentionDays`, `GraceDays`, `Priority`.
- **صفحه پیش‌نمایش اثر:** قبل از ذخیره، نمایش دهد این سیاست روی چند فایل موجود اثر می‌گذارد و چند فایل بلافاصله منقضی خواهند شد.
- سیاست سراسری (`ClientId = NULL, Priority = 0`) قابل حذف نباشد.

### گام M2.7 — AuditLog

`SaveChangesInterceptor` روی `AppDbContext` که برای Entity های حساس (`Client`, `ApiKey`, `SmsAccount`, `MessageTemplate`, `RetentionPolicy`) رکورد `AuditLog` بسازد.
مقادیر رمزنگاری‌شده در `OldValueJson`/`NewValueJson` **ماسک** شوند.

### ✅ DoD مایلستون M2

- [ ] ورود با کاربر ادمین و اجبار تغییر رمز اولیه
- [ ] ساخت کلاینت → ساخت API Key → کلید یک بار نمایش داده می‌شود و بار دوم قابل مشاهده نیست
- [ ] ساخت دو حساب پیامکی (Bulk و Otp) و تست ارسال موفق
- [ ] ساخت قالب لینک و قالب OTP با پیش‌نمایش صحیح
- [ ] هر تغییر در حساب پیامکی یک رکورد `AuditLog` با مقادیر ماسک‌شده ثبت می‌کند
- [ ] `Viewer` نمی‌تواند وارد صفحات ویرایش شود (تست دسترسی)

---

## M3 — API بارگذاری

**حجم:** متوسط · **وابستگی:** M1

### گام M3.1 — احراز هویت API Key

`src/Shortener.Api/Authentication/ApiKeyAuthenticationHandler.cs`

```
هدر: X-Api-Key: sk_xxxxx
```

**الگوریتم:**

1. خواندن هدر؛ نبود → `401` با کد `ERR_MISSING_API_KEY`
2. `hash = SHA256(key)`
3. جستجو در Redis: `apikey:{hash}` (TTL 5 دقیقه)
4. Cache Miss → جستجو در DB با `Include(Client)` → ذخیره در Redis
5. اعتبارسنجی: `IsActive`, `RevokedAt == null`, `ExpiresAt`, `Client.IsActive`
6. ساخت `ClaimsPrincipal` با `ClientId`, `ApiKeyId`
7. به‌روزرسانی `LastUsedAt` **غیرهمزمان** (از طریق `Channel`، نه در مسیر درخواست)

**Rate Limiting:** با `AddRateLimiter` داخلی .NET، پارتیشن بر اساس `ClientId`، پنجره ثابت ۶۰ ثانیه، سقف پیکربندی‌پذیر (پیش‌فرض ۶۰۰۰ در دقیقه برای پوشش روز پیک).

### گام M3.2 — Endpoint بارگذاری

```
POST /api/v1/links
Content-Type: multipart/form-data
X-Api-Key: {key}
X-Correlation-Id: {optional}
```

**ترتیب Part ها اجباری است:** `metadata` باید **قبل از** `file` بیاید.

```jsonc
// part "metadata" — application/json
{
  "shop": "12345",
  "shod": "678",
  "radif": "9",
  "reportId": 12,
  "reportName": "سند مالکیت",
  "phoneNumber": "09121234567",
  "clientRequestId": "REQ-8891",
  "sendSmsImmediately": true,
  "batchTag": "BATCH-1405-06"
}
```

**پیاده‌سازی (اجباری — بدون Buffer کامل در حافظه):**

```csharp
[DisableFormValueModelBinding]
[RequestSizeLimit(4_194_304)]   // 4MB با حاشیه
```

از `MultipartReader` مستقیم استفاده کن، نه `IFormFile`:

```csharp
var boundary = MultipartRequestHelper.GetBoundary(
    MediaTypeHeaderValue.Parse(Request.ContentType), 128);
var reader = new MultipartReader(boundary, Request.Body);
```

**الگوریتم کامل:**

1. خواندن Section اول → باید `metadata` باشد → deserialize + validate (FluentValidation)
2. اگر `metadata` نبود → `400 ERR_METADATA_FIRST`
3. **بررسی Idempotency:** جستجوی `(ClientId, Shop, Shod, Radif, ReportId)` با `IsActive=1` → اگر بود، بدون خواندن فایل پاسخ موجود با `isDuplicate: true` برگردان
4. **بررسی فضای دیسک:** اگر اشغال > `DiskRejectThresholdPercent` → `507 ERR_DISK_FULL`
5. خواندن Section دوم (`file`):
   - خواندن ۱۲ بایت اول در بافر → **بررسی Magic Number** → عدم تطابق → `415 ERR_INVALID_FILE_TYPE`
   - استریم به `IFileStorage.SaveAsync` (بافر اول را هم بنویس)
   - عبور از ۳MB → `413 ERR_FILE_TOO_LARGE`
6. `IRetentionResolver` → محاسبه `FileExpiresAt = StoredAt + RetentionDays`
7. تولید `Code` شش‌کاراکتری (گام M3.3)
8. **در یک تراکنش SQL:**
   - `INSERT StoredFile`
   - `INSERT ShortLink` (با `RequestId = Guid.CreateVersion7()`)
   - `INSERT OutboxMessage` **فقط اگر** `sendSmsImmediately == true`
9. Commit؛ در صورت خطا → `IFileStorage.DeleteAsync` + پرتاب خطا
10. نوشتن کش Redis: `link:{code}`
11. پاسخ `201`

**پاسخ:**

```jsonc
{
  "requestId": "0192f4c1-...",
  "clientRequestId": "REQ-8891",
  "code": "Ab3xY9",
  "shortUrl": "https://links.example.ir/s/Ab3xY9",
  "fileExpiresAt": "2026-11-01T00:00:00Z",
  "smsStatus": "Queued",
  "isDuplicate": false
}
```

هدر `X-Request-Id` هم با همان `requestId` برگردانده شود (حتی در پاسخ‌های خطا).

### گام M3.3 — تولید کد کوتاه

`src/Shortener.Application/Services/ShortCodeGenerator.cs`

```csharp
private const string Alphabet =
    "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
// حذف شده: 0 O o 1 l I  — برای جلوگیری از اشتباه خواندن
```

**الگوریتم:**

1. تولید ۶ کاراکتر با `RandomNumberGenerator.GetInt32(0, Alphabet.Length)`
2. `SET link:reserve:{code} 1 NX EX 30` در Redis — اگر ناموفق، تکرار
3. درج در DB؛ در صورت خطای Unique Index، تکرار
4. حداکثر `MaxGenerationRetries` تلاش، سپس `ERR_CODE_GENERATION_FAILED`

**نکته:** با ۵۶ کاراکتر و طول ۶، فضا ≈ ۳۰٫۸ میلیارد. با ۱۰۰ هزار رکورد، احتمال برخورد ناچیز است.

### گام M3.4 — Endpoint فعال‌سازی ارسال پیامک

برای سناریوی پیک (بارگذاری و ارسال تفکیک‌شده):

```
POST /api/v1/links/dispatch-sms
{ "batchTag": "BATCH-1405-06", "ratePerMinute": 3000 }

POST /api/v1/links/{requestId}/dispatch-sms
```

**رفتار:** برای همه `ShortLink` هایی که `SmsMessage` با `Status = Pending` دارند یا اصلاً `SmsMessage` ندارند، `OutboxMessage` بساز. عملیات باید **Idempotent** باشد — فراخوانی دوباره نباید پیامک تکراری بسازد.

پاسخ: `{ "queuedCount": 98432, "skippedCount": 1568 }`

### گام M3.5 — Endpoint پیگیری

```
GET /api/v1/links/{requestId}
GET /api/v1/links/by-client-request/{clientRequestId}
```

پاسخ شامل: وضعیت لینک، وضعیت پیامک لینک، تعداد OTP درخواستی، تعداد دانلود، `fileExpiresAt`، `fileStatus`.

### گام M3.6 — مستندسازی

Scalar یا Swagger روی `/scalar/v1`. شامل نمونه `curl` برای فراخوانی multipart:

```bash
curl -X POST https://api.example.ir/api/v1/links \
  -H "X-Api-Key: sk_xxx" \
  -F 'metadata={"shop":"123","shod":"45","radif":"6","reportId":12,"reportName":"سند","phoneNumber":"09121234567"};type=application/json' \
  -F "file=@doc.pdf"
```

### ✅ DoD مایلستون M3

- [ ] بارگذاری فایل PDF ۲MB → دریافت `shortUrl` معتبر
- [ ] بارگذاری فایل ۴MB → `413`
- [ ] بارگذاری فایل `.exe` با پسوند `.pdf` → `415` (Magic Number)
- [ ] ارسال دوباره همان `(shop, shod, radif, reportId)` → همان لینک با `isDuplicate: true`
- [ ] API Key نامعتبر → `401`
- [ ] تست: خطای عمدی در DB → فایل روی دیسک باقی نمی‌ماند
- [ ] تست: ۱۰۰ آپلود همزمان بدون خطا و بدون رشد غیرعادی حافظه
- [ ] `sendSmsImmediately: false` → هیچ `OutboxMessage` ساخته نمی‌شود
- [ ] `dispatch-sms` دو بار فراخوانی شود → پیامک تکراری ساخته نمی‌شود

---

## M4 — دانلود با OTP

**حجم:** بزرگ · **وابستگی:** M1، M3 · از `FakeSmsProvider` استفاده کن تا موازی با M5 پیش برود

### جریان کامل

```
GET  /s/{code}              → صفحه شروع
POST /s/{code}/otp/request  → صدور و ارسال کد
POST /s/{code}/otp/verify   → تأیید کد → توکن دانلود
GET  /d/{token}             → استریم فایل
```

### گام M4.1 — کش لینک

`ILinkCache` با الگوی Cache-Aside:

- کلید: `link:{code}` — مقدار: JSON شامل `ShortLinkId`, `StoredFileId`, `ReportName`, `PhoneNumber`, `ExpiresAt`, `IsActive`, `FileStatus`, `StorageKey`, `ContentType`, `Extension`, `ClientId`
- TTL: ۶۰ دقیقه
- Cache Miss → خواندن از SQL → نوشتن در Redis
- **Fallback:** اگر Redis در دسترس نبود، مستقیم از SQL بخوان و خطا را فقط لاگ کن (سرویس نباید بیفتد)
- **ابطال:** هنگام غیرفعال‌سازی لینک یا حذف فایل، `DEL link:{code}`

### گام M4.2 — صفحه شروع `GET /s/{code}`

**بررسی‌ها به ترتیب:**

| شرط | نتیجه |
|---|---|
| کد یافت نشد | `404` صفحه «لینک نامعتبر» |
| `IsActive == false` | صفحه «لینک غیرفعال شده» |
| `ExpiresAt < now` | صفحه «مهلت دانلود به پایان رسیده» |
| `FileStatus == Deleted` | صفحه «فایل به دلیل اتمام مهلت نگهداری حذف شده است» |
| `LockedUntil > now` | صفحه «قفل موقت» با شمارش معکوس |
| سالم | صفحه شروع |

**محتوای صفحه شروع:**
- نام فایل از `ReportName`
- شماره ماسک‌شده: **فقط پیش‌شماره** → `0912*******` (هرگز ۴ رقم آخر را نمایش نده)
- دکمه «ارسال کد تأیید»
- طراحی RTL، ریسپانسیو، بدون وابستگی به JS برای عملکرد پایه

ثبت `LinkAccessLog` با `AccessType = View`.

### گام M4.3 — درخواست OTP

```
POST /s/{code}/otp/request
```

**الگوریتم:**

1. بررسی‌های گام M4.2 دوباره انجام شود (نباید فقط به صفحه قبل تکیه کرد)
2. **Cooldown:** `EXISTS otp:cd:{code}` → اگر بود، `429 ERR_OTP_COOLDOWN` با زمان باقی‌مانده
3. **سقف به‌ازای لینک:** `INCR otp:cnt:{code}` + `EXPIRE 3600` → اگر > ۳ → `429 ERR_OTP_LIMIT_LINK`
4. **سقف به‌ازای IP:** `INCR otp:ip:{ipHash}` + `EXPIRE 3600` → اگر > ۱۰ → `429 ERR_OTP_LIMIT_IP`
5. تولید کد: `RandomNumberGenerator.GetInt32(100000, 1000000)` → شش رقم
6. ذخیره در Redis: `SET otp:{code} {HMAC-SHA256(otp, serverSecret)} EX 120`
   **هرگز کد خام را در Redis ذخیره نکن.**
7. `SET otp:cd:{code} 1 EX 90` (Cooldown)
8. `DEL otp:fail:{code}` (ریست شمارنده تلاش)
9. یافتن `SmsAccount` با `Purpose = Otp` برای کلاینت + قالب `TemplateType = Otp`
10. اگر حساب OTP یا قالب نبود → `500 ERR_OTP_NOT_CONFIGURED` + لاگ هشدار (خطای پیکربندی است)
11. رندر متن، درج `SmsMessage` با `MessageType = Otp, Status = Queued`
12. **انتشار مستقیم روی `sms:otp`** (نه از مسیر Outbox — تأخیر Outbox برای OTP قابل قبول نیست)
13. `UPDATE ShortLinks SET OtpRequestCount += 1, LastOtpRequestedAt = now`
14. ثبت `LinkAccessLog` با `AccessType = OtpRequested`
15. پاسخ: `{ "cooldownSeconds": 90, "ttlSeconds": 120 }`

### گام M4.4 — تأیید OTP

```
POST /s/{code}/otp/verify
{ "otp": "123456" }
```

**الگوریتم:**

1. بررسی `LockedUntil`
2. `GET otp:{code}` → اگر نبود → `400 ERR_OTP_EXPIRED`
3. مقایسه **Constant-Time**: `CryptographicOperations.FixedTimeEquals`
4. **در صورت خطا:**
   - `INCR otp:fail:{code}`
   - اگر ≥ ۵ → `DEL otp:{code}` + `UPDATE ShortLinks SET LockedUntil = now+15m, FailedVerifyCount += 5` → `429 ERR_TOO_MANY_ATTEMPTS`
   - در غیر این صورت → `400 ERR_OTP_INVALID` با تعداد تلاش باقی‌مانده
   - ثبت `LinkAccessLog` با `VerifyFailed`
5. **در صورت موفقیت:**
   - `DEL otp:{code}`, `DEL otp:fail:{code}`
   - تولید توکن: `Base64Url(RandomNumberGenerator.GetBytes(32))`
   - `SET dl:{token} {json} EX 300` که json شامل: `ShortLinkId`, `IpHash`, `UserAgentHash`
   - `UPDATE ShortLinks SET FirstVerifiedAt = COALESCE(FirstVerifiedAt, now), FailedVerifyCount = 0`
   - ثبت `LinkAccessLog` با `VerifySuccess`
   - پاسخ: `{ "downloadUrl": "/d/{token}" }`

### گام M4.5 — دانلود

```
GET /d/{token}
```

**الگوریتم:**

1. `GETDEL dl:{token}` — **ابطال آنی، یک‌بارمصرف**
2. اگر نبود → `410 ERR_TOKEN_USED_OR_EXPIRED` (صفحه «لینک دانلود مصرف شده، دوباره تأیید کنید»)
3. تطبیق `IpHash` و `UserAgentHash` — عدم تطابق → `403 ERR_TOKEN_MISMATCH`
4. بارگذاری لینک از کش → بررسی مجدد `IsActive`, `ExpiresAt`, `FileStatus`
5. `IFileStorage.ExistsAsync` → اگر نبود → `410` + لاگ `ERROR` (ناسازگاری دیسک و DB)
6. استریم:

```csharp
var fileName = SanitizeFileName($"{link.ReportName}{file.Extension}");
Response.Headers["X-Content-Type-Options"] = "nosniff";
Response.Headers["Cache-Control"] = "no-store";
return PhysicalFile(fullPath, file.ContentType, fileName, enableRangeProcessing: true);
```

**`SanitizeFileName`:** حذف کاراکترهای `\ / : * ? " < > |` و کنترلی، محدودیت ۱۵۰ کاراکتر، حفظ حروف فارسی. نام فایل با `Content-Disposition` به‌صورت `filename*=UTF-8''...` انکد شود تا فارسی درست نمایش داده شود.

7. `UPDATE ShortLinks SET DownloadCount += 1` (غیرهمزمان)
8. ثبت `LinkAccessLog` با `AccessType = Download`

### گام M4.6 — رابط کاربری ورود کد

- شش خانه مجزا، `inputmode="numeric"`, `pattern="[0-9]*"`, `autocomplete="one-time-code"`
- جابه‌جایی خودکار فوکوس، پشتیبانی از Paste کامل کد
- تایمر شمارش معکوس ۱۲۰ ثانیه
- دکمه «ارسال مجدد» غیرفعال تا پایان ۹۰ ثانیه Cooldown
- نمایش تعداد تلاش باقی‌مانده پس از خطا
- کار بدون JS هم باید ممکن باشد (fallback به یک input تکی)

### گام M4.7 — لاگ غیرهمزمان

`LinkAccessLog` نباید در مسیر درخواست نوشته شود:

```csharp
Channel.CreateBounded<LinkAccessLog>(new BoundedChannelOptions(10_000) {
    FullMode = BoundedChannelFullMode.DropWrite   // در پیک، لاگ فدای سرویس شود
});
```

`BackgroundService` مصرف‌کننده که هر ۲ ثانیه یا هر ۵۰۰ رکورد، Bulk Insert می‌کند.

### ✅ DoD مایلستون M4

- [ ] مسیر کامل: لینک → درخواست OTP → کد در لاگ `FakeSmsProvider` → تأیید → دانلود موفق
- [ ] کد اشتباه ۵ بار → قفل ۱۵ دقیقه‌ای
- [ ] کد پس از ۱۲۰ ثانیه منقضی می‌شود
- [ ] درخواست چهارم OTP در یک ساعت → `429`
- [ ] درخواست دوم قبل از ۹۰ ثانیه → `429` با زمان باقی‌مانده
- [ ] توکن دانلود بار دوم → `410`
- [ ] توکن با IP متفاوت → `403`
- [ ] نام فایل فارسی در مرورگر درست دانلود می‌شود
- [ ] لینک منقضی/حذف‌شده → صفحه خطای مناسب، نه `500`
- [ ] با Redis خاموش، `GET /s/{code}` همچنان کار می‌کند (Fallback به SQL)

---

## M5 — صف پیامک

**حجم:** بزرگ · **وابستگی:** M1

> **این مهم‌ترین بخش پروژه است.** جداسازی صف OTP از صف انبوه غیرقابل مذاکره است.

### گام M5.1 — انتزاع Provider

`src/Shortener.Application/Abstractions/ISmsProvider.cs`

```csharp
public interface ISmsProvider
{
    string ProviderCode { get; }

    IReadOnlyList<ProviderField> GetRequiredFields();

    Task<SmsSendResult> SendAsync(
        SmsAccountConfig config, SmsSendRequest request, CancellationToken ct);

    Task<IReadOnlyList<SmsDeliveryStatus>> GetStatusAsync(
        SmsAccountConfig config, IReadOnlyList<string> providerMessageIds,
        CancellationToken ct);

    Task<decimal?> GetCreditAsync(SmsAccountConfig config, CancellationToken ct);
}

public sealed record SmsSendResult(
    bool IsSuccess,
    string? ProviderMessageId,
    string? ProviderStatusCode,
    string? ErrorMessage,
    bool IsRetryable,
    decimal? Cost);

public sealed record SmsSendRequest(
    string PhoneNumber,
    string Body,
    string? PatternCode,
    IReadOnlyDictionary<string, string>? PatternTokens,
    SmsMessageType MessageType);
```

**ثبت با Keyed DI:**

```csharp
services.AddKeyedScoped<ISmsProvider, KavenegarProvider>("kavenegar");
services.AddKeyedScoped<ISmsProvider, MeliPayamakProvider>("melipayamak");
services.AddKeyedScoped<ISmsProvider, FakeSmsProvider>("fake");
```

`ISmsProviderFactory.Resolve(string providerCode)` با `IServiceProvider.GetRequiredKeyedService`.

**قوانین پیاده‌سازی هر Provider:**
- از `IHttpClientFactory` با نام اختصاصی استفاده کن.
- Timeout: ۱۰ ثانیه برای OTP، ۳۰ ثانیه برای انبوه.
- Polly: Retry سه‌باره فقط برای خطاهای شبکه و `5xx`. خطای `4xx` معنایی → `IsRetryable = false`.
- کدهای وضعیت Provider را به `SmsStatus` داخلی نگاشت کن؛ جدول نگاشت را در همان کلاس Provider نگه‌دار.
- **هرگز اعتبارنامه را لاگ نکن.**

**`FakeSmsProvider`:** کد OTP و متن پیام را با سطح `Information` لاگ می‌کند و همیشه موفق برمی‌گردد. فقط در محیط Development فعال باشد.

### گام M5.2 — Outbox Publisher

`OutboxPublisherService : BackgroundService` در `Shortener.Worker`:

هر ۲ ثانیه:

```sql
UPDATE TOP (500) OutboxMessages WITH (READPAST, UPDLOCK)
SET Status = 1, ProcessedAt = SYSUTCDATETIME()
OUTPUT inserted.Id, inserted.Type, inserted.PayloadJson
WHERE Status = 0;
```

`READPAST` اجازه می‌دهد چند نمونه Worker بدون قفل‌شدن کار کنند.

سپس `XADD` روی `sms:bulk`.
در صورت خطای Redis → بازگرداندن `Status = 0` و `TryCount += 1`.

**Job بازیابی:** هر ۵ دقیقه، رکوردهایی با `Status = 1` که بیش از ۱۰ دقیقه پیش `ProcessedAt` خورده‌اند ولی `SmsMessage` متناظرشان هنوز `Queued` است را دوباره منتشر کن.

### گام M5.3 — Worker های مصرف‌کننده

**دو کلاس کاملاً مجزا:**

| | `BulkSmsWorker` | `OtpSmsWorker` |
|---|---|---|
| Stream | `sms:bulk` | `sms:otp` |
| Consumer Group | `bulk-workers` | `otp-workers` |
| اندازه Batch | ۱۰۰ | ۱ |
| Rate Limit | `SmsAccount.RatePerMinute` | بدون محدودیت |
| Timeout | ۳۰ ثانیه | ۱۰ ثانیه |
| هدف تأخیر | < ۳۰ دقیقه | **< ۱۰ ثانیه** |
| حساب پنل | `Purpose = Bulk` | `Purpose = Otp` |

**حلقه مصرف:**

```csharp
// راه‌اندازی یک‌باره
await db.StreamCreateConsumerGroupAsync(stream, group, "0-0", createStream: true);

while (!ct.IsCancellationRequested)
{
    var entries = await db.StreamReadGroupAsync(
        stream, group, consumerName, ">", count: batchSize);

    if (entries.Length == 0) { await Task.Delay(500, ct); continue; }

    foreach (var entry in entries)
    {
        try
        {
            await ProcessAsync(entry);
            await db.StreamAcknowledgeAsync(stream, group, entry.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "...");
            // بدون XACK — پیام در Pending می‌ماند و توسط XAUTOCLAIM برداشته می‌شود
        }
    }
}
```

**پردازش هر پیام:**

1. بارگذاری `SmsMessage` از DB → اگر `Status != Queued` → `XACK` و رد شو (Idempotency)
2. اگر `ProviderMessageId` پر است → قبلاً ارسال شده → `XACK` و رد شو
3. `Status = Sending`
4. رمزگشایی اعتبارنامه `SmsAccount` با `IDataProtector`
5. `ISmsProviderFactory.Resolve(...).SendAsync(...)`
6. موفق → `Status = Sent`, `ProviderMessageId`, `SentAt`, `Cost` + `SmsStatusHistory`
7. ناموفق و `IsRetryable` → `Status = Queued`, `TryCount += 1`, `NextRetryAt = now + 2^TryCount دقیقه` → بدون `XACK`
8. ناموفق و غیرقابل تکرار، یا `TryCount >= 5` → `Status = Failed` + `XADD sms:dead` + `XACK`

### گام M5.4 — بازیابی پیام گیرکرده

`StreamClaimerService` هر ۲ دقیقه:

```csharp
var claimed = await db.StreamAutoClaimAsync(
    stream, group, consumerName, minIdleTimeInMs: 300_000, startAtId: "0-0", count: 100);
```

پیام‌هایی که بیش از `MaxDeliveryAttempts` بار Claim شده‌اند → `sms:dead` + `Status = Failed`.

### گام M5.5 — تریم Stream

هر ساعت: `XTRIM {stream} MAXLEN ~ 100000`.
امن است چون منبع حقیقت `OutboxMessages` و `SmsMessages` در SQL هستند.

### گام M5.6 — استعلام وضعیت تحویل

`DeliveryStatusPollerService` هر ۵ دقیقه:

1. انتخاب `SmsMessages` با `Status = Sent` و `SentAt` بین ۱ دقیقه تا ۲۴ ساعت پیش
2. گروه‌بندی بر اساس `SmsAccountId`
3. فراخوانی `GetStatusAsync` به‌صورت دسته‌ای (هر Provider سقف خودش را دارد؛ معمولاً ۱۰۰)
4. به‌روزرسانی `Status`, `DeliveredAt` + درج `SmsStatusHistory`
5. پس از ۲۴ ساعت بدون تغییر → `Status = Undelivered`

**Webhook اختیاری:** اگر Provider پشتیبانی می‌کند، `POST /api/v1/sms/dlr/{providerCode}` با اعتبارسنجی امضا یا IP Whitelist.

### گام M5.7 — پایش تأخیر صف

`QueueLagMonitorService` هر ۳۰ ثانیه:

- خواندن `XPENDING` و `XLEN` هر دو Stream
- محاسبه تأخیر: زمان قدیمی‌ترین پیام Pending
- **هشدار بحرانی اگر تأخیر `sms:otp` > ۳۰ ثانیه**
- انتشار به‌عنوان Metric در OpenTelemetry: `sms_queue_lag_seconds{stream="otp|bulk"}`

### ✅ DoD مایلستون M5

- [ ] پیامک لینک با Provider واقعی ارسال و در پنل پیامکی دیده می‌شود
- [ ] پیامک OTP از حساب `Purpose = Otp` ارسال می‌شود، نه از حساب انبوه
- [ ] تست: ۱۰٬۰۰۰ پیام در `sms:bulk` + یک پیام در `sms:otp` → پیام OTP در کمتر از ۱۰ ثانیه ارسال می‌شود
- [ ] کشتن Worker وسط پردازش → پیام پس از ۵ دقیقه با `XAUTOCLAIM` بازیابی می‌شود
- [ ] خطای دائمی Provider → پیام پس از ۵ تلاش در `sms:dead` و `Status = Failed`
- [ ] ارسال دوباره یک `SmsMessage` که `ProviderMessageId` دارد → پیامک تکراری ارسال نمی‌شود
- [ ] `RatePerMinute` رعایت می‌شود (سنجش با شمارش ارسال در دقیقه)
- [ ] استعلام وضعیت، `Delivered` را به‌درستی ثبت می‌کند

---

## M6 — گزارشات

**حجم:** متوسط · **وابستگی:** M5

### گام M6.1 — داشبورد

کارت‌های خلاصه (بازه پیش‌فرض: ۷ روز اخیر):

| شاخص | منبع |
|---|---|
| لینک ساخته‌شده | `COUNT(ShortLinks)` |
| نرخ کلیک | `View / Total` |
| نرخ دانلود موفق | `Download / View` |
| پیامک ارسالی (تفکیک نوع) | `SmsMessages GROUP BY MessageType` |
| نرخ تحویل | `Delivered / Sent` |
| پیامک ناموفق | `Status = Failed` |
| فضای اشغالی | `SUM(SizeBytes) WHERE Status != Deleted` |
| فایل در آستانه انقضا (۷ روز) | `COUNT` |
| تأخیر صف OTP | Redis |

نمودار روند ۳۰ روزه با Chart.js.

### گام M6.2 — گزارش لینک‌ها

فیلترها: بازه تاریخ، کلاینت، `ReportId`، `BatchTag`، وضعیت، `RequestId`، `ClientRequestId`، `Shop`، شماره تلفن، `Code`.

ستون‌ها: `Code`, `RequestId`, پرونده، `ReportName`, شماره (ماسک‌شده)، تاریخ ایجاد (شمسی)، انقضا، تعداد OTP، تعداد دانلود، وضعیت پیامک، وضعیت فایل.

صفحه جزئیات: تایم‌لاین کامل رخدادها از `LinkAccessLogs` + `SmsStatusHistories`.

### گام M6.3 — گزارش پیامک‌ها

فیلترها: بازه، کلاینت، حساب، **نوع پیام (لینک/OTP)**، وضعیت، شماره.

نمای تجمیعی: تعداد و هزینه به تفکیک `MessageType` و کلاینت — برای پاسخ به «چقدر از اعتبار صرف OTP شد؟».

عملیات: ارسال مجدد دستی پیام‌های `Failed` (فقط `Operator` به بالا، فقط برای `MessageType = DownloadLink` — ارسال مجدد OTP معنا ندارد چون کد منقضی شده).

### گام M6.4 — گزارش فضا و نگهداشت

- مصرف فضا به تفکیک کلاینت و ماه
- تعداد فایل در هر وضعیت (`Active`, `Expired`, `PendingDelete`, `Deleted`)
- پیش‌بینی: در ۳۰ روز آینده چند فایل و چه حجمی حذف خواهد شد
- تاریخچه حذف از `FileDeletionLogs`

### گام M6.5 — خروجی Excel

با `ClosedXML`. برای خروجی‌های بزرگ (> ۵۰ هزار سطر) به‌صورت Job پس‌زمینه با لینک دانلود، نه همزمان.

### گام M6.6 — تاریخ شمسی

`PersianDateHelper` با `System.Globalization.PersianCalendar`:
- `ToPersianDate(DateTime utc)` → تبدیل به وقت محلی سپس شمسی
- `ParsePersianDate(string)` برای فیلترها
- Tag Helper یا Extension برای استفاده در View

### ✅ DoD مایلستون M6

- [ ] جستجو با `RequestId` رکورد درست را برمی‌گرداند
- [ ] گزارش پیامک، هزینه OTP و لینک را جدا نشان می‌دهد
- [ ] خروجی Excel با تاریخ شمسی صحیح
- [ ] صفحه جزئیات لینک، تایم‌لاین کامل را نمایش می‌دهد
- [ ] `Viewer` نمی‌تواند «ارسال مجدد» را ببیند

---

## M7 — نگهداشت و پاکسازی

**حجم:** متوسط · **وابستگی:** M1، M4

### گام M7.1 — Job انقضا

`ExpirationJob` — روزانه در پنجره کم‌بار (ساعت ۱ تا ۵):

```sql
UPDATE StoredFiles
SET Status = 1, PendingDeleteAt = DATEADD(day, @graceDays, SYSUTCDATETIME())
WHERE Status = 0 AND FileExpiresAt < SYSUTCDATETIME();
```

سپس:

```sql
UPDATE sl SET IsActive = 0
FROM ShortLinks sl
JOIN StoredFiles sf ON sf.Id = sl.StoredFileId
WHERE sf.Status = 1 AND sl.IsActive = 1;
```

و برای هر لینک غیرفعال‌شده: `DEL link:{code}` از Redis (به‌صورت دسته‌ای با `Batch`).

### گام M7.2 — Job حذف فیزیکی

`FileDeletionJob` — روزانه، در پنجره کم‌بار:

```
حلقه:
  1. انتخاب حداکثر DeleteBatchSize (۵۰۰۰) رکورد:
     WHERE Status = 2 AND PendingDeleteAt < now
  2. برای هر رکورد:
     a. IFileStorage.DeleteAsync(storageKey)
     b. درج FileDeletionLog (Reason = RetentionPolicy)
     c. UPDATE Status = 3, DeletedAt = now, DeletedBy = 'system:retention'
     d. رکورد DB هرگز حذف نمی‌شود — فقط Status تغییر می‌کند
  3. Task.Delay(DeleteBatchDelayMs)
  4. اگر خارج از پنجره زمانی شدیم → توقف، ادامه در اجرای بعدی
  5. تکرار تا اتمام
```

**نکته پیک:** ۱۰۰ هزار فایل با بسته‌های ۵۰۰۰ تایی = ۲۰ بسته، در یک شب کامل می‌شود.

اگر حذف فیزیکی ناموفق بود (فایل قفل، دسترسی) → `PhysicalDeleteOk = false` در لاگ، `Status` بدون تغییر بماند تا اجرای بعدی دوباره تلاش کند.

### گام M7.3 — Job پوشه خالی

پس از `FileDeletionJob`، پوشه‌های خالی shard/روز/ماه را حذف کن. از پایین‌ترین سطح شروع کن و بالا برو. پوشه ماه جاری را دست نزن.

### گام M7.4 — Job فایل یتیم

`OrphanScanJob` — هفتگی:

**جهت اول (دیسک → DB):** پیمایش فایل‌های قدیمی‌تر از ۲۴ ساعت که در `StoredFiles` نیستند → حذف + `FileDeletionLog` با `Reason = Orphan`.

**جهت دوم (DB → دیسک):** رکوردهای `Status = Active` که فایلشان روی دیسک نیست → لاگ `ERROR` + هشدار در پنل. **حذف نکن** — ممکن است مشکل موقت Mount باشد.

**فایل‌های `.tmp`:** قدیمی‌تر از `TempFileMaxAgeHours` (۶ ساعت) → حذف.

### گام M7.5 — پایش فضای دیسک

`DiskSpaceHealthCheck`:

| اشغال | رفتار |
|---|---|
| < ۷۰٪ | `Healthy` |
| ۷۰–۹۰٪ | `Degraded` + هشدار در داشبورد و لاگ |
| > ۹۰٪ | `Unhealthy` + API بارگذاری `507` برمی‌گرداند |

### گام M7.6 — زمان‌بند

از `BackgroundService` با محاسبه زمان تا اجرای بعدی استفاده کن (نیازی به Hangfire نیست). قفل توزیع‌شده با Redis (`SET job:{name} {instanceId} NX EX 3600`) تا در صورت اجرای چند نمونه Worker، Job دوبار اجرا نشود.

### ✅ DoD مایلستون M7

- [ ] فایل با `FileExpiresAt` گذشته → `Status = Expired` و لینکش غیرفعال می‌شود
- [ ] کلید Redis لینک غیرفعال‌شده حذف می‌شود
- [ ] پس از Grace، فایل فیزیکی حذف و `Status = Deleted` می‌شود
- [ ] رکورد `StoredFile` و `ShortLink` در DB **باقی می‌مانند** (برای گزارش)
- [ ] `FileDeletionLog` برای هر حذف ثبت شده
- [ ] تست با ۱۰٬۰۰۰ فایل: Job بدون Timeout و بدون فشار روی DB کامل می‌شود
- [ ] فایل یتیم روی دیسک شناسایی و حذف می‌شود
- [ ] رکورد DB بدون فایل → فقط لاگ خطا، بدون حذف
- [ ] با اشغال > ۹۰٪، بارگذاری `507` برمی‌گرداند
- [ ] با اجرای دو نمونه Worker، Job فقط یک بار اجرا می‌شود

---

## M8 — رصدپذیری، تست و استقرار

**حجم:** متوسط · **وابستگی:** همه

### گام M8.1 — Serilog

- Sink: Console (JSON در Production) + File با Rolling روزانه + نگهداشت ۳۰ روز
- Enricher: `CorrelationId`, `ClientId`, `RequestId`, `MachineName`
- **هرگز لاگ نکن:** شماره تلفن کامل (ماسک کن)، کد OTP، اعتبارنامه پنل، توکن دانلود، API Key
- Middleware که `X-Correlation-Id` را می‌خواند یا می‌سازد و در `LogContext` می‌گذارد

### گام M8.2 — OpenTelemetry

Metric های اجباری:

| نام | نوع | برچسب |
|---|---|---|
| `links_created_total` | Counter | `client` |
| `file_upload_bytes` | Histogram | `client` |
| `otp_requested_total` | Counter | `client` |
| `otp_verify_total` | Counter | `result` |
| `downloads_total` | Counter | `client` |
| `sms_sent_total` | Counter | `type`, `provider`, `result` |
| `sms_queue_lag_seconds` | Gauge | `stream` |
| `disk_used_percent` | Gauge | — |
| `files_pending_delete` | Gauge | — |

### گام M8.3 — Health Check

`/health/live` (فقط زنده بودن) و `/health/ready`:

- SQL Server
- Redis
- فضای دیسک
- تأخیر صف OTP (بحرانی اگر > ۶۰ ثانیه)
- وجود حداقل یک `SmsAccount` فعال با `Purpose = Otp` برای هر کلاینت فعال

### گام M8.4 — تست‌ها

**Unit (پوشش هدف ۷۰٪ روی Application):**
`ShortCodeGenerator`, `RetentionResolver`, `TemplateRenderer`, نگاشت وضعیت هر Provider, `SanitizeFileName`, منطق Rate Limit.

**Integration (Testcontainers: MsSql + Redis):**
- مسیر کامل بارگذاری → OTP → دانلود
- Idempotency بارگذاری
- سناریوی Retention سرتاسری با دستکاری تاریخ
- بازیابی `XAUTOCLAIM` پس از کشتن Worker
- Fallback هنگام قطع Redis

**تست بار (k6 یا NBomber):**

| سناریو | هدف |
|---|---|
| بارگذاری | ۲۸ درخواست/ثانیه به مدت ۱۰ دقیقه، فایل ۱MB، p95 < ۲ ثانیه، بدون خطا |
| دانلود همزمان | ۱۰۰ کاربر همزمان، p95 < ۱ ثانیه |
| اولویت صف | ۱۰۰٬۰۰۰ پیام bulk + پیام‌های otp پراکنده → تأخیر OTP < ۱۰ ثانیه |

### گام M8.5 — سخت‌سازی امنیتی

- HTTPS اجباری + HSTS
- هدرها: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, CSP روی `PublicWeb`
- Rate Limit روی `/s/{code}` (۶۰ در دقیقه به‌ازای IP) برای مهار Enumeration
- بلاک IP پس از ۲۰ خطای `404` روی `/s/` در ۵ دقیقه
- **پنل مدیریت روی اینترنت منتشر نشود** — فقط شبکه داخلی یا پشت VPN
- `PublicWeb` و `Api` روی سایت‌های IIS جداگانه یا کانتینرهای جدا
- Data Protection Key ها روی مسیر پایدار مشترک ذخیره شوند (نه در حافظه)

### گام M8.6 — استقرار

**IIS (Windows):**
- سه Application Pool مجزا: `Api`, `Admin`, `PublicWeb`
- `Worker` به‌عنوان Windows Service با `sc create` یا `dotnet publish` + NSSM
- `maxAllowedContentLength` در `web.config` روی `4194304`
- Identity هر Pool دسترسی **Modify** روی `FileStorage:RootPath` داشته باشد

**Docker (Linux):** یک `Dockerfile` چندمرحله‌ای، مسیر ذخیره‌سازی به‌صورت Volume.

**Migration در استقرار:** با `dotnet ef migrations script --idempotent` اسکریپت بساز و دستی اجرا کن. **هرگز `Database.Migrate()` در Startup فراخوانی نکن** (خطر اجرای همزمان چند نمونه).

### گام M8.7 — بکاپ

| مورد | روش | تناوب |
|---|---|---|
| SQL Server | Full + Differential + Log | روزانه / ۶ ساعت / ۱۵ دقیقه |
| فایل‌ها | **Snapshot سطح Volume** (نه کپی فایل‌به‌فایل — با میلیون‌ها فایل کوچک بسیار کند است) | روزانه |
| Data Protection Keys | همراه بکاپ فایل | روزانه |
| Redis | نیازی نیست (فقط کش و صف؛ منبع حقیقت در SQL است) | — |

تست بازیابی حداقل یک بار قبل از Go-Live.

### گام M8.8 — Runbook

مستند عملیاتی شامل:
- نحوه بررسی تأخیر صف و اقدام در صورت انباشت
- نحوه ارسال مجدد پیام‌های `sms:dead`
- نحوه بازیابی فایل از بکاپ
- نحوه چرخش (Rotate) کلید API یک کلاینت
- نحوه افزودن Provider پیامکی جدید
- اقدام در صورت پر شدن دیسک
- چک‌لیست روز پیک

### ✅ DoD مایلستون M8

- [ ] `/health/ready` همه اجزا را `Healthy` گزارش می‌کند
- [ ] هیچ لاگی حاوی شماره کامل، OTP، توکن یا اعتبارنامه نیست (بازبینی دستی نمونه لاگ)
- [ ] تست بار: ۲۸ درخواست/ثانیه با p95 < ۲ ثانیه
- [ ] تست بار: تأخیر OTP زیر بار ۱۰۰ هزار پیام bulk کمتر از ۱۰ ثانیه
- [ ] پوشش تست واحد Application ≥ ۷۰٪
- [ ] استقرار روی محیط Staging و اجرای مسیر سرتاسری
- [ ] بازیابی موفق از بکاپ در محیط تست
- [ ] Runbook تحویل شده

---

## پیوست A — کلیدهای Redis

| کلید | TTL | توضیح |
|---|---|---|
| `link:{code}` | ۶۰ دقیقه | کش متادیتای لینک |
| `link:reserve:{code}` | ۳۰ ثانیه | رزرو کد هنگام تولید |
| `apikey:{sha256}` | ۵ دقیقه | کش اعتبارسنجی API Key |
| `otp:{code}` | ۱۲۰ ثانیه | **HMAC** کد، نه خود کد |
| `otp:cd:{code}` | ۹۰ ثانیه | Cooldown ارسال مجدد |
| `otp:cnt:{code}` | ۳۶۰۰ ثانیه | شمارنده درخواست به‌ازای لینک |
| `otp:ip:{ipHash}` | ۳۶۰۰ ثانیه | شمارنده درخواست به‌ازای IP |
| `otp:fail:{code}` | ۱۲۰ ثانیه | شمارنده تلاش نادرست |
| `dl:{token}` | ۳۰۰ ثانیه | توکن دانلود یک‌بارمصرف (`GETDEL`) |
| `rl:404:{ipHash}` | ۳۰۰ ثانیه | شمارنده ۴۰۴ برای بلاک Enumeration |
| `job:{name}` | ۳۶۰۰ ثانیه | قفل توزیع‌شده Job |
| `sms:bulk` | — | Stream پیامک انبوه |
| `sms:otp` | — | Stream پیامک OTP |
| `sms:dead` | — | Stream پیام‌های شکست‌خورده |

---

## پیوست B — کدهای خطا

| کد | HTTP | معنا |
|---|---|---|
| `ERR_MISSING_API_KEY` | 401 | هدر `X-Api-Key` ارسال نشده |
| `ERR_INVALID_API_KEY` | 401 | کلید نامعتبر، منقضی یا باطل‌شده |
| `ERR_CLIENT_INACTIVE` | 403 | کلاینت غیرفعال است |
| `ERR_METADATA_FIRST` | 400 | بخش `metadata` قبل از `file` نیامده |
| `ERR_VALIDATION` | 400 | خطای اعتبارسنجی فیلدها |
| `ERR_FILE_TOO_LARGE` | 413 | حجم بیش از ۳MB |
| `ERR_INVALID_FILE_TYPE` | 415 | Magic Number با فرمت مجاز مطابقت ندارد |
| `ERR_DISK_FULL` | 507 | فضای دیسک بیش از آستانه |
| `ERR_CODE_GENERATION_FAILED` | 500 | تولید کد یکتا پس از N تلاش ناموفق |
| `ERR_RATE_LIMITED` | 429 | سقف نرخ درخواست |
| `ERR_LINK_NOT_FOUND` | 404 | کد لینک وجود ندارد |
| `ERR_LINK_INACTIVE` | 410 | لینک غیرفعال شده |
| `ERR_LINK_EXPIRED` | 410 | مهلت دانلود تمام شده |
| `ERR_FILE_DELETED` | 410 | فایل به دلیل اتمام نگهداشت حذف شده |
| `ERR_OTP_COOLDOWN` | 429 | هنوز زمان ارسال مجدد نرسیده |
| `ERR_OTP_LIMIT_LINK` | 429 | سقف درخواست OTP برای این لینک |
| `ERR_OTP_LIMIT_IP` | 429 | سقف درخواست OTP برای این IP |
| `ERR_OTP_NOT_CONFIGURED` | 500 | حساب یا قالب OTP برای کلاینت تعریف نشده |
| `ERR_OTP_EXPIRED` | 400 | کد منقضی شده |
| `ERR_OTP_INVALID` | 400 | کد نادرست |
| `ERR_TOO_MANY_ATTEMPTS` | 429 | قفل موقت پس از ۵ تلاش نادرست |
| `ERR_TOKEN_USED_OR_EXPIRED` | 410 | توکن دانلود مصرف یا منقضی شده |
| `ERR_TOKEN_MISMATCH` | 403 | IP یا User-Agent با زمان صدور توکن نمی‌خواند |
| `ERR_FILE_MISSING` | 410 | فایل روی دیسک یافت نشد (ناسازگاری) |

قالب پاسخ خطا (RFC 7807):

```jsonc
{
  "type": "https://links.example.ir/errors/ERR_FILE_TOO_LARGE",
  "title": "حجم فایل بیش از حد مجاز است",
  "status": 413,
  "errorCode": "ERR_FILE_TOO_LARGE",
  "requestId": "0192f4c1-...",
  "detail": "حداکثر حجم مجاز ۳ مگابایت است."
}
```

---

## پیوست C — چک‌لیست نهایی

### پیش‌نیازهای سازمانی (خارج از کد — از M0 پیگیری کن)

- [ ] الگوی (Pattern) احراز هویت OTP در پنل پیامکی **هر کلاینت** تأیید و فعال شده
- [ ] مجوز ارسال لینک در پیامک گرفته شده و دامنه در Whitelist اپراتور ثبت شده
- [ ] ۱ TB فضای SSD با مسیر مشخص و رویه بکاپ تخصیص یافته
- [ ] گواهی SSL برای دامنه لینک کوتاه آماده است

### امنیت

- [ ] API Key فقط به‌صورت Hash ذخیره می‌شود
- [ ] اعتبارنامه پنل پیامکی رمزنگاری شده و در فرم ماسک می‌شود
- [ ] کد OTP فقط به‌صورت HMAC در Redis
- [ ] توکن دانلود یک‌بارمصرف و مقید به IP و User-Agent
- [ ] شماره تلفن در صفحه عمومی فقط با پیش‌شماره نمایش داده می‌شود (`0912*******`)
- [ ] بررسی Magic Number روی همه فایل‌ها
- [ ] `Content-Disposition: attachment` + `nosniff` روی همه دانلودها
- [ ] پنل مدیریت روی اینترنت عمومی منتشر نشده
- [ ] هیچ داده حساسی در لاگ نیست

### عملیاتی

- [ ] هشدار فضای دیسک در ۷۰٪ فعال است
- [ ] هشدار تأخیر صف OTP در ۳۰ ثانیه فعال است
- [ ] Job ها با قفل توزیع‌شده اجرا می‌شوند
- [ ] بکاپ فایل و DB تست بازیابی شده
- [ ] Runbook در دسترس تیم عملیات است

### تصمیمات معماری غیرقابل تغییر

این‌ها را بدون تأیید صریح تغییر نده:

1. **صف OTP و صف انبوه کاملاً جدا** — Stream مجزا، Worker مجزا، حساب پنل مجزا
2. **فایل هرگز کامل در حافظه بارگذاری نشود** — همیشه استریم
3. **رکورد DB هنگام حذف فایل پاک نمی‌شود** — فقط `Status = Deleted`
4. **حذف فایل دومرحله‌ای** — انقضا، سپس Grace، سپس حذف فیزیکی
5. **تمام `DateTime` ها در DB به UTC** — تبدیل فقط در لایه نمایش
6. **Idempotency روی `(ClientId, Shop, Shod, Radif, ReportId)`** — بارگذاری تکراری لینک جدید نمی‌سازد
7. **منبع حقیقت SQL است، Redis فقط کش و صف** — قطعی Redis نباید سرویس را بخواباند
