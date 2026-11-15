# Visitor Management

ASP.NET Core 8 Razor Pages app with EF Core + SQLite.

## Run

Development:

```bash
dotnet run --project src/VisitorManagement.csproj --urls http://localhost:5080
```

Local "deployment" (Release build):

```bash
dotnet publish src/VisitorManagement.csproj -c Release -o publish
dotnet publish/VisitorManagement.dll --contentRoot D:/repos/local/vms-claude/publish --urls http://localhost:5080
```

`--contentRoot` must be an absolute path (a relative one is resolved against the `publish` folder itself).

The SQLite database is created at `App_Data/visitors.db` under the content root on first run
(`src/App_Data` for `dotnet run`, `publish/App_Data` for the published build)
and seeded with four sample hosts. Schema changes are applied automatically at startup via EF Core migrations.

The webcam only works on `http://localhost` or over HTTPS — browsers block camera access on plain-HTTP network addresses.

## Multi-tenant

Each organisation (tenant) has a short ID used in every URL: `http://host/acme/`, `/acme/CheckIn`, `/acme/Visits` …
All tenants share one database; every row carries a `TenantId`.

- **Isolation**: EF Core global query filters restrict every query to the current tenant, and `SaveChanges` stamps new rows
  with it and refuses to modify another tenant's rows. Cross-tenant access has to opt out explicitly (`IgnoreQueryFilters`),
  which only the platform admin's counts do. Uniqueness (host/staff emails, list names, settings) is per tenant.
- **Routing**: the first URL segment is resolved to a tenant and becomes the request's `PathBase`, so every generated link,
  redirect, `~/` path and sign-in cookie is scoped to `/acme` automatically. Unknown or inactive tenants get a 404.
  IDs are 2–30 lowercase letters, digits and hyphens; words the app uses (`platform`, `css`, `account` …) are reserved.
- **Sign-in**: staff cookies are path-scoped to their tenant and carry a tenant claim checked on every request.
  Kiosk badge links are signed for one tenant and visit.
- **Platform admin** (`/platform`): separate accounts and cookie. First visit creates the platform admin; it then creates
  tenants (ID, name, and the tenant's first admin with a temporary password), renames IDs and (de)activates tenants.
  It sees per-tenant counts only, not tenant data.
- **Site root** (`/`): "Go to your organisation" – enter the ID to jump to its pages.
- Data from before multi-tenancy was moved into the tenant **`main`**.

## License packages

- **Packages** (`/platform/packages`, platform admins): name, base price per month, visitors included in the base price,
  maximum visitors per month, cost per extra visitor, minimum credit purchase, and which features are on. Defaults:

  | | Silver | Gold | Platinum |
  |---|---|---|---|
  | Price / month | ₹1,499 | ₹3,999 | ₹9,999 |
  | Included visitors | 300 | 1,500 | 5,000 |
  | Max visitors / month | 1,000 | 5,000 | 25,000 |
  | Per extra visitor | ₹8 | ₹6 | ₹4 |
  | Min. credit purchase | ₹500 | ₹1,000 | ₹2,000 |
  | Features | Appointments, Blacklist | + Business card scanning, Reports, Dashboard charts, Host import | + Custom branding, Pass templates |

  (These are the defaults for a new installation. Packages that already existed were converted from USD at ₹88 = $1.)

  Check-in, badges, visitor log, hosts, staff users and data export are always on. Edits apply immediately; inactive
  packages can't be assigned to new organisations; a package can only be deleted when unused. Existing organisations were
  put on Platinum so nothing they used was switched off.
- **Organisations** (`/platform/tenants`): a package is required when creating an organisation; it can be changed per
  organisation, which shows this month's check-ins, credit balance and cost, and can be granted free credit (an amount).
- **Credit is money** (a prepaid ₹ balance). **Billing period** is the calendar month. Check-ins up to the included number
  are covered by the base price; each further check-in deducts the package's *cost per extra visitor* from the credit balance.
  At the monthly maximum, or when the balance can't cover another visitor, check-in stops (kiosk visitors are told to see
  reception; staff see why). Unused credit carries over between months.
- **Plan & usage** (`/{tenant}/Plan`, all staff; Admin › Plan & usage): package and features, check-ins this month,
  included left, credit remaining (₹), possible check-ins left (included + what the credit covers), cost so far (base price +
  credit bought this month), and the credit history. Admins buy credit by amount (at least the package minimum; the page shows
  how many extra check-ins it covers); purchases are recorded "to be invoiced" (no payment gateway yet). A banner and a notification warn when check-ins run low or are paused.
- Pages for features outside the package are hidden from the menus and redirect to Plan & usage, which names the package that
  includes them. Without Custom branding / Pass templates the default banner and Classic pass are used (saved settings are kept).
- Currency: Indian rupee. `Billing:CurrencySymbol` (default `₹`) and `Billing:Culture` (default `en-IN`, so ₹1,00,000) in appsettings.

## Backups and data export

- **Tenant data export** (`/{tenant}/Admin/Export`, tenant admins): ZIP of CSVs (visits, hosts, staff without passwords,
  blacklist, dropdown lists, settings), the logo, `manifest.json` and `README.txt`; optionally photos, signatures and business card images.
  Date range or all time. Requires the admin's password, limited to 5 per 24 hours per tenant, and every export is logged
  (who, when, range, size) on the page. Built per tenant, so the database filters keep other tenants' data out.
- **Platform backup** (`/platform/backups`, platform admins): full-database backup via SQLite's online backup API
  (consistent while the app runs), integrity-checked, zipped with a manifest, saved to `Backup:Directory`
  (default `App_Data/backups`, outside the website). Download needs the password; names are validated (no paths);
  the newest `Backup:Keep` (default 20) are kept.
- **Restore**: stop the app, replace `App_Data/visitors.db` with `visitors.db` from the backup ZIP (delete any `-wal`/`-shm`
  files), start the app.
- **Scheduled server backups** (set up on the server; these manual tools are in addition to them), e.g. nightly with cron:
  `sqlite3 /srv/vms/App_Data/visitors.db ".backup '/srv/backups/vms-$(date +%F).db'"`, keep 14–30 days, copy off-site
  (object storage / storage box), and test a restore regularly. After moving to PostgreSQL, use `pg_dump` instead.

## Staff sign-in

Each tenant's first admin is created by the platform admin when the tenant is created (or, for a tenant with no accounts, at `/{tenant}/Account/Setup`).
Admins add other staff under **Admin › Staff users** with a temporary password, which the person must change at first sign-in.

| Role | Can use |
|---|---|
| Reception | Dashboard, check-in, appointments, visitor log, reports, blacklist, help |
| Admin | Everything above, plus hosts, staff users, dropdown lists and branding |

- Passwords are hashed with ASP.NET Core's `PasswordHasher` (PBKDF2). At least 8 characters with letters and numbers.
- 5 wrong passwords lock the account for 10 minutes; an admin reset clears the lock.
- Changing a password, role or active state signs that person out everywhere (security stamp in the cookie).
- There must always be at least one active admin; you can't deactivate yourself.
- **Kiosk:** `/CheckIn` stays open for visitors, shown without any staff navigation. After check-in a visitor can see
  only their own badge and photo, via a signed link that expires after 10 minutes. Everything else needs sign-in.
- Locked out with no admin left? Delete the rows in the `StaffUsers` table and the setup page reappears.

## Features

- **Dashboard** (`/`) – stat tiles, who is on site with an *Overstaying* filter, today's appointments, then four charts (visits per day, busiest hours, by purpose, by department). List or card view.
- **Kiosk check-in** (`/CheckIn`) – appointment visitors find their booking by access code or by mobile number (OTP first); walk-ins fill in a short form with department → host. Every visitor must:
  - take a **webcam photo**, shown on the badge and in staff views;
  - **verify their mobile number with an OTP**;
  - choose an **expected stay** (5 minutes to 8 hours by default), which sets their expected check-out time;
  - choose the **building and floor** they'll be in (lists in Admin › Dropdown lists; defaults Building A/B, Floor 1–4;
    hidden if a list is empty);
  - **sign** to accept the visitor agreement;
  - accept the visitor agreement (`Pages/Shared/_Agreement.cshtml`).
  The dashboard's *Check in* button opens this same flow, so staff can't skip the photo or the OTP.
- **Appointments** (`/Appointments`) – upcoming bookings by day; *New appointment* generates a 6-character access code and a planned stay.
- **Evacuation roll call** (`/Reports/Evacuation`, sidebar *Evacuation*, dashboard button) – everyone checked in right now,
  grouped by building then floor (in dropdown-list order; visits without a location last), with photo, phone, host and host phone.
  Filter by building, tick people off as they're accounted for (kept in that browser), print, or download CSV. Available to
  all staff on every license package. Covers visitors only – staff need their own roll call.
- **Messages** – no browser pop-ups: every message and confirmation (deletes, cancellations, check out all…) appears inline at
  the top of the page. The top bar's *Recent messages* menu lists the last 10 messages (kept in that browser per organisation
  and user; not on the kiosk). Use `data-confirm="…"` on a form, or `VmsMessages.show/ask` in `wwwroot/js/site.js`.
- **Overstay** – a visitor still checked in after their expected check-out is flagged everywhere; the dashboard and visitor log both have an *Overstaying* filter.
- **Blacklist** (`/Blacklist`) – blacklist from any visit or by hand, matched on mobile number in any format. Blacklisted visitors can't check in (the kiosk just says "see reception"), badge printing is disabled for them, appointments for them warn staff, and the page warns if one is on site.
- **Printable badges** (`/Visits/Badge/{id}`) – with photo, department and expected check-out, laid out for a 4x3in label printer.
- **Visitor log** (`/Visits`) – date range, status, department and text filters, pagination, CSV export. List or card view.
- **Reports** (`/Reports`) – visit register, daily summary, by department / host / purpose, overstays, frequent visitors and blacklist; filter by period, department, host and purpose; view with a chart, export CSV or print.
- **Hosts** (`/Employees`) – add hosts; deactivate instead of delete so history is kept.
- **Host import** (`/Employees/Import`) – CSV (comma, semicolon or tab) or Excel `.xlsx`, up to 2 MB / 5,000 rows. Columns Name and Email are required, Department and Phone optional; common alternative headings are recognised. A preview shows what will be added, updated, skipped or rejected (and why) before anything is saved. Options: update existing hosts (matched by email) and create missing departments. A CSV template is available on the page. Old `.xls` files aren't supported.
- **Pass templates** (`/Admin/Passes`) – Classic (4×3 in), Modern (4×3 in), Lanyard (3×4 in portrait) or Minimal label (4×2.25 in, black-only for thermal printers). Choose what's shown (logo, photo, company, host, valid until) and the footer text; the print page size follows the template.
- **Admin** (`/Admin/Lists`) – one tabbed page for every dropdown: visit purposes (with badge colour), departments, expected stay options, buildings and floors. Add, rename, recolour, reorder, deactivate; options in use can't be deleted.
- **Business card scanning** (walk-ins, optional) – *Scan with camera* or *Upload image* of the visitor's business card; the text is read in the browser (OCR) and fills in name, company, email and mobile number (fields the visitor already typed are kept, with a *Use this* option). The visitor checks the details and still verifies the mobile number by OTP. The card image is kept with the visit.
- **Signature** – visitors sign on screen to accept the visitor agreement. Staff see the signature and business card on the visit's badge page (never on the kiosk, never printed).

## OTP (demo mode)

`DummyOtpService` sends nothing and accepts **9999** for any number (the check-in screen says so).
After a correct code the server issues a signed 30-minute token tied to that number
(`PhoneVerification`), and check-in is refused unless the posted phone matches the token.

To go live, implement `IOtpService` with an SMS provider (Twilio, Azure Communication Services, ...):
generate a random code per number, store it with an expiry and an attempt limit, rate-limit sends,
then register it in `Program.cs` instead of `DummyOtpService`.

## Photos

Photos are JPEGs captured in the browser (max 640px wide), validated server-side, and stored in the
`VisitPhotos` table (one row per visit). They are served at `/visits/{id}/photo`.

## Extending

- **Host notifications**: `VisitService.NotifyHostAsync` only logs. Wire it to email (SMTP / SendGrid), Teams or Slack.
- **HTTPS**: before exposing the app beyond this PC, run it behind HTTPS so sign-in cookies and the webcam work on the network.
- **Schema changes**: `dotnet tool restore`, then `dotnet ef migrations add <Name> --project src/VisitorManagement.csproj -o Data/Migrations`.
- **Data retention**: add a scheduled job to purge visitor personal data, photos, signatures and business card images after your retention period.
- **Business card OCR**: Tesseract.js runs in the browser and is loaded from cdn.jsdelivr.net (with its English language data) the
  first time a card is scanned, so the kiosk needs internet access for it. To run fully offline, host `tesseract.js`, its worker,
  core and `eng.traineddata` under `wwwroot/lib` and pass `workerPath` / `corePath` / `langPath` in `getWorker` (`wwwroot/js/checkin.js`).
  Field extraction is `parseBusinessCard` in the same file.
