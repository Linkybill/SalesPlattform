# Report- und Navigationsprüfungen

Aus dem Repository nativ in Windows PowerShell ausführen:

```powershell
dotnet restore tests/Reports/Reports.csproj --configfile backend/NuGet.Config
dotnet run --project tests/Reports/Reports.csproj --no-restore
node --test tests/ReportNavigation.test.mjs tests/RootUrls.test.mjs
node tests/ReportBrowser.mjs
```

Die Frontend-Abhängigkeiten müssen unter `frontend/node_modules` installiert
sein. Der Browsertest benötigt Chrome unter dem üblichen Windows-Pfad oder
`REPORT_TEST_BROWSER` mit dem Pfad zu Chrome/Edge. Er startet Vite ausschließlich
auf Loopback und einen separaten temporären Browser mit synthetischer
Plattform-/CRM-Antwort. Das temporäre Browserprofil wird anschließend entfernt.
Produktive Anmeldung, CRM und Datenbanken werden nicht angesprochen.

Die Backend-Prüfung führt die tatsächliche Reportprojektion mit synthetischen
kanonischen Entitäten aus. Navigation/SSR prüfen Regeln, Layout-Sichtbarkeit,
Metadaten und Links. Der Browser prüft echte Klicks, Modaldialog, Suche,
Pagination, Escape/Fokusrückgabe, Zeitraumwahl und Desktop-/Handylayout.
Dies ersetzt keine produktive Login-/Tenant- oder Datenbankabnahme.
