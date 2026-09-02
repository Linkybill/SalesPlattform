# Ziel-Datenmodell der SalesPlattform

**Status:** EF-Modell, additive Migration und Zoho-Initialimport umgesetzt; produktiver Lauf steht nach der OAuth-Neuautorisierung aus
**Quelle:** Pflichtenheft, insbesondere Abschnitte 02, 04, 06, 08–18, sowie
`02-datenmodell-und-zoho.md` und `06-integrationsarchitektur.md`.

Diese Datei beschreibt das vollständige Zielmodell. Die Entitäten und die
additive EF-Migration sind im Backend vorhanden. Offene Mapping- und
Fachentscheidungen bleiben für spätere Anbieter, Rückschreiben und
Regelberechnungen bestehen; der fachliche Zoho-Initialimport ist davon nicht
mehr abhängig.

## Leitentscheidungen

- Das CRM bleibt führend für CRM-Stammdaten und CRM-Vorgänge.
- Die SalesPlattform speichert ein kanonisches, CRM-anbieterneutrales Modell.
- Zoho, später Pipedrive und weitere Anbieter, liefern über eigene Adapter in
  dasselbe Modell.
- Anbieter-IDs, Rohdaten, Cursor, Mapping und Sync-Läufe bleiben im Bereich
  `integration_*`. Fachregeln dürfen keine Zoho-Feldnamen kennen.
- Alle Tabellen sind tenant-isoliert. Jede Entität erbt von
  `PlatformTenantEntity`; bei einer Shared-Database-Strategie müssen auch
  Fremdschlüssel und eindeutige Indizes `TenantId` berücksichtigen.
- Primärschlüssel sind `uuid`. Zeitpunkte werden als UTC in `timestamptz`
  gespeichert. Geldbeträge verwenden `numeric(18,2)` plus ISO-Währungscode.
- CRM-Datensätze werden nicht still gelöscht. Nicht mehr gelieferte Datensätze
  werden als inaktiv bzw. als aus der Quelle entfernt markiert.
- `jsonb` ist nur für Rohdaten, flexible Adapter-Mappings, Regelparameter,
  Begründungen und Audit-Snapshots vorgesehen; fachliche Kernfelder bleiben
  typisierte Spalten.

## Fachliche Beziehungen

```text
Owner ───────< Customer ───────< Contact
   │              │  │
   │              │  └────────< Lead
   │              └───────────< Deal >──── Pipeline ───< PipelineStage
   │                              │  │
   │                              │  └──────── Product >── ProductCategory
   │                              └──────────< Contract
   │
   └──── Team / TeamMember

Deal ───< DealStageHistory
Customer / Lead / Deal / Contact ───< ActivityRelation >── Activity
Customer / Lead / Deal / Contact ───< AppointmentRelation >── Appointment

CRM Connection ───< ExternalEntityLink ───> kanonische Entität
CRM Connection ───< SyncRun ───< SyncRunItem / SyncError

Regel ───< RuleEvaluation ───> WorkItem ───< WorkItemRelation
FiscalYear ───< TargetPeriod ───< Target
SnapshotRun ───< KpiSnapshot / PipelineSnapshot / ActivitySnapshot
```

## Datenbankbereiche und Tabellen

### Kanonische Fachdaten

```text
sales_owners
sales_teams
sales_team_members
sales_customers
sales_customer_relationships
sales_customer_status_history
sales_contacts
sales_leads
sales_product_categories
sales_products
sales_pipelines
sales_pipeline_stages
sales_deals
sales_contracts
sales_deal_stage_history
sales_activities
sales_activity_relations
sales_appointments
sales_appointment_relations
sales_appointment_status_history
```

### Regelwerk, Arbeitslisten und Ziele

```text
sales_work_items
sales_work_item_relations
sales_work_item_events
sales_rule_definitions
sales_rule_runs
sales_rule_evaluations
sales_priority_profiles
sales_priority_weights
sales_fiscal_years
sales_target_periods
sales_targets
sales_work_calendars
sales_working_hours
sales_holidays
sales_communication_templates
sales_notifications
```

### Historie, Auswertungen und Datenqualität

```text
sales_snapshot_runs
sales_kpi_snapshots
sales_pipeline_snapshots
sales_activity_snapshots
sales_customer_status_snapshots
sales_data_quality_findings
sales_duplicate_candidates
sales_duplicate_decisions
sales_merge_operations
sales_owner_change_requests
sales_audit_log
```

### Integration und Import

```text
integration_connections
integration_oauth_states
integration_entity_links
integration_raw_records
integration_sync_runs
integration_sync_run_items
integration_sync_cursors
integration_sync_errors
integration_field_mappings
integration_pipeline_mappings
integration_stage_mappings
integration_writeback_operations
integration_webhook_events       # später, falls ein Anbieter Webhooks erhält
```

Die bereits vorhandenen Tabellen `integration_connections`,
`integration_oauth_states`, `integration_entity_links`,
`integration_raw_records`, `integration_sync_runs` und
`integration_sync_cursors` bleiben Bestandteil dieses Zielbereichs. Ihre
aktuellen Klassen bilden aber noch nicht alle Felder des Zielmodells ab.

### Remote-ID- und Löschsemantik

`integration_entity_links` ist der dauerhafte technische Schlüssel zwischen
CRM und SalesPlattform. Ein Link wird über Provider, Verbindung, kanonischen
Entitätstyp und `ExternalId` eindeutig aufgelöst. Die lokale Entität bleibt bei
einem CRM-Delete bestehen und erhält `SourceDeletedAt` bzw. `IsActive = false`;
der Link wird nicht entfernt. Ein erneuter Upsert derselben Remote-ID kann die
Entität dadurch wieder aktivieren, ohne Beziehungen oder Historie zu verlieren.

Für von der SalesPlattform erzeugte CRM-Tasks enthält der Link zusätzlich
`WorkItemId`. Damit kann ein Delete des CRM-Tasks eindeutig dem konkreten
Arbeitsvorgang zugeordnet werden. Der Vorgänger wird geschlossen, ein
Nachfolger in derselben `WorkItemChainId` angelegt und anschließend mit einer
neuen CRM-Task-Remote-ID verknüpft. Bei der Löschung des fachlichen Zielobjekts
(z. B. Lead oder Kunde) wird der betroffene Vorgang dagegen nur mit
`target-deleted-in-crm` geschlossen; ein Nachfolger ohne gültiges Ziel wird
nicht erstellt.

## Kanonische Entitäten

### Owner, Teams und Kundenorganisationen

#### `SalesOwner` → `sales_owners`

Repräsentiert einen Vertriebsmitarbeiter bzw. CRM-Besitzer. Die fachliche
Entität verwendet keine Zoho- oder Pipedrive-ID als Primärschlüssel.

```text
Id                     uuid PK
DisplayName            varchar(300) NOT NULL
Email                  varchar(320) NULL
IsActive               boolean NOT NULL
SourceCreatedAt        timestamptz NULL
SourceModifiedAt       timestamptz NULL
LastSeenAt             timestamptz NULL
SourceDeletedAt        timestamptz NULL
```

Die Anbieter-ID des Besitzers wird über `integration_entity_links` auf diesen
Owner abgebildet. Dadurch kann derselbe interne Owner später aus einem anderen
CRM oder manuell gepflegt werden.

#### `SalesTeam` / `SalesTeamMember` → `sales_teams`, `sales_team_members`

Die Teamzuordnung ist für Team-Steuerung, Zielerreichung und Berechtigungs-
auswertung nötig. Sie ist nicht identisch mit einer Identity-Platform-Rolle.

```text
sales_teams:
  Id, Key, Name, IsActive

sales_team_members:
  Id, TeamId, OwnerId, ValidFrom, ValidTo, IsPrimary
```

Historische Gültigkeitszeiträume verhindern, dass alte KPIs nach einer
Teamänderung rückwirkend falsch zugeordnet werden.

#### `SalesCustomer` → `sales_customers`

Repräsentiert einen Kunden bzw. eine Organisation/Account.

```text
Id                     uuid PK
Name                   varchar(300) NOT NULL
LegalName              varchar(300) NULL
TaxNumber              varchar(100) NULL
WebsiteDomain          varchar(300) NULL
Industry               varchar(200) NULL
PostalCode             varchar(30) NULL
City                   varchar(200) NULL
RegionCode             varchar(100) NULL
CountryCode            varchar(10) NULL
AddressLine1           varchar(300) NULL
HouseNumber            varchar(50) NULL
OwnerId                uuid NULL FK sales_owners
Status                 varchar(100) NOT NULL
LastContactAt          timestamptz NULL
LastPhoneCallAt        timestamptz NULL
LifetimeRevenue        numeric(18,2) NULL  -- abgeleiteter Cache
IsActive               boolean NOT NULL
NeedsReview            boolean NOT NULL
GeocodingStatus        varchar(40) NULL
Latitude               numeric(9,6) NULL
Longitude              numeric(9,6) NULL
SourceCreatedAt        timestamptz NULL
SourceModifiedAt       timestamptz NULL
LastSeenAt             timestamptz NULL
SourceDeletedAt        timestamptz NULL
```

`CountryCode` und `PostalCode` sind gemeinsam zu betrachten. Fehlt das Land,
kann beim Import konfiguriert Deutschland vorbelegt und `NeedsReview` gesetzt
werden. Nicht geocodierbare Kunden bleiben sichtbar und werden gezählt.

#### `SalesCustomerRelationship` → `sales_customer_relationships`

Für Konzern, Tochter, Holding und ähnliche Organisationsbeziehungen. Die
Beziehung ist im Pflichtenheft noch offen, das Modell hält sie deshalb
gerichtet und erweiterbar vor.

```text
Id, ParentCustomerId, ChildCustomerId, RelationshipType,
ValidFrom, ValidTo, Source, Notes
```

Ein Kunde darf nicht mit sich selbst verknüpft werden. Die fachlichen
Beziehungstypen werden erst nach der offenen Konzernentscheidung festgelegt.

#### `SalesCustomerStatusHistory` → `sales_customer_status_history`

Benötigt für Kundenbestand, Churn und Lifetime-Auswertungen.

```text
Id, CustomerId, Status, ValidFrom, ValidTo, SourceModifiedAt
```

### Kontakte und Leads

#### `SalesContact` → `sales_contacts`

```text
Id                     uuid PK
CustomerId             uuid NULL FK sales_customers
Name                   varchar(300) NOT NULL
FirstName              varchar(150) NULL
LastName               varchar(150) NULL
Email                  varchar(320) NULL
Phone                  varchar(100) NULL
MobilePhone            varchar(100) NULL
JobTitle               varchar(200) NULL
IsPrimary              boolean NOT NULL
IsActive               boolean NOT NULL
SourceCreatedAt        timestamptz NULL
SourceModifiedAt       timestamptz NULL
LastSeenAt             timestamptz NULL
SourceDeletedAt        timestamptz NULL
```

E-Mail und Telefonnummer werden zusätzlich normalisiert bzw. indexierbar
gespeichert, damit die Dublettenprüfung nicht von Originalformatierungen
abhängt.

#### `SalesLead` → `sales_leads`

Ein Lead kann bereits einem Kunden zugeordnet sein, muss es aber nicht.

```text
Id                     uuid PK
CustomerId             uuid NULL FK sales_customers
ContactId              uuid NULL FK sales_contacts
OwnerId                uuid NULL FK sales_owners
Name                   varchar(300) NOT NULL
CompanyName            varchar(300) NULL
Email                  varchar(320) NULL
Phone                  varchar(100) NULL
Status                 varchar(100) NOT NULL
Source                 varchar(150) NULL
LastContactAt          timestamptz NULL
LastPhoneCallAt        timestamptz NULL
ResponseDueAt          timestamptz NULL
CallsSinceConversation integer NOT NULL DEFAULT 0
TotalCallAttempts     integer NOT NULL DEFAULT 0
FirstActivityAt        timestamptz NULL
IsActive               boolean NOT NULL
NeedsReview            boolean NOT NULL
SourceCreatedAt        timestamptz NULL
SourceModifiedAt       timestamptz NULL
LastSeenAt             timestamptz NULL
SourceDeletedAt        timestamptz NULL
```

Die beiden Anrufzähler sind für die Regelengine materialisierte Werte. Die
einzelnen Anrufe bleiben zusätzlich in `sales_activities` dauerhaft erhalten.
`NULL` bzw. Platzhalterdaten wie `1900-01-01` werden vor der Berechnung als
„nie kontaktiert“ normalisiert.

### Produkte und Pipelines

#### `SalesProductCategory` / `SalesProduct` → `sales_product_categories`, `sales_products`

Ein Deal bezieht sich auf genau ein Produkt. Die Kategorie ist davon getrennt,
damit Cross-Selling und Produktmix auf Kategorieebene funktionieren.

```text
sales_product_categories:
  Id, Key, Name, IsActive, SortOrder

sales_products:
  Id, CategoryId, Key, Name, Description, IsActive,
  SourceCreatedAt, SourceModifiedAt, LastSeenAt, SourceDeletedAt
```

Ein Importwert wie `Produkt 3;Produkt 5` darf nicht automatisch in zwei Deals
oder eine neue Kategorie umgewandelt werden. Er erzeugt einen
Datenqualitätsbefund und bleibt im Rohdatensatz nachvollziehbar.

#### `SalesPipeline` → `sales_pipelines`

```text
Id, Key, Name, Description, IsActive, SortOrder,
SourceCreatedAt, SourceModifiedAt
```

Das Modell unterstützt von Beginn an fünf Pipelines. Die konkreten fünf
Pipelines sind laut Pflichtenheft noch zu bestätigen.

#### `SalesPipelineStage` → `sales_pipeline_stages`

```text
Id                     uuid PK
PipelineId             uuid NOT NULL FK sales_pipelines
Key                    varchar(100) NOT NULL
Name                   varchar(200) NOT NULL
StageType              varchar(30) NOT NULL  -- open, won, lost, other
SortOrder              integer NOT NULL
Probability            numeric(5,4) NULL
IsTerminal             boolean NOT NULL
IsActive               boolean NOT NULL
SourceModifiedAt       timestamptz NULL
```

`Probability` liegt im Bereich 0 bis 1 und ist je Pipeline/Stufe konfigurier-
bar. Der Deal referenziert `PipelineId` und `PipelineStageId`; reine
`PipelineKey`-/`StageKey`-Strings im Deal sind nur noch Import-Mappingwerte.

### Deals und Verträge

#### `SalesDeal` → `sales_deals`

```text
Id                     uuid PK
CustomerId             uuid NULL FK sales_customers
OwnerId                uuid NULL FK sales_owners
PipelineId             uuid NULL FK sales_pipelines
PipelineStageId        uuid NULL FK sales_pipeline_stages
ProductId              uuid NULL FK sales_products
Name                   varchar(300) NOT NULL
Amount                 numeric(18,2) NULL
Currency               varchar(10) NULL
Status                 varchar(100) NOT NULL
LossReason             varchar(300) NULL
DurationMonths         numeric(10,2) NULL
ContractStartAt        timestamptz NULL
ContractEndAt          timestamptz NULL
ClosingAt              timestamptz NULL
LastActivityAt         timestamptz NULL
IsActive               boolean NOT NULL
NeedsReview            boolean NOT NULL
SourceCreatedAt        timestamptz NULL
SourceModifiedAt       timestamptz NULL
LastSeenAt             timestamptz NULL
SourceDeletedAt        timestamptz NULL
```

Die Felder `ProductId`, `CustomerId`, Betrag und Stufe dürfen technisch
nullable bleiben, damit fehlerhafte Quellwerte importiert und in der
Datenqualitätsansicht bearbeitet werden können. Das Pflichtenheft verlangt
aber einen Deal pro Produkt; ein fehlendes Produkt ist daher ein Befund und
kein gültiger fachlicher Normalfall.

#### `SalesContract` → `sales_contracts`

Ein gewonnener Deal kann einen laufenden Vertrag erzeugen. Die separate
Entität verhindert, dass Renewal- und ARR-Logik nur aus dem aktuellen Dealtext
rekonstruiert werden muss.

```text
Id                     uuid PK
CustomerId             uuid NOT NULL FK sales_customers
DealId                 uuid NULL FK sales_deals
ProductId              uuid NULL FK sales_products
OwnerId                uuid NULL FK sales_owners
ContractNumber         varchar(150) NULL
Status                 varchar(50) NOT NULL
StartAt                timestamptz NULL
EndAt                  timestamptz NULL
DurationMonths         numeric(10,2) NULL
RecurringAmount        numeric(18,2) NULL
Currency               varchar(10) NULL
IsActive               boolean NOT NULL
SourceModifiedAt       timestamptz NULL
LastSeenAt             timestamptz NULL
SourceDeletedAt        timestamptz NULL
```

`ARR` wird aus den typisierten Vertrags-/Dealwerten berechnet, nicht als
unkontrollierter CRM-Wert übernommen.

#### `SalesDealStageHistory` → `sales_deal_stage_history`

```text
Id, DealId, PipelineId, PipelineStageId, StageKeySnapshot,
EnteredAt, ExitedAt, SourceObservedAt, SourceEventKey
```

Die Stage-Historie wird beim ersten vollständigen Sync vollständig und danach
inkrementell dauerhaft ergänzt. `StageKeySnapshot` bleibt als historische
Bezeichnung erhalten, falls ein CRM eine Stufe später umbenennt.

### Aktivitäten und Termine

#### `SalesActivity` → `sales_activities`

```text
Id                     uuid PK
ActivityType           varchar(100) NOT NULL  -- call, email, task, note, ...
Subject                varchar(500) NULL
OccurredAt             timestamptz NOT NULL
DurationSeconds        integer NULL
Direction              varchar(50) NULL
ConnectionStatus       varchar(50) NULL
ConversationClass      varchar(50) NULL  -- conversation, attempt, mailbox, unknown
CountsAsConversation    boolean NULL
Result                 varchar(200) NULL
OwnerId                uuid NULL FK sales_owners
IsCorrected            boolean NOT NULL
CorrectionNote         varchar(1000) NULL
SourceCreatedAt        timestamptz NULL
SourceModifiedAt       timestamptz NULL
LastSeenAt             timestamptz NULL
SourceDeletedAt        timestamptz NULL
```

Ein Gespräch zählt ab der appweit konfigurierten Mindestdauer, standardmäßig
ab 20 Sekunden. Liefert das Telefonsystem einen Verbindungsstatus, hat dieser
Vorrang. Die Korrekturmöglichkeit für
Mailbox/Telefonanlage bleibt durch `IsCorrected` und die Klassifikation
erhalten.

#### `SalesActivityRelation` → `sales_activity_relations`

Aktivitäten können einen Lead, Kontakt, Kunden und/oder Deal betreffen. Eine
polymorphe Relation verhindert unbrauchbare `RelatedExternalId`-Felder in der
Fachdomäne.

```text
Id, ActivityId, TargetType, TargetId, RelationRole
```

`TargetType` ist eine kontrollierte interne Menge (`customer`, `contact`,
`lead`, `deal`, `contract`, `service-case`, `offer`, `order`, `invoice`), keine
Zoho-Modulbezeichnung.

#### `SalesAppointment` → `sales_appointments`

```text
Id                     uuid PK
Subject                varchar(500) NULL
StartsAt               timestamptz NOT NULL
EndsAt                 timestamptz NOT NULL
Status                 varchar(100) NOT NULL
AppointmentType        varchar(150) NULL
OwnerId                uuid NULL FK sales_owners
OriginalStartsAt       timestamptz NULL
RescheduleCount        integer NOT NULL DEFAULT 0
IsActive               boolean NOT NULL
SourceCreatedAt        timestamptz NULL
SourceModifiedAt       timestamptz NULL
LastSeenAt             timestamptz NULL
SourceDeletedAt        timestamptz NULL
```

Die Statuswerte müssen mindestens geplant, stattgefunden, abgesagt,
verschoben und nicht erschienen unterscheiden. Ab drei Verschiebungen greift
R-12.

`sales_appointment_relations` hat dieselbe Struktur wie
`sales_activity_relations`. `sales_appointment_status_history` speichert
Statuswechsel, Verschiebungszeitpunkte und die Herkunft, damit der Meeting
Report nicht nur den aktuellen Status kennt.

### Servicefälle und kommerzielle Kette

Die CRM-Module `Cases`, `Quotes`, `Sales_Orders` und `Invoices` werden in
eigenen kanonischen Tabellen abgelegt. Dadurch bleiben Beschwerden,
Verkaufschance, Auftrag und Zahlung fachlich verknüpft, ohne dass die
Reportseite Zoho direkt abfragen muss.

```text
SalesServiceCase → sales_service_cases
Id, TenantId, CustomerId, ContactId, DealId, OwnerId, Subject, Description,
Status, Priority, Origin, Reason, OpenedAt, DueAt, ResolvedAt,
SourceCreatedAt, SourceModifiedAt, LastSeenAt, SourceDeletedAt, IsActive

SalesOffer → sales_offers
Id, TenantId, CustomerId, ContactId, DealId, OwnerId, Name, OfferNumber,
Status, Amount, Currency, IssuedAt, SentAt, ValidUntil,
SourceCreatedAt, SourceModifiedAt, LastSeenAt, SourceDeletedAt, IsActive

SalesOrder → sales_orders
Id, TenantId, CustomerId, OfferId, DealId, OwnerId, Name, OrderNumber,
Status, Amount, Currency, OrderedAt, PromisedAt, DeliveredAt,
SourceCreatedAt, SourceModifiedAt, LastSeenAt, SourceDeletedAt, IsActive

SalesInvoice → sales_invoices
Id, TenantId, CustomerId, OrderId, DealId, OwnerId, Name, InvoiceNumber,
Status, Amount, OpenAmount, Currency, IssuedAt, DueAt, PaidAt,
SourceCreatedAt, SourceModifiedAt, LastSeenAt, SourceDeletedAt, IsActive
```

Die Beziehungen sind nullable, weil CRM-Daten unvollständig sein können. Ein
fehlender Parent erzeugt einen Datenqualitätsfall, löscht aber weder die
Historie noch den Datensatz. `SalesContact.RoleType` hält zusätzlich die
fachliche Kontaktrolle wie Entscheider, Einkauf oder Rechnungswesen fest.

## Regelwerk, Arbeitsliste und Ziele

### `SalesWorkItem` → `sales_work_items`

Dies ist die einheitliche fachliche Vorgangstabelle für Wiedervorlagen,
Renewals, Reaktivierungen, Cross-Selling, Besitzerwechsel und Warnungen.

```text
Id                     uuid PK
WorkItemType           varchar(60) NOT NULL
Status                 varchar(40) NOT NULL
Title                  varchar(500) NOT NULL
Reason                text NULL
OwnerId                uuid NULL FK sales_owners
DueAt                  timestamptz NULL
AvailableFrom          timestamptz NULL -- frühester Bearbeitungszeitpunkt
PriorityScore          numeric(10,2) NULL
PriorityCalculatedAt   timestamptz NULL
SourceRuleCode         varchar(50) NULL
SourceRuleRunId        uuid NULL FK sales_rule_runs
RequiresApproval       boolean NOT NULL
CompletedAt            timestamptz NULL
CompletedBy            varchar(256) NULL
DismissedAt            timestamptz NULL
SnoozedUntil           timestamptz NULL
WorkItemChainId        uuid NOT NULL
PreviousWorkItemId     uuid NULL
ClosureReason          varchar(60) NULL
CreatedAt              timestamptz NOT NULL
UpdatedAt              timestamptz NOT NULL
```

`sales_work_item_relations` verknüpft einen Vorgang mit Kunde, Lead, Deal,
Vertrag, Aktivität, Termin, Servicefall, Angebot, Auftrag oder Rechnung. Damit
gibt es eine gemeinsame Arbeitsliste, ohne R-01 bis R-18 in getrennten Tabellen
zu duplizieren.

`sales_work_item_events` protokolliert Erzeugung, Regel-Neuberechnung,
Verschiebung, Erledigung, Verwerfung und Wiedereröffnung. Beim Zurückstellen
wird die aktuelle Instanz mit `ClosureReason = deferred` geschlossen und eine
Nachfolgeinstanz mit `AvailableFrom` angelegt. `WorkItemChainId` und
`PreviousWorkItemId` halten die historische Kette zusammen.

Der Score wird aus konfigurierbaren Basiswerten, Altersbonus und Wertbonus
berechnet. Die vorgeschlagenen Startwerte aus dem Pflichtenheft gehören in
`sales_priority_profiles` und `sales_priority_weights`, nicht in den Code.

### Regeln

#### `SalesRuleDefinition` → `sales_rule_definitions`

```text
Id, Code, Name, Description, IsEnabled, AutomationMode,
Version, ParametersJson, ValidFrom, ValidTo, UpdatedBy, UpdatedAt
```

Enthält R-01 bis R-18 einschließlich Schwellwerten, Intervallen,
Automatisierungsgrad und Besitzerlogik. `AutomationMode` unterscheidet zum
Beispiel Vorschlag, automatische Wiedervorlage und menschliche Freigabe.

#### `SalesRuleRun` / `SalesRuleEvaluation`

```text
sales_rule_runs:
  Id, TriggerType, Status, StartedAt, FinishedAt,
  RuleSetVersion, EvaluatedCount, CreatedCount, Error

sales_rule_evaluations:
  Id, RuleRunId, RuleDefinitionId, TargetType, TargetId,
  Outcome, WorkItemId, ExplanationJson, EvaluatedAt
```

So lässt sich erklären, warum ein Vorgang entstanden ist, und eine Regel
idempotent erneut ausführen.

### Ziele und Geschäftsjahr

#### `SalesFiscalYear` / `SalesTargetPeriod`

```text
sales_fiscal_years:
  Id, Name, StartsAt, EndsAt, TimeZone, IsClosed

sales_target_periods:
  Id, FiscalYearId, PeriodType, PeriodNumber, StartsAt, EndsAt,
  DistributionWeight
```

`DistributionWeight` ermöglicht Verteilungen wie `20 / 25 / 20 / 35 %`; die
Summe der Quartalsgewichte wird validiert.

#### `SalesTarget` → `sales_targets`

```text
Id                     uuid PK
FiscalYearId           uuid NOT NULL FK sales_fiscal_years
TargetPeriodId         uuid NULL FK sales_target_periods
OwnerId                uuid NOT NULL FK sales_owners
TargetType             varchar(60) NOT NULL
AppointmentType        varchar(150) NULL
TargetValue            numeric(18,2) NOT NULL
Currency               varchar(10) NULL
ApprovedAt             timestamptz NULL
ApprovedBy             varchar(256) NULL
ValidFrom              timestamptz NOT NULL
ValidTo                timestamptz NULL
```

`TargetType` deckt mindestens Umsatz, erreichte Gespräche, neue Termine,
versendete Angebote und neue Pipeline-Deals ab. Das Gesamtziel je Mitarbeiter
und Geschäftsjahr wird nicht auf Pipeline-Ebene aufgeteilt, sofern die
Fachspezifikation das nicht später ändert.

### Arbeitszeit und Kommunikation

`sales_work_calendars`, `sales_working_hours` und `sales_holidays` modellieren
Zeitzone, Arbeitswochen, Pausen und Feiertage. Sie werden für R-09 und die
Lead-Response-Zeit verwendet. Arbeitszeiten dürfen nicht als feste
`DateTime`-Logik im Code stehen.

`sales_communication_templates` speichert die E-Mail-Vorlage aus R-02 und
spätere Benachrichtigungstexte. `sales_notifications` ist die idempotente
E-Mail-Outbox und hält Empfänger, Zustellschlüssel, Vorgang, Betreff/Inhalt,
Fälligkeit, Eskalationsstufe, Zustellstatus, Versuchs-/Retry-Informationen und
Gelesen-Zeitpunkt. Der Transport ist über einen Providervertrag entkoppelt;
lokal wird SMTP/Mailpit verwendet.

## Snapshots und KPI-Fakten

### `SalesSnapshotRun` → `sales_snapshot_runs`

```text
Id, SnapshotDate, SnapshotType, Status, StartedAt, FinishedAt, Error
```

### `SalesPipelineSnapshot` → `sales_pipeline_snapshots`

```text
Id, SnapshotRunId, SnapshotDate, PipelineId, PipelineStageId,
OwnerId, OpenDealCount, OpenAmount, WeightedAmount, Currency
```

Damit sind Pipeline je Stufe, gewichtete Pipeline, Stufenverweildauer und
historische Funnel-Vergleiche reproduzierbar.

### `SalesKpiSnapshot` → `sales_kpi_snapshots`

```text
Id                     uuid PK
SnapshotRunId          uuid NOT NULL FK sales_snapshot_runs
SnapshotDate           date NOT NULL
PeriodType             varchar(20) NOT NULL  -- day, month, quarter, year, lifetime
PeriodStart            date NOT NULL
PeriodEnd              date NOT NULL
MetricKey              varchar(100) NOT NULL
OwnerId                uuid NULL FK sales_owners
PipelineId             uuid NULL FK sales_pipelines
ProductCategoryId      uuid NULL FK sales_product_categories
Industry               varchar(200) NULL
CountryCode            varchar(10) NULL
PostalRegion           varchar(10) NULL
Value                  numeric(20,4) NULL
CountValue             bigint NULL
Numerator              numeric(20,4) NULL
Denominator            numeric(20,4) NULL
Currency               varchar(10) NULL
```

Diese Tabelle deckt die acht Cockpit-KPIs, Analyse nach Branche/Produkt/
Region, Churn, Win Rate, Sales Cycle, ARR, Zielerreichung und Lifetime ab.
Die Dimensionen sind typisierte Spalten; zusätzliche erklärende Details dürfen
als JSON ergänzt werden.

`sales_activity_snapshots` hält geplante, stattgefundene, abgesagte,
verschobene und versäumte Termine sowie erreichte/nicht erreichte Anrufe je
Owner, Typ und Zeitraum. `sales_customer_status_snapshots` hält die Bestands-
und Abgangszählung für Churn und Lifetime.

Ohne diese Snapshots sind Aussagen wie „plus 18 % zum Vormonat“ nicht belastbar.

## Datenqualität, Dubletten und Freigaben

### `SalesDataQualityFinding` → `sales_data_quality_findings`

```text
Id, Code, Severity, Status, EntityType, EntityId, FieldName,
Message, DetailsJson, DetectedAt, ResolvedAt, ResolvedBy
```

Beispiele: fehlender Betrag, fehlender Verlustgrund, fehlende Branche,
kombinierte Produktangabe, fehlende Geoverortung und ungültiges Kontakt-
datum. Befunde sind persistent und werden nicht bei jedem Lauf unkontrolliert
neu dupliziert.

### Dubletten

`sales_duplicate_candidates` enthält ein kanonisch geordnetes Kundenpaar,
Punktzahl, Sicherheitsstufe, Merkmalsdetails und Prüfstatus. Die Bewertung
folgt den Pflichtenheftpunkten für USt-ID, E-Mail-Domain, Telefonnummer,
Namensähnlichkeit, Anschrift, PLZ und Website.

`sales_duplicate_decisions` speichert „kein Duplikat“, „prüfen“, „Merge
freigegeben“ bzw. „zurückgestellt“, Entscheider, Zeit und die gewählten
führenden Feldwerte. „Kein Duplikat“ bleibt dauerhaft gespeichert.

`sales_merge_operations` protokolliert Quelle, Ziel, Status, Freigabe,
übertragene Deals/Aktivitäten/Termine, CRM-Writeback-Referenz und Fehler.
Zusammenführen wird niemals automatisch ausgeführt, auch nicht bei einer
Punktzahl ab 100.

### Besitzerwechsel

`sales_owner_change_requests` enthält mindestens:

```text
Id, TargetType, TargetId, CustomerId, OldOwnerId, ProposedOwnerId,
SourceRuleCode, Reason, Status, RequestedAt, DecidedAt, DecidedBy,
AppliedAt, WritebackStatus
```

Die Vertriebsleitung entscheidet. Nach Freigabe entsteht die Wiedervorlage
für den neuen Besitzer; alter Besitzer, neuer Besitzer, Zeitpunkt und Regel
bleiben revisionssicher erhalten.

### `SalesAuditLog` → `sales_audit_log`

```text
Id, ActorSubject, ActorDisplayName, Action, EntityType, EntityId,
OccurredAt, BeforeJson, AfterJson, CorrelationId
```

Der Audit-Log ist für manuelle Entscheidungen, Statusänderungen,
Besitzerwechsel, Dublettenentscheidungen und optionale Rückschreibungen
zuständig. Secrets und Access-/Refresh-Tokens werden niemals protokolliert.

## Integration und Import

### `IntegrationConnection` → `integration_connections`

Enthält Provider, Verbindungsschlüssel, externe Organisation, API-Domain,
Aktivstatus und technische Status-/Zeitfelder. Keine Client-Secrets oder
Refresh-Tokens als Klartextspalten.

Mehrere Verbindungen pro Tenant werden über `(ProviderKey, ConnectionKey)`
unterschieden, zum Beispiel Zoho Production und Zoho Sandbox.

### Externe Identität und Rohdaten

`integration_entity_links` erhält neben `ProviderKey` künftig auch
`ConnectionKey`:

```text
ProviderKey, ConnectionKey, EntityType, ExternalId, ExternalUrl?
  -> InternalEntityType, InternalEntityId, LastSeenAt, SourceDeletedAt
```

`integration_raw_records` speichert optional das unveränderte `jsonb`,
ExternalModifiedAt, FirstSeenAt, LastSeenAt, SourceDeletedAt und den
zugehörigen `SyncRunId`. Rohdaten sind für Debugging und Reprocessing da, nicht
für fachliche SQL-Abfragen.

### Fachlicher CRM-Synchronisationslauf

Die zentrale Jobdefinition und Run-Historie gehören der Identity Platform.
`integration_sync_runs` bleibt die app-eigene, tenantisolierte Fachhistorie
mit Modulständen, Cursorn und Datensatzfehlern und verwendet dieselbe Run-ID:

```text
Id                     uuid PK
ProviderKey            varchar(50) NOT NULL
ConnectionKey          varchar(100) NOT NULL
Mode                   varchar(30) NOT NULL  -- full, incremental, manual
Status                 varchar(30) NOT NULL  -- queued, running, succeeded,
                                             -- completed_with_errors, failed, cancelled
RequestedModulesJson   jsonb NOT NULL
RequestedBy            varchar(256) NULL
QueuedAt               timestamptz NOT NULL
StartedAt              timestamptz NULL
FinishedAt             timestamptz NULL
CurrentModule          varchar(100) NULL
RecordsRead            integer NOT NULL
RecordsWritten         integer NOT NULL
RecordsFailed          integer NOT NULL
RetryCount             integer NOT NULL
LeaseUntil             timestamptz NULL
WorkerId               varchar(200) NULL
Error                  varchar(4000) NULL
CorrelationId          varchar(200) NULL
```

`integration_sync_run_items` protokolliert je Modul die Phase, Cursor,
Zeitpunkte, Zähler und Fehler. `integration_sync_errors` enthält einzelne
Datensatz-/Modulfehler mit ExternalId, klassifiziertem Fehlercode und
Retry-Information.

Der Ablauf ist:

```text
Platform: Zeitplan/manueller Start -> zentraler Run -> durable Queue-Nachricht
Shared Worker: TenantId/RunId setzen -> registrierte Jobklasse aufrufen
Sales Job: CrmSynchronizationService -> ausgewählten Provideradapter aufrufen
Adapter: raw/link/upsert -> Modulhistorie/Cursor/Fehler in Tenant-DB speichern
Platform: Fortschritt/Abschluss speichern und per SignalR an `/jobs` senden
```
zulässig.

`integration_sync_cursors` wird um `ConnectionKey`, `LastSuccessfulRunId`,
`LastStartedAt` und optional `LastError` ergänzt. Ein Cursor wird nur nach
erfolgreicher Verarbeitung des jeweiligen Moduls fortgeschrieben.

### Mapping

`integration_field_mappings` speichert provider- und verbindungsspezifische
Zuordnungen:

```text
Id, ProviderKey, ConnectionKey, SourceEntityType, SourceField,
TargetEntityType, TargetField, TransformationKey, IsRequired,
ConfigurationJson, Version, IsActive
```

Zoho-Feldnamen bleiben damit im Adapter-/Mapping-Bereich. `TransformationKey`
verweist auf getestete Normalisierer, nicht auf beliebig ausführbaren Code aus
der Datenbank.

Pipelines und Stufen erhalten zusätzlich eigene Zuordnungstabellen. Das ist
wichtig, weil ein CRM unterschiedliche externe IDs, Namen, Reihenfolgen und
Wahrscheinlichkeiten liefern kann:

```text
integration_pipeline_mappings
Id, ProviderKey, ConnectionKey, ExternalPipelineId,
InternalPipelineId, SourceNameSnapshot, IsActive, LastSeenAt

integration_stage_mappings
Id, ProviderKey, ConnectionKey, ExternalPipelineId, ExternalStageId,
InternalPipelineId, InternalStageId, SourceNameSnapshot, SourceProbability,
IsActive, LastSeenAt
```

Die Zuordnung ist damit pro Tenant und CRM-Verbindung eindeutig. Ein
`SalesPipelineStage` ist eine interne, tenant-spezifische Fachstufe und kennt
keine Zoho-, Pipedrive- oder HubSpot-ID. Der Import löst externe Pipeline- und
Stufen-IDs über diese Tabellen auf; unbekannte Werte bleiben als Rohdaten
erhalten und erzeugen einen Datenqualitätsbefund, statt stillschweigend nach
dem Namen zu matchen.

## Indizes und Integritätsregeln

- Jeder Index beginnt logisch mit `TenantId` bzw. verwendet bei Tenant-
  übergreifender Shared-Database die entsprechende zusammengesetzte Struktur.
- Externe Identitäten sind eindeutig nach
  `(TenantId, ProviderKey, ConnectionKey, EntityType, ExternalId)`.
- Interne kanonische IDs sind niemals externe IDs.
- Owner-, Pipeline-, Stage-, Produkt- und Kundenbeziehungen werden als
  typisierte FKs modelliert; polymorphe Zuordnungen gibt es nur in den
  ausdrücklichen Relationstabellen.
- Häufige Arbeitslisten erhalten Indizes auf `(TenantId, Status, DueAt)`,
  `(TenantId, OwnerId, Status)` und `(TenantId, PriorityScore)`.
- Deals erhalten Indizes auf Kunde, Pipeline/Stufe, ClosingAt,
  ContractEndAt und LastActivityAt.
- Aktivitäten und Termine erhalten Indizes auf Zeitpunkt, Owner und
  Relation.
- Status-/History-Tabellen erhalten Indizes auf Ziel und Gültigkeitsbeginn.
- Geldwerte, Wahrscheinlichkeiten und Dauer werden nicht als `double`
  gespeichert.
- Löschungen aus dem CRM führen nicht zu kaskadierenden Löschungen fachlicher
  Historie. Der Import markiert Quelle und Datensatz als entfernt/inaktiv.

## Import-Mapping in das Zielmodell

| CRM-Bereich | Kanonische Zielentitäten | Besondere Behandlung |
|---|---|---|
| Accounts | Customer, Owner, CustomerStatusHistory | Adresse normalisieren, Geocoding-Befund erzeugen |
| Contacts | Contact, Customer | E-Mail/Telefon normalisieren |
| Leads | Lead, Owner, ActivityRelation | Kontakt-/Response-Felder und Zähler normalisieren |
| Deals | Deal, Customer, Owner, Pipeline, Stage, Product, Contract | fehlende Pflichtfelder als Datenqualität markieren |
| Stage History | DealStageHistory | beim ersten Full-Sync vollständig übernehmen |
| Activities/Calls | Activity, ActivityRelation | Gesprächsklassifikation und zwei Anrufzähler ableiten |
| Events/Appointments | Appointment, AppointmentRelation, AppointmentStatusHistory | Terminstatus und Verschiebungen historisieren |
| CRM Owner | Owner, Team/TeamMember soweit verfügbar | keine CRM-ID in Domainregeln |

Jeder Importdatensatz durchläuft immer:

```text
raw record -> external link -> normalisierte DTO -> kanonische Entität
           -> History/Status -> Datenqualitätsbefund -> Cursor
```

Der Adapter darf keine fachlichen Regeln ausführen. Nach dem Import führt
derselbe CRM-Lauf unmittelbar die Regelbewertung, die Aktualisierung des
Tages-Snapshots und den direkten Versand der Regelbenachrichtigungen aus. Die
Outbox ist dabei nur die idempotente Zustell- und Retry-Sicherung; sie ist kein
separater Plattformjob.

## Reihenfolge der Umsetzung

### Vor dem ersten Import bestätigen

1. Zoho-Module, API-Version, Paging und Änderungsfelder.
2. Feld- und Stage-Mappings je Zoho-Organisation.
3. Die fünf Pipelines und ihre Stufen/Wahrscheinlichkeiten.
4. Fachliche Rollenmatrix und Identity-Platform-Rollen.
5. Geschäftsjahr, Zeitzone, Arbeitszeit und Feiertagskalender.
6. Behandlung von Verträgen, Konzernbeziehungen und eingehenden Anrufen.
7. Aufbewahrung von Rohdaten, History, Snapshots und Auditdaten.

### Technische Reihenfolge

1. Kanonische Entitäten und Tenant-FKs vervollständigen.
2. Integrationsschema inklusive persistentem `SyncRun`-Job erweitern.
3. EF-Migration und Constraints/Indizes erstellen.
4. Provider-neutrale Normalisierungs- und Mapping-DTOs definieren.
5. Zoho-Adapter auf alle Zielentitäten und Historien erweitern.
6. Ersten Full-Import als Job ausführen und Datenqualitätsbericht prüfen.
7. Inkrementellen Sync ist als Cursor-/Soft-Delete-Job umgesetzt; Regelengine
   und Snapshots werden nachgelagert aktiviert.

Punkt 6 ist technisch implementiert und wird nach der einmaligen OAuth-
Neuautorisierung mit den erweiterten Zoho-Scopes produktiv ausgeführt. Die
Migration `CompleteSalesDomainModel` bildet die für Initial- und
inkrementelle Läufe nötige Struktur ab; Regelengine, Snapshots und weitere
Provider bleiben nachgelagerte Arbeit.
