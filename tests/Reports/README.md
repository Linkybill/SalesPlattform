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

## Zusätzliche Reportregressionen

SpecifiedReportRegressions prüft echte Projektionen aus synthetischen Entitäten:
Neu-/Bestandsumsatz, mehrjährige Reihen, unbekannte Besitzer, Vertragslaufzeit,
gemischte Währungen, Statusintervalle/Churn, fünf Kalendermonate, zukünftige
Termine, unveränderte Kontaktmarker, Anruf-Deduplizierung/Reset, Stufenconversion,
Verweildauer/Wahrscheinlichkeiten, gleichmäßige/saisonale Monats- und
Quartalsziele mit Cent-Summen, Arbeitszeit/Pausen/Feiertage, Standorte/Distanz,
fehlende/ungültige GeoJSON-Grenzen, Panelnachweise und Rollenbegrenzung.
ImportFieldRegressions prüft Aliasfallbacks bei null/leeren Feldern, Produkt-
Lookups, Auswahl bevorzugter Felder sowie unveränderte Bedeutung von 0/false.