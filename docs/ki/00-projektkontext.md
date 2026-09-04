# Projektkontext

## Zweck

Die SalesPlattform wird ein eigenständiges Dashboard- und Steuerungstool für
Vertriebsteams. Sie liest Daten über CRM-Adapter, synchronisiert sie in eine eigene
tenant-isolierte Datenbank und berechnet dort Prioritäten, Regeln, Ziele und
Auswertungen. Die Anwendung soll einer Vertriebsmitarbeiterin oder einem
Vertriebsmitarbeiter beim Öffnen eine priorisierte Tagesarbeit zeigen und der
Leitung belastbare Steuerungsinformationen geben.

## Aktueller technischer Stand

Stand: 2026-09-02.

- React/Vite-Frontend.
- ASP.NET-Core-Backend mit geschütztem `GET /api/worklist` sowie dem bisherigen
  technischen `GET /api/hello-world`.
- EF-Core-Datenmodell und tenant-isolierte Plattform-Datenbank.
- Registrierung über `backend/manifest.json` in der Identity Platform.
- App-Rollen: `sales-user` / „SalesPlattform Benutzer“, `sales-manager` /
  „Vertriebsleitung“, `sales-management` / „Management/Geschäftsführung“ und
  `sales-backoffice` / „Sales Backoffice“.
- Native Windows-PowerShell- und Docker-Rebuilds über `rebuild-all.ps1` bzw.
  `rebuild-all.cmd`.
- Zoho-OAuth, die Zoho-Token-Erneuerung in der SalesPlattform, verschlüsselte
  tenantbezogene Refresh-Tokens, Metadatenabruf und der vollständige read-only
  Initialimport der für das Pflichtenheft benötigten CRM-Daten sind umgesetzt.
  Die Identity Platform stellt dafür nur eine provider-neutrale
  Credential-Ablage bereit und enthält keine Zoho-Fachlogik.
- Die Identity Platform besitzt Definition, tenantbezogenen Cron-Zeitplan,
  durable RabbitMQ-Zustellung, Run-/Event-Historie und SignalR-Live-Status der
  Hintergrundjobs. Die gemeinsame Jobdetailansicht zeigt Live-Fortschritt,
  Logs, Fehler und strukturierte JSON-Details; aktive Läufe können dort echt
  abgebrochen werden. Die SalesPlattform registriert ihre
  Implementierungsklassen über `IdentityPlatform.Shared`.
- `crm-full-import` ist durch Tenant-Admins konfigurierbar (Default täglich);
  `crm-incremental-crawl` läuft fest alle 15 Minuten. Die gemeinsame React-
  Library integriert `/jobs` automatisch als tenantadmin-geschützten
  Headerpunkt.
- Beide Jobs rufen `CrmSynchronizationService` und anschließend den anhand von
  `crm.integration` ausgewählten `ICrmSynchronizationAdapter` auf. Zoho kennt
  weder Plattformjobdefinition noch Zeitplan; die Jobs kennen keine Zoho-API.
- Der Lauf protokolliert zuerst den Synchronisationsplan, danach den aktuellen
  Modulschritt mit gelesenen, geschriebenen, fehlgeschlagenen und noch offenen
  Datensätzen. Die Abschlussdetails enthalten zusätzlich die geschriebenen
  Records als strukturiertes JSON für die Jobdetailansicht.
- E-Mails bleiben Bestandteil desselben CRM-Sync-Laufs. Sie werden als
  Related-List der Elternobjekte Accounts, Leads und Deals gelesen;
  es gibt keinen separaten E-Mail-Sync-Job.
- Nach dem CRM-Sync wird die Arbeitsliste automatisch neu bewertet. Ein
  Vollimport bewertet alle Ziele; ein Incremental-Crawl übergibt nur die
  geänderten kanonischen Datensätze und folgt deren Integration-Links sowie
  Aktivitäts- und CRM-Zuordnungen zu den betroffenen Regelzielen.
- Vollimport und inkrementeller Crawl sind über die zentrale, mandantenbezogene
  Exklusivgruppe `crm-synchronization` gekoppelt und können nicht gleichzeitig
  laufen.
- Die allgemeine CRM-Integration wird über die Application Settings der
  Identity Platform je App/Mandant ausgewählt. Zoho ist aktuell der erste
  auswählbare Provider; seine Client-ID, sein Datacenter und sein Client-Secret
  werden nur eingeblendet, wenn `Zoho CRM` ausgewählt ist. Das Client-Secret ist
  ein verschlüsseltes Secret-Setting.
- Die CRM-Besitzerzuordnung wird ebenfalls tenantbezogen in den AppSettings
  gespeichert. Die Sales-App bietet dafür unter `Einstellungen` einen
  komfortablen Editor; gespeichert wird die Zuordnung über die stabile
  Plattform-Subject-ID und die `SalesOwner`-ID, mit E-Mail als Anzeige- und
  Fallback-Wert.
- Die Mindestdauer eines qualifizierten Gesprächs wird als
  `sales.callConversationThresholdSeconds` auf der Scope-Ebene `tenantApp`
  gespeichert. Der Standardwert beträgt 20 Sekunden; der Tenant-Admin kann ihn
  appweit zwischen 1 und 3600 Sekunden konfigurieren.
- Die Zeit- und Versuchsschwellen der Arbeitslistenregeln liegen als
  tenantbezogene `sales.rules.*`-App-Einstellungen vor. Die Arbeitsliste lädt
  sie bei jeder Bewertung; die Defaults entsprechen dem Pflichtenheft, unter
  anderem 14 Tage Anruf-Wiedervorlage, 6–10 Versuche für Langläufer, 30 Tage
  Deal-Inaktivität, 90 Tage Renewal-Horizont, 90 Tage Kontakt-Inaktivität und
  1/4 Arbeitsstunden für Lead-Erstreaktion und Eskalation.
- Die Regel- und Timeout-Konfiguration wird nicht in der Sales-App dupliziert.
  Sie ist ausschließlich auf `tenantApp` definiert und wird im Tenant Portal
  über „AppSettings“ der SalesPlattform gepflegt. Die Sales-App liest die
  effektiven Werte nur noch serverseitig für die Regelbewertung.
- Die aktuell integrierten Paketstände sind `@hammer2fall/identity-platform-react`
  `0.1.45` im Frontend und `IdentityPlatform.Shared` `0.1.45` im Backend.
  Die Jobdefinition enthält neben Zeitplan und Aktivierung auch
  `ConcurrencyGroup` und `ConcurrencyScope`; Vollimport und Crawl verwenden
  gemeinsam `crm-synchronization`.
- Der lokale K3d-Rollout vom 2026-09-02 ist verifiziert: Identity-Platform-API
  und Deployment-Controller, Aufmaß-Backend/-Frontend sowie Sales-Backend/-Frontend
  sind jeweils `1/1` bereit. Die laufenden Anwendungstags sind Aufmaß `1.0.0`
  und Sales `0.1.0`. Die Plattformdatenbank enthält die Migration
  `20260902090000_AddApplicationJobConcurrency`.
- Die fachliche Arbeitslisten-Projektion für R-01 bis R-18 ist umgesetzt. Die
  CRM-geführte Auflösung entfernt den lokalen „Erledigt“-Schritt. Neue
  Servicefälle, Angebote, Aufträge und Rechnungen werden im Full- und
  Incremental-Crawl synchronisiert und regelbezogen bewertet.
- Das Report-Dashboard ist als direkt bearbeitbarer Seitenbaum umgesetzt.
  Arbeitsliste, Cockpit, Team-Steuerung, Meeting Report, Analyse,
  Kundenstamm/Karte, Ziele/Pace, Aufräumen, Servicefälle sowie die
  kommerzielle Kette sind eigenständige Report-Komponenten. Tenant-Admins
  können auf der Reportseite Grids, Tabs, Akkordeons, Überschriften und
  Textblöcke hinzufügen, benennen, verschachteln und Reports dazwischen
  platzieren. Das Standardmodell enthält alle Reports; der JSON-Seitenbaum
  bleibt eine interne Implementierungsform.
- Die Report-API liefert eine tenantisolierte, read-only Auswertung aus dem
  kanonischen Sales-Modell. Unmittelbar in jedem Full- und Incremental-Sync
  werden die täglichen KPI-, Pipeline-, Aktivitäts- und Kundenstatus-Snapshots
  aktualisiert und Regelbenachrichtigungen direkt versendet. Dafür gibt es
  keine separaten Sales-Jobs oder eigenen Zeitpläne.

Die Regelbewertung nach dem CRM-Teil ist eine eigene Live-Phase. Der
Plattformfortschritt zeigt die gepr��ften Regeltreffer, bestehende Vorg��nge und
die verbleibende Menge; aktuelle Regelziele und das Speichern der Ergebnisse
werden als Job-Logs protokolliert. CRM-Aufgaben-Abgleich, Kennzahlen und
Benachrichtigungen bleiben bis zum tats��chlichen Abschluss sichtbar.

## Erste fachliche Umsetzung: Arbeitsliste

Die Startansicht ist für `sales-user` eine persönliche und für
`sales-manager` eine tenantweite, jeweils backendseitig gefilterte
Arbeitsliste. `GET /api/worklist?refresh=true` projiziert die aktuellen
CRM-Daten in die vorhandenen `sales_work_items` und sortiert sie nach dem dokumentierten
Prioritätsscore. Umgesetzt sind zunächst R-01 bis R-04 für Anruf-Follow-ups,
R-05 (hängender Deal), R-06 (Vertragsverlängerung), R-07 (fehlender/alter
Kundenkontakt), R-08 (Zuständigkeitswechsel), R-09 (neuer Lead ohne
Erstreaktion), R-10 (Cross-Selling), R-12 (mehrfach verschobener Termin),
R-13/R-14 (Account Care und Deal-Reaktivierung) sowie R-15 bis R-18 für
Servicefälle, Angebote, Aufträge und Rechnungen.

Die Einträge verwenden eine stabile Identität aus Mandant, Regel und Zielobjekt.
`POST /api/worklist/{id}/snooze` schließt die aktuelle Vorgangsinstanz mit dem
Grund `deferred` und erzeugt einen Nachfolger in derselben Vorgangskette. Der
Nachfolger besitzt ein `AvailableFrom` („Bearbeitung beginnen ab“); vor diesem
Zeitpunkt wird er von der Arbeitslisten-API nicht ausgeliefert. Einen
fachlichen Abschluss gibt es nur durch die CRM-Änderung und den folgenden Sync.
Lokale Aktionen erzeugen ein WorkItem-Ereignis und einen Audit-Eintrag. Besitzer werden über die
Benutzer-E-Mail dem CRM-Besitzer zugeordnet; bis zur Zuordnung werden nur
unzugeordnete Vorgänge gezeigt. Bei Anrufen zählen Nichterreichen, Mailbox und
falscher Ansprechpartner als Versuch, aber nicht als echter Kontakt. Eine echte
Gesprächsverbindung (standardmäßig mindestens 20 Sekunden, appweit
konfigurierbar) setzt den Zähler „seit letztem Gespräch“ zurück. Die
Regelbewertung läuft nach jedem Sync; beim
Incremental-Sync werden nur die betroffenen Datensätze und die abhängige
Beziehungskette ausgewertet. Die erste R-09-Variante misst eine verstrichene
Stunde; die vorhandenen Arbeitszeitkalender werden für die Arbeitszeitrechnung
der nächsten Regelengine-Stufe verwendet.

## Zielarchitektur

```text
Zoho CRM / Pipedrive / weitere CRM-Systeme
    -> jeweiliger ICrmSynchronizationAdapter
Identity-Platform-Jobs (voll / incremental / später webhook)
    -> CrmSynchronizationService
    -> Adapter liest, normalisiert und nutzt kanonische Repositories
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

Zoho-Änderungen können zusätzlich zum Incremental-Crawl über einen
provider-spezifischen Subscription-Adapter eingehen. Der gemeinsame Job
`CRM-Hooks verwalten` erneuert die Hooks und verarbeitet ausschließlich die von
Zoho gemeldeten Datensätze. Für die fachliche Wirkung werden anschließend die
betroffenen Regeln, CRM-Task-Spiegelung, Kennzahlen und Benachrichtigungen
aktualisiert; ein Hook startet keinen Vollimport.

## Fachliche Hauptansichten

1. Meine Arbeitsliste – tägliche, priorisierte Arbeit.
2. Cockpit – Management-Schnellüberblick.
3. Team-Steuerung – Zielerreichung und Aktivitäten pro Mitarbeiter.
4. Meeting Report – Qualität und Entwicklung der Termine.
5. Analyse – Umsatz, Produkte, Branchen, Regionen, Verlustgründe und Prozesse.
6. Kundenstamm – Karte, Abdeckung und weiße Flecken.
7. Ziele und Pace – Zielverfolgung gegen Zeitanteil.
8. Aufräumen – Dublettenprüfung mit manueller Zusammenführung.
9. Servicefälle – Beschwerden, Supportfälle, Prioritäten und Fristen.
10. Kommerzielle Kette – Angebote, Aufträge und Rechnungs-/Zahlungsstatus.

## Begriffe und Leitregeln

- Ein Deal entspricht genau einem Produkt; dadurch sind Umsätze direkt
  summierbar. Kombinierte Produktangaben gelten als Datenqualitätsauffälligkeit.
- Ein Gespräch zählt ab der appweit konfigurierten Mindestdauer, standardmäßig
  mindestens 20 Sekunden. Nicht erreichte Anrufe bleiben Versuche.
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
