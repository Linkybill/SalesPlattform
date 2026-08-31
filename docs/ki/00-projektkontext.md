# Projektkontext

## Zweck

Die SalesPlattform wird ein eigenständiges Dashboard- und Steuerungstool für
Vertriebsteams. Sie liest Daten über CRM-Adapter, synchronisiert sie in eine eigene
tenant-isolierte Datenbank und berechnet dort Prioritäten, Regeln, Ziele und
Auswertungen. Die Anwendung soll einer Vertriebsmitarbeiterin oder einem
Vertriebsmitarbeiter beim Öffnen eine priorisierte Tagesarbeit zeigen und der
Leitung belastbare Steuerungsinformationen geben.

## Aktueller technischer Stand

Stand: 2026-08-30.

- React/Vite-Frontend.
- ASP.NET-Core-Backend mit geschütztem `GET /api/hello-world`.
- EF-Core-Datenmodell und tenant-isolierte Plattform-Datenbank.
- Registrierung über `backend/manifest.json` in der Identity Platform.
- Vorhandene App-Rolle: `sales-user` / „SalesPlattform Benutzer“.
- Native Windows-PowerShell- und Docker-Rebuilds über `rebuild-all.ps1` bzw.
  `rebuild-all.cmd`.
- Zoho-OAuth, die Zoho-Token-Erneuerung in der SalesPlattform, verschlüsselte
  tenantbezogene Refresh-Tokens, Metadatenabruf und ein erster read-only Import
  von Accounts, Deals und Leads sind umgesetzt. Die Identity Platform stellt
  dafür nur eine provider-neutrale Credential-Ablage bereit und enthält keine
  Zoho-Fachlogik.
- Die allgemeine CRM-Integration wird über die Application Settings der
  Identity Platform je App/Mandant ausgewählt. Zoho ist aktuell der erste
  auswählbare Provider; seine Client-ID, sein Datacenter und sein Client-Secret
  werden nur eingeblendet, wenn `Zoho CRM` ausgewählt ist. Das Client-Secret ist
  ein verschlüsseltes Secret-Setting.
- Regelengine, Cockpit und Fachansichten bleiben weiterer Zielumfang.

## Zielarchitektur

```text
Zoho CRM / Pipedrive / weitere CRM-Systeme
    -> jeweiliger Adapter
    -> periodischer Sync und Normalisierung
Kanonisches SalesPlattform-Domainmodell in eigener Datenbank
    -> Regelengine / Berechnungen / Snapshots
React-Ansichten und Arbeitslisten
```

Das jeweils verbundene CRM bleibt führend für Stammdaten und Prozesse. Die
SalesPlattform speichert
zusätzlich Stage-Historie, Aktivitäts- und Deal-Snapshots, Ziele,
Konfigurationen, berechnete Vorgänge und eigene Wiedervorlagen. Ein eventuelles
Rückschreiben ist begrenzt, explizit aktiviert und abschaltbar.

Das Domainmodell bleibt unabhängig vom Anbieter. Zoho ist der erste Adapter;
Pipedrive und weitere Anbieter werden später nach demselben Muster ergänzt.
Details stehen in [`06-integrationsarchitektur.md`](./06-integrationsarchitektur.md).

## Fachliche Hauptansichten

1. Meine Arbeitsliste – tägliche, priorisierte Arbeit.
2. Cockpit – Management-Schnellüberblick.
3. Team-Steuerung – Zielerreichung und Aktivitäten pro Mitarbeiter.
4. Meeting Report – Qualität und Entwicklung der Termine.
5. Analyse – Umsatz, Produkte, Branchen, Regionen, Verlustgründe und Prozesse.
6. Kundenstamm – Karte, Abdeckung und weiße Flecken.
7. Ziele und Pace – Zielverfolgung gegen Zeitanteil.
8. Aufräumen – Dublettenprüfung mit manueller Zusammenführung.

## Begriffe und Leitregeln

- Ein Deal entspricht genau einem Produkt; dadurch sind Umsätze direkt
  summierbar. Kombinierte Produktangaben gelten als Datenqualitätsauffälligkeit.
- Ein Gespräch zählt standardmäßig ab mindestens 20 Sekunden. Nicht erreichte
  Anrufe bleiben Versuche.
- Wiedervorlagen werden von der SalesPlattform selbst erzeugt und verwaltet;
  sie sind nicht bloß eine Anzeige vorhandener CRM-Aufgaben.
- Besitzerwechsel, Dubletten-Merges und automatische Statusänderungen brauchen
  menschliche Entscheidung gemäß Pflichtenheft.
- Zeit wird in UTC gespeichert und lokal angezeigt. Geschäftsjahr,
  Arbeitszeitfenster, Pipelines und Schwellwerte sind konfigurierbar.

## Umgang mit Anforderungen

Die Quelle ist `docs/pflichtenheft/Vertriebstool_Spezifikation.md`. Diese
Infodateien sind eine strukturierte Arbeitskopie für KI-Agenten. Sie enthalten
keine Aufforderung, alles sofort zu implementieren. Für eine Änderung gilt:

1. betroffene fachliche Anforderung identifizieren,
2. Auswirkungen auf Datenmodell, Rechte und Sync prüfen,
3. offene Entscheidung dokumentieren,
4. erst danach implementieren und Status aktualisieren.
