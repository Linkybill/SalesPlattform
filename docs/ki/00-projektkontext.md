# Projektkontext

## Zweck

Die SalesPlattform wird ein eigenständiges Dashboard- und Steuerungstool für
Vertriebsteams. Sie liest Daten über CRM-Adapter, synchronisiert sie in eine eigene
tenant-isolierte Datenbank und berechnet dort Prioritäten, Regeln, Ziele und
Auswertungen. Die Anwendung soll einer Vertriebsmitarbeiterin oder einem
Vertriebsmitarbeiter beim Öffnen eine priorisierte Tagesarbeit zeigen und der
Leitung belastbare Steuerungsinformationen geben.

## Aktueller technischer Stand

Tenant-Login-Korrektur vom 09.09.2026: Die Plattform hat React `0.1.50`
veröffentlicht; Sales verwendet das Registry-Paket in Manifest und Lockfile.
Die App übergibt `applicationRootUrl` separat von der tenantbezogenen
`applicationBaseUrl`. Die gemeinsame Library erkennt `/{tenantId}/...`, hält
OIDC-Callbacks an der App-Root und verhindert wiederholte identische Redirects.
Keine eigene Login-Implementierung und keine Änderung der NuGet-Version.
Alle drei Root-/OIDC-Vertragstests und der Frontend-Build sind bestanden.
Noch nicht ausgerollt; interaktiver Microsoft-Login bleibt live abzunehmen.
Details: `IdentityPlattform/docs/login-tenant-routing.md`.

Deployment-Bereinigung vom 09.09.2026 (noch nicht ausgerollt): Backend-Auth,
Trust und zentrale RabbitMQ-Secret-Verweise kommen aus dem gemeinsamen
Plattform-App-Profil; keine zweiten lokalen Setter und keine Backend-Credentials
im Frontend. Rebuild kombiniert Image/Profil/Restartmarker ohne Scale-0/1-Schleife.
Controller/HPA behalten bestehende Replica-Anzahlen. Zoho-Redirect/Frontend-
Callback stehen als `BackendUrls` in dieser App-Deployment-Datei (local/ax42-1),
nicht als Sonderfall im Plattform-Release. Optionale Zoho-Scopes/Webhook- und
lokale Sales-Mail-Settings bleiben app-eigen. Keine Shared-/React-Änderung hierfür.
Details und einmalige Helm-Replica-Migrationsgrenze:
`IdentityPlattform/docs/deployment-zustaendigkeiten.md`.

Frontend-Session-Korrektur vom 09.09.2026: Die gemeinsame React-Library ist
im Paketmanifest und Lockfile aktualisiert. Token-Erneuerung verändert die
autorisierten Fetch-Callbacks nicht mehr; ungespeicherte Settings bleiben bei
Hintergrund-Aktualisierungen erhalten. Benutzer-/Tenant-Wechsel setzen die
Ansicht weiterhin zurück. Keine eigene Auth-/Renewal-Implementierung in Sales.
Frontend-Build mit dem veröffentlichten Paket bestanden; noch nicht ausgerollt.

HTTPS-Ergänzung vom 09.09.2026: Frontend-nginx und Backend-Kestrel verwenden
intern TLS. Rebuild-/Pipeline-Profile mounten cert-manager-Zertifikate und die
öffentliche CA; Keycloak-/Platform-Backchannels sind HTTPS. Router Edge ist im
Application Router zusammengeführt. Fachliche Tenant-/App-Isolation bleibt;
Ausstellung/Erneuerung und Zertifikatsübersicht gehören zur Plattform, nicht zur
Sales-App. Details: `IdentityPlattform/docs/zertifikate.md`.

Deployment-Vertrag vom 09.09.2026: `appsettings.Deployment.json` definiert
`Frontend.Port=3003` ohne Pfadzusatz (Root `/`) für local/dev und ax42-1/dev. Aufgelöste Origins
ohne abschließenden Slash: `https://127.0.0.1:3003` bzw.
`https://176.9.57.203:3003`. Operator, Tenant-Portal und Aufmass liegen am
jeweiligen Host auf 3000, 3001 und 3002. Tenant-Pfade sind `/{tenantId}/...`;
die installierte Shared-Library übernimmt weiterhin Tenant-Auflösung und Auth.
Eigener Sales-Rollout aus diesem Repository (niemals Plattform/Aufmass mitbauen):
`deploy-all.ps1 -Target local|ax42-1 -Environment dev`, optional
`-ClusterNamePrefix abc`. Prefix verändert Namen, nicht URLs/Ports. Pro
Target/Environment nur eine Installation; kein stiller Cutover.
Plattform zuerst separat bereitstellen. App-Registrierung liefert den HTTPS-Origin;
API/Router/Controller müssen `ApplicationIngress__Contract=registration-v1`
unterstützen. Kein manueller App-URL-Eintrag im Plattform-Deployment und kein
automatischer Plattform-Rebuild. Die neue Solution-Trennung ist offline geprüft,
noch nicht ausgerollt; spätere Live-Nachweise unten betreffen den älteren Code.
Details: Plattform-Repository `docs/solution-deployments.md` und
`docs/deployment-konfiguration.md`.

App-Rebuilds nutzen den geprüften Namespace/Context, öffentliches Runtime-JS
und ein gemountetes Manifest für die Shared-Pflichtregistrierung. Auch direkte
Rebuilds akzeptieren `Environment`/`ClusterNamePrefix` und benötigen die vorherige
deploy-all-Zuordnung. Default-dev verwendet
`https://127.0.0.1:3003`; Zoho-Callbacks werden als
`/api/integrations/zoho/oauth/callback` und `/import` daran angehängt.
Die jeweilige OAuth-Redirect-URL muss extern in Zoho freigegeben werden.
Platform-API und Tenant-Portal kommen ausschließlich aus dem geprüften Plan;
Runtime-JS hat Vorrang vor Build-Werten. Alte URL-Prozessvariablen überschreiben
den Rebuild-Plan nicht. Der Root-URL-Umbau ist am 09.09.2026 lokal über
deploy-all erfolgreich auf `https://127.0.0.1:3003/` ausgerollt. HTML, Modul-JS,
Runtime-URLs, mandantenbezogene Asset-/OIDC-Callback-Pfade und API-Guards
(400 ohne Tenant, 401 ohne Anmeldung) sind geprüft. Remote bleibt noch offen.
Auch der vollständige Folgeaufruf ohne Endpoint-Schalter und anschließend
`rebuild-all.ps1 -Target local -Environment dev` separat sind erfolgreich.
Die registrierten Aufmass-/Plattform-Einstiege bleiben dabei erhalten.
Früherer Nachweis zum vorherigen URL-Stand:
Lokaler Gesamtrollout vom 09.09.2026 über deploy-all erfolgreich; Backend und
Frontend bereit, Manifest registriert. Anschließend separater Sales-Rebuild
mit demselben Profil ebenfalls erfolgreich (jeweils Exitcode 0).

Standalone-Ergänzung vom 09.09.2026: `rebuild-all.ps1 -Target local -Environment dev`
verwendet denselben lokalen Lifecycle-Lock und gepinnten kubectl wie die Plattform,
prüft die API-Bereitschaft vor dem Build und stellt Prozess-PATH/KUBECONFIG danach
wieder her. `-Preview` ist read-only; beide CMD-Launcher/Plattform-Aufrufe benötigen
PowerShell 7 (Skriptminimum 7.2). Remote-Rollouts jetzt über das eigene deploy-all.
HTTPS-Vertrag, Preview und beide Live-Läufe bestanden. Der Rebuild prüft HTML,
Modul-JavaScript und Runtime-URLs mit dem gemeinsamen CI-HTTPS-Prüfer. Node
erhält temporär die öffentliche Dev-CA über `NODE_EXTRA_CA_CERTS`, ohne
TLS-Bypass; Routing-Verzögerungen werden begrenzt wiederholt, Fehler brechen ab.
Entra-Credentials sind eingerichtet. Entra-Keycloak-Callbacks für local/dev
(127.0.0.1) und ax42-1/dev (176.9.57.203) nach Betreiberfreigabe ergänzt und
zurückgelesen; exakte Adressen stehen in der Plattform-Deployment-Dokumentation.
Echter Microsoft-Login bleibt separat abzunehmen, ebenso Zoho-/Tenant-
Fachabläufe. Vorhandene Plattform-PVCs/Zertifikate wurden weiterverwendet.

Stand: 2026-09-02.
Vault-/Service-Kommunikation: 2026-09-08; verbindliche Details in
[`08-vault-und-service-kommunikation.md`](./08-vault-und-service-kommunikation.md).

HTTPS-Root-Quellstand (09.09.2026): Rebuild, Frontend-Dockerfile, Manifest,
Backend- und Zoho-Defaults verwenden die eigene App-Origin mit Pfad `/`.
OIDC-Callbacks liegen unter `/auth/callback`, `/auth/silent-callback` und
`/auth/logout-callback`; Web-Origins enthalten nur die Origin.
Private Kubernetes-Verbindungen verwenden ebenfalls HTTPS; Traefik und
Zertifikate gehören zur Plattform. Direkte Vite-Entwicklung nutzt 3003 mit
`strictPort`, benötigt aber die zentrale HTTPS-/Profilversorgung. Ohne Runtime-JS
müssen Platform-API und Tenant-Portal aus dem Profil als `VITE_*` vorliegen;
die App enthält dafür keine festen Adressen. `tests/Test-LocalHttps.ps1`
prüft den Vertrag ohne Rollout. `node --test tests/RootUrls.test.mjs` prüft die
installierte Tenant-Auflösung für beide Hosts sowie den Runtime-Vorrang.
Shared-/React-Paketstände bleiben unverändert.

Der Release-Workflow kopiert das generierte `deployment-config.js` vor dem
Image-Build nach `frontend/public/assets` und liest App-, Platform-API- und
Tenant-Portal-Build-URLs aus den Release-Values. Produktion übergibt
`vars.PUBLIC_APPLICATION_URL` an das gepinnte Tooling. CI führt zusätzlich die
Root-/Runtime-Tests aus. Beide HTTPS-/Lifecycle-Verträge, sechs Frontend-Tests
über beide Apps und TypeScript-Prüfungen bestanden. Anschließend lokaler Build
und Rollout über deploy-all mit HTTPS-Abnahme bestanden; keine Änderungen in
Azure oder an externen Providerkonten für diese Portumstellung.

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
- Zoho-OAuth, die Zoho-Token-Erneuerung in der SalesPlattform, tenantbezogene
  Refresh-Tokens im zentralen Vault, Metadatenabruf und der vollständige read-only
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
  ein ausschließlich in Vault gespeichertes Secret-Setting; Client-ID und
  Datacenter sind normale Werte in der Tenant-Datenbank der App.
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
- Die installierten Paketstände werden ausschließlich aus
  `frontend/package.json`, `frontend/package-lock.json` und den Backend-
  `.csproj`-Dateien gelesen. Die Jobdefinition enthält neben Zeitplan und Aktivierung auch
  `ConcurrencyGroup` und `ConcurrencyScope`; Vollimport und Crawl verwenden
  gemeinsam `crm-synchronization`.
- Interne Platform-API-Aufrufe verwenden die technische Service-Identität aus
  `ServiceAuthentication`. Das Client-Secret wird ausschließlich als
  Kubernetes-Secret `identity-platform-secrets`, Schlüssel
  `PORTALAPP_CLIENT_SECRET`, nach `ServiceAuthentication__ClientSecret`
  injiziert. `IdentityPlatform:RegistrationSecret` bleibt eine zusätzliche
  App-/Bootstrap-Identifikation; es ersetzt nicht den S2S-Token und ist kein
  Verschlüsselungsschlüssel. Die technische Rolle `service-to-service` ist
  keine menschliche Sales-Rolle.
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
`CRM-Hooks erneuern` (`crm-subscription-maintenance`) ist mit täglich 03:00 Uhr
in `Europe/Berlin` als Standard registriert. Tenant-Admins können seinen
Zeitplan konfigurieren und ihn weiterhin manuell starten. Er erneuert die
Hooks und verarbeitet ausschließlich die von Zoho gemeldeten Datensätze.
Für die fachliche Wirkung werden anschließend die
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

Deployment-Stand 08.09.2026: Backend und Frontend auf Image-Tag `0.1.4` neu
gebaut und ausgerollt, jeweils `1/1` bereit. Manifestregistrierung erfolgreich;
Frontend/JavaScript HTTPS 200 und aktive Browser-Adressen HTTPS. CI und der
manuelle Release-Workflow liegen unter `.github/workflows/`; gemeinsame
SSH-/Helm-Implementierung und offene Remote-Bootstrap-Voraussetzungen stehen
in `IdentityPlattform/deploy/cicd/README.md`. `PACKAGES_TOKEN` und geschuetzte
GitHub-Environments sind fuer dieses Repository noch einzurichten. Keine
Remote-Deployments ausgefuehrt und keine Produktionsfreigabe erteilt.

Die Quelle ist `docs/pflichtenheft/Vertriebstool_Spezifikation.md`. Diese
Infodateien sind eine strukturierte Arbeitskopie für KI-Agenten. Sie enthalten
keine Aufforderung, alles sofort zu implementieren. Für eine Änderung gilt:

1. betroffene fachliche Anforderung identifizieren,
2. Auswirkungen auf Datenmodell, Rechte und Sync prüfen,
3. offene Entscheidung dokumentieren,
4. erst danach implementieren und Status aktualisieren.
