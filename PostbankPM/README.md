# PostbankPM

میزبان ASP.NET Core 8 محصول PostbankPM است. این پروژه منطق Core را کپی نمی‌کند و قابلیت‌ها را با `ProjectReference` از پروژه‌های NexusCore و ماژول‌های منتخب compose می‌کند. در زمان package شدن Core، همین referenceها می‌توانند با `PackageReference` نسخه‌دار جایگزین شوند.

## اجرا

```powershell
dotnet run --project PostbankPM/PostbankPM.csproj
```

- HTTP: `http://localhost:5267`
- HTTPS: `https://localhost:7267`
- Swagger: `/swagger`

Connection stringها و JWT signing key موجود در `appsettings.json` فقط مقدار توسعه هستند و در محیط واقعی باید با Secret/Environment Variable جایگزین شوند.
