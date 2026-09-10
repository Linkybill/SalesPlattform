# Vault, App-Settings und Service-Kommunikation

Stand: 10.09.2026. Diese Datei beschreibt den Ist-Stand und die verbindlichen
Anforderungen der SalesPlattform, keine Fehler- oder Datenhistorie.

Die Plattform ist für Vault-Betrieb und technische Client-Provisionierung
zuständig. Kanonische Referenzen im benachbarten Plattform-Repository:

- [Vault: Speicherung, Registrierung, Neustart und Recovery](../../../../IdentityPlattform/docs/vault.md)
- [Service-to-Service: Tokenvertrag und Deployment-Credentials](../../../../IdentityPlattform/docs/service-to-service-auth.md)
- [Plattform-KI-Kontext](../../../../IdentityPlattform/docs/ki-kontext.md)

## Settings und Zuständigkeiten

Sales definiert seine eigenen Settings in `backend/manifest.json`. Normale
`tenantApp`- und `appUser`-Werte liegen als JSON in der Tenant-Datenbank der App:
bei Shared tenant-isoliert im gemeinsamen Datenspeicher, bei Dedicated in der
Kundendatenbank. Das Ziel ergibt sich ausschließlich aus dem Plattform-Binding.
Secretwerte aller Scopes werden ausschließlich im zentralen Vault gespeichert.

| Setting / Credential | Scope | Speicherung und Zweck |
| --- | --- | --- |
| `crm.integration` | `tenantApp` | Normales Setting; Auswahl `none` oder `zoho` |
| `zoho.datacenter` | `tenantApp` | Normales Setting; Standard `eu` |
| `zoho.clientId` | `tenantApp` | Normales Setting; Client-ID der Zoho-Anwendung |
| `zoho.clientSecret` | `tenantApp` | `secret: true`; Client-Secret in Vault |
| `integration.zoho.default.refresh-token` | `tenantApp` | Internes Credential ohne Manifest-Editor; nach erfolgreichem OAuth vom Zoho-Adapter in Vault gespeichert |

Client-ID und Datacenter sind keine Secrets. Der Tenant-Admin pflegt die
Zoho-Konfiguration im Tenant Portal unter **SalesPlattform → AppSettings**.
Die Zoho-Felder werden bei `crm.integration = zoho` eingeblendet. Das
Secret-Passwortfeld setzt einen neuen Wert; gespeicherte Secretwerte werden
in normalen Settings-Antworten nicht zurückgegeben.

Die Zoho-OAuth-Verbindung wird anschließend in der Sales-Ansicht
**CRM-Integration → Zoho verbinden** aufgebaut. Codeaustausch, Provider-URLs,
Token-Erneuerung und CRM-Aufrufe bleiben vollständig im Zoho-Adapter des
Sales-Backends. Die Plattform stellt nur den generischen Secret-Speicher
bereit und implementiert keine Zoho-Fachlogik.

`ZohoConnectionStore` speichert in `integration_connections` nur
Verbindungsmetadaten wie Provider, Aktivierung, API-Domain und Zeitpunkte.
Client-Secret und Refresh-Token werden nicht in der Sales-Datenbank abgelegt.
Es gibt keinen app-eigenen `TokenProtectionKey`, keine Secret-DB-Verschlüsselung
und keinen daraus abgeleiteten Schlüssel aus `RegistrationSecret`.

## Kommunikationsweg

```text
Tenant-Portal-Browser
  -> eigene Portal-/Platform-API
     -> Application Router -> Sales-App-Settings-Backend
        -> normales Setting: Tenant-Datenbank
        -> Secret: S2S -> Platform API -> Vault

Sales-Zoho-Adapter
  -> IApplicationSettingsSecretStore -> S2S -> Platform API -> Vault
  -> Zoho-OAuth / Zoho-CRM mit den serverseitig aufgelösten Credentials
```

Der Browser kommuniziert weder direkt mit Vault noch mit einem fremden
App-Backend. Sales verwendet `IApplicationSettingsSecretStore` aus
`IdentityPlatform.Shared`, keine eigene Vault-Verbindung. Der gemeinsame
Adapter transportiert GET/PUT/DELETE zum internen Secret-Endpunkt der Platform
API mit App-Key, Tenant, Setting und Scope. Nur die Platform API besitzt die
Vault-Kubernetes-Auth und verwendet ihren `IPlatformSecretStore` direkt.

Der dedizierte interne Secret-GET darf den Wert an das berechtigte Backend
liefern. Normale Settings-Antworten bleiben maskiert; Zoho-Tokens werden nie
an das Frontend geliefert. Secret-Bodies, Tokenantworten und Credentials
dürfen nicht protokolliert oder ins Repository aufgenommen werden.

## Service-Identität und fachliche Rollen

Interne HTTP-Aufrufe verwenden Keycloak-Client-Credentials aus
`ServiceAuthentication`. Im aktuellen Plattformvertrag sind Client-ID und
Audience `portalapp`, die technische Realm-Rolle heißt `service-to-service`.
Die gemeinsame Service-Identität ist noch keine individuelle Identität je
App-Workload. Endpunkte müssen ihre App-/Tenant-/Benutzerprüfungen beibehalten.

Bei benutzerbezogenen Aufrufen bleibt `Authorization: Bearer ...` der
Benutzer-Token. Der separate Header
`X-Identity-Platform-Service-Authorization` trägt den technischen Token.
Ein Service-Account ersetzt weder Tenant-Admin noch fachliche Sales-Rollen.
Menschen erhalten keine Rolle `service-to-service`.

Die fachlichen Rollen bleiben `sales-user`, `sales-manager`,
`sales-management` und `sales-backoffice`, tenant-scoped gemäß dem
[Rollenvertrag](./04-rollen-ansichten-und-berechtigungen.md). Technische
Keycloak-Provisionierung erfolgt ausschließlich beim Start der Platform API;
sie darf den gemeinsamen Benutzer-Client-Scope `roles` nicht einschränken.
Router-Trust und `RegistrationSecret` sind zusätzliche, getrennte Verträge,
kein Ersatz für S2S-OAuth oder fachliche Autorisierung.

Das Laufzeit-Client-Secret wird aus dem Kubernetes-Secret
`identity-platform-secrets`, Schlüssel `PORTALAPP_CLIENT_SECRET`, als
`ServiceAuthentication__ClientSecret` injiziert. Es gehört zur
Deployment-Konfiguration, nicht zu den Zoho-App-Settings. Bei einer Rotation
müssen Plattform-Provisionierung und alle konsumierenden Workloads denselben
Wert bekommen und neu gestartet werden. Paket-Credentials wie
`GITHUB_PACKAGES_TOKEN` sind hiervon vollständig unabhängig.

Seit der Deployment-Bereinigung kommen diese Backend-Werte ausschließlich aus
dem gemeinsamen Runtime-Profil (`deploy/app-runtime.mjs` in der Plattform).
Die App erzeugt oder rotiert keine RabbitMQ-Zugangsdaten und betreibt keinen
eigenen Broker. Kubernetes-Secret-Verweise bleiben `valueFrom.secretKeyRef`;
keine zusätzlichen literalen `value`-Setter für dieselbe Variable. Frontends
erhalten keine Backend-/RabbitMQ-/Registrierungs-Credentials. App-eigene
Zoho- und fachliche Mail-Settings bleiben davon getrennt.

## Registrierung und Betriebspflichten

- `IdentityPlatform.Shared` registriert das Manifest bei jedem Sales-Backend-
  Start. Die Platform API stellt dabei die Vault-Policy/-Rolle ausschließlich
  für die gerade registrierende App sicher, auch bei unverändertem Manifest.
- Der App-Host startet erst nach erfolgreicher Registrierung weiter. Bei
  vorübergehend nicht verfügbarem Vault bleibt die Registrierung blockiert
  und wird wiederholt. Settings-Lese-/Schreibaufrufe provisionieren keine Rollen.
- Nach einem vollständigen Vault-Reset benötigt Sales seine eigene erneute
  Registrierung. Ein AufmassApp-Neustart schaltet Sales nicht frei. Gelöschte
  Secretwerte werden durch Registrierung nicht rekonstruiert.
- Ein normaler Plattform-/App-Neustart mit erhaltenem Vault-Datenspeicher
  erhält die Secrets. Init, Unseal, Sicherung und Recovery gehören ausschließlich
  zum Plattform-Lifecycle; die Sales-Skripte führen keinen Vault-Bootstrap aus.
- Weder Vault-Instanz noch Schlüsseldienst oder Unseal-Material gehören auf
  jeden App-Server. Betriebsdetails werden nur in der Plattform gepflegt.
- Paketstände stehen in den `.csproj`- und `package*.json`-Dateien. Shared-
  Änderungen benötigen zuerst eine veröffentlichte neue NuGet-Version und
  danach ein Consumer-Update. API-only-Provisionierung und Dokumentation
  benötigen keinen neuen Shared-/React-Paketstand.

Maßgebliche Sales-Dateien: `backend/manifest.json`,
`backend/Integrations/Zoho/ZohoConfigurationService.cs` und
`backend/Integrations/Zoho/ZohoConnectionStore.cs`.
