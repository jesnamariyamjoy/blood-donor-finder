# Blood Donor Finder (ASP.NET Core 8 + React)

Connects people needing blood in an emergency with nearby, compatible, eligible donors.

## Stack
- **Backend:** C# / .NET 8 Minimal API, EF Core + SQLite, JWT auth, Swagger
- **Frontend:** React 18 + Vite
- **Tests:** xUnit unit tests + integration tests (WebApplicationFactory)

## Run
    dotnet run --project api           # API on http://localhost:5000 (Swagger at /swagger)
    cd frontend && npm install && npm run dev   # UI on http://localhost:5173
    dotnet test tests                  # run all tests

Demo accounts (seeded): phone `9876500001` ... `9876500006`, password `demo1234`.

## Features
- Smart search: blood compatibility + 90-day eligibility + distance (Haversine) with "use my location"
- Accounts: register/login with JWT, PBKDF2-hashed passwords, brute-force rate limiting
- Privacy: anonymous visitors see masked phone numbers; health/location details never exposed publicly
- Emergency requests board: urgency levels, donor pledges, WhatsApp sharing, only the poster can close a request
- SMS alerts: new requests alert eligible compatible donors in the city; requester is texted on each pledge
- Donor profile: donation count, levels (Bronze to Hero), next-eligible date, pause availability, SMS opt-out, delete account
- Impact dashboard with supply-vs-demand shortage alerts

## SMS
Default provider logs messages to the console. For real SMS set (appsettings.json or env vars):
`Sms__Provider=Twilio`, `Twilio__AccountSid`, `Twilio__AuthToken`, `Twilio__From`.

## Before deploying
Set a strong `Jwt__Key` (env var), turn off `SeedDemoData`, serve over HTTPS, and switch `EnsureCreated()` to EF migrations.
