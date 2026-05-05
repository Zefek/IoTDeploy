# IoTDeploy

Windows aplikace pro automatické nasazování přes GitHub Actions self-hosted runner. Primárně pro flashování firmware do IoT zařízení (Arduino), volitelně i pro nasazení .NET aplikací z předem připravených buildů (artefaktů).

## Co aplikace dělá

IoTDeploy umožňuje jedním kliknutím (nebo z příkazové řádky) spustit deployment workflow na GitHubu. Aplikace:

1. Připojí se k GitHub repozitáři přes GitHub App autentizaci
2. Stáhne a nakonfiguruje GitHub Actions self-hosted runner
3. Stáhne Arduino CLI toolchain (capability deployera — využije ho IoT workflow, .NET workflow ho ignoruje)
4. Volitelně rozliší build artefakt z předchozího CI runu (`--useartifact <runId|latest>`)
5. Spustí deployment workflow s parametry (větev, prostředí, COM port a/nebo artefakt)
6. Automaticky schválí deployment přes GitHub Environments
7. Po dokončení uklidí runner

---

## Požadavky

- **Windows 10/11** (aplikace je WinForms)
- **.NET 9.0 Runtime** – [stáhnout zde](https://dotnet.microsoft.com/download/dotnet/9.0)
- **IoT zařízení** připojené přes USB (COM port)
- Přístup k internetu (stahování runneru, Arduino CLI, GitHub API)

---

## Příprava GitHub App

Aplikace se autentizuje vůči GitHubu pomocí **GitHub App** (ne osobním tokenem). Postupuj takto:

### 1. Vytvoření GitHub App

1. Přejdi do **GitHub → Settings → Developer settings → GitHub Apps → New GitHub App**
   (nebo pro organizaci: **Org Settings → Developer settings → GitHub Apps**)

2. Vyplň:
   - **GitHub App name**: `IoT Deployer` (nebo libovolný název)
   - **Homepage URL**: libovolná URL (např. URL repozitáře)
   - **Webhook**: odškrtni `Active` (webhook nepotřebujeme)

3. **Permissions** – nastav tato oprávnění:

   | Oprávnění | Úroveň |
   |-----------|--------|
   | Repository → Actions | Read & write |
   | Repository → Administration | Read & write |
   | Repository → Deployments | Read & write |
   | Repository → Environments | Read-only |
   | Repository → Metadata | Read-only |

4. **Where can this GitHub App be installed?** → dle potřeby (`Only on this account` nebo `Any account`)

5. Klikni **Create GitHub App**

### 2. Získání App ID

Po vytvoření se zobrazí stránka s nastavením aplikace. **App ID** je hned nahoře (číslo). Zapiš si ho.

### 3. Vygenerování privátního klíče

1. Na stránce nastavení GitHub App scrolluj dolů na sekci **Private keys**
2. Klikni **Generate a private key**
3. Stáhne se soubor ve formátu `<jméno-aplikace>.<datum>.private-key.pem`
4. **Přejmenuj nebo ponech** soubor tak, aby odpovídal vzoru `iot-deployer.*.private-key.pem`
   - Pokud máš jiný název, uprav `PemFilePattern` v `appsettings.json`
5. Zkopíruj `.pem` soubor do složky vedle spustitelného souboru aplikace

### 4. Instalace GitHub App do repozitáře

1. Na stránce GitHub App klikni **Install App** (vlevo)
2. Vyber účet/organizaci a poté repozitář (nebo všechny)
3. Klikni **Install**
4. Po instalaci se v URL zobrazí **Installation ID** (číslo za `/installations/`). Zapiš si ho.
   - Alternativně: v nastavení organizace **Installed GitHub Apps** → klikni na app → URL obsahuje installation ID

### 5. Nastavení GitHub Environment jako chráněného prostředí

Aby aplikace mohla automaticky schvalovat deployment, musí být GitHub App přidána jako **Required Reviewer** v Environment:

1. Přejdi do repozitáře → **Settings → Environments**
2. Vyber (nebo vytvoř) environment, do kterého chceš nasazovat
3. V sekci **Deployment protection rules** zaškrtni **Required reviewers**
4. Přidej GitHub App jako reviewera (zadej jméno app)
5. Ulož

Alternativně použij přiložený PowerShell skript:

```powershell
.\setup-environment-reviewer.ps1 `
  -Repository "nazev-repozitare" `
  -Environment "nazev-prostredi" `
  -Token "ghp_tvujPersonalniToken"
```

---

## Konfigurace aplikace

Vedle spustitelného `.exe` souboru musí být soubor `appsettings.json`:

```json
{
  "GitHub": {
    "AppId": "ZDE_APP_ID",
    "InstallationId": ZDE_INSTALLATION_ID,
    "Owner": "jmeno-organizace-nebo-uzivatele",
    "PemFilePattern": "iot-deployer.*.private-key.pem"
  },
  "Runner": {
    "WorkflowTimeoutMinutes": 2,
    "Labels": ["iot"]
  }
}
```

### Popis položek

| Položka | Popis | Příklad |
|---------|-------|---------|
| `GitHub.AppId` | App ID z GitHub App (viz krok 2) | `"2427873"` |
| `GitHub.InstallationId` | Installation ID (viz krok 4) | `98456061` |
| `GitHub.Owner` | Jméno GitHub uživatele nebo organizace | `"MojeOrg"` |
| `GitHub.PemFilePattern` | Vzor pro nalezení `.pem` souboru | `"iot-deployer.*.private-key.pem"` |
| `Runner.WorkflowTimeoutMinutes` | Timeout čekání na spuštění workflow (minuty) | `2` |
| `Runner.Labels` | Labely self-hosted runneru (musí odpovídat `runs-on` ve workflow) | `["iot"]` |

---

## Umístění souborů

Výsledná struktura složky aplikace musí vypadat takto:

```
IoTDeploy/
├── IoTDeployUI.exe          ← spustitelný soubor
├── appsettings.json          ← konfigurace
└── iot-deployer.2026-01-01.private-key.pem  ← privátní klíč GitHub App
```

> GitHub Actions Runner a Arduino CLI se stahují automaticky při prvním spuštění deploymentu do podsložky `runners/`.

---

## GitHub Actions Workflow

Tvůj repozitář musí obsahovat workflow, který:

- Běží na self-hosted runneru s labelem nastaveným v `Runner.Labels` (výchozí: `iot`)
- Je spouštěn přes `deployment_status` event
- Čte parametry z `github.event.deployment.payload.<Key>`

### Příklad: IoT (Arduino) workflow

```yaml
on:
  deployment_status:

jobs:
  flash-firmware:
    if: github.event.deployment_status.state == 'success'
    runs-on: [self-hosted, iot]
    environment: ${{ github.event.deployment.environment }}
    steps:
      - uses: actions/checkout@v4
      - name: Upload firmware
        run: arduino-cli upload --port ${{ github.event.deployment.payload.serial_port }} ...
```

### Příklad: .NET deployment se separátním CI buildem

CI workflow (`build.yml`) vytváří artefakt:

```yaml
name: Build
on:
  push:
    branches: [main, develop]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - run: dotnet publish -c Release -o publish/
      - uses: actions/upload-artifact@v4
        with:
          name: publish
          path: publish/
```

Deployment workflow (`deploy.yml`) — IoTDeploy předá v payloadu `artifact_run_id` (a `artifact_name`):

```yaml
on:
  deployment_status:

jobs:
  deploy:
    if: github.event.deployment_status.state == 'success'
    runs-on: [self-hosted, iot]
    environment: ${{ github.event.deployment.environment }}
    steps:
      - uses: actions/download-artifact@v4
        with:
          run-id: ${{ github.event.deployment.payload.artifact_run_id }}
          name:   ${{ github.event.deployment.payload.artifact_name }}
          github-token: ${{ secrets.GITHUB_TOKEN }}
          path: app
      - name: Deploy
        shell: pwsh
        run: |
          $target = "${{ github.event.deployment.payload.target_path }}"
          Copy-Item -Recurse -Force app\* $target
```

> Pozor: `actions/download-artifact@v4` s `run-id` z jiného workflow potřebuje token s `actions: read`. GitHub App, kterou IoTDeploy používá, má `Actions: Read & write`, což to pokrývá — `secrets.GITHUB_TOKEN` v deploymentu funguje.

---

## Použití aplikace

### GUI

1. **Spusť** `IoTDeployUI.exe`
2. Aplikace se připojí k GitHubu a načte repozitáře dostupné pro GitHub App
3. Vyber:
   - **Repository** – repozitář
   - **Branch** – větev
   - **Environment** – GitHub Environment (musí existovat v repozitáři)
   - **COM port** – volitelné, jen pro IoT flashování
   - **Use artifact** – volitelné, pro .NET deploye. Po zaškrtnutí dropdown nabídne posledních 10 úspěšných buildů na vybrané větvi (první položka = `Latest`).
4. Klikni **Deploy**
5. Sleduj průběh ve stavovém řádku
6. Pro zrušení klikni **Cancel**

### CLI

```
IoTDeploy.exe <repository> <branch> <environment> [<port>]
              [--useartifact <runId|latest>] [--artifact-name <name>] [--workflow <file>]
```

| Argument | Popis |
|----------|-------|
| `<repository>` | Jméno repozitáře (povinné, poziční) |
| `<branch>` | Větev (povinné, poziční) |
| `<environment>` | GitHub Environment (povinné, poziční) |
| `<port>` | COM port (**volitelné** 4. poziční). Pokud zadáno, předá se jako `serial_port`. |
| `--useartifact <id\|latest>` | Předá `artifact_run_id` v payloadu. `latest` se resolvuje těsně před triggerem na nejnovější úspěšný run s artefaktem na dané větvi. |
| `--artifact-name <name>` | Filtr na konkrétní jméno artefaktu (z `actions/upload-artifact`). Při `latest` resolve omezuje, v který run se má hledat. |
| `--workflow <file>` | Filtr na konkrétní CI workflow (`build.yml`). Použije se při `latest` resolve. |

**Příklady:**

```bash
# IoT (zpětně kompatibilní s předchozí verzí)
IoTDeploy.exe my-firmware main prod COM3

# .NET — latest build z větve main
IoTDeploy.exe my-app main staging --useartifact latest

# .NET — konkrétní run, jen artefakty se jménem 'publish' z workflow build.yml
IoTDeploy.exe my-app main staging --useartifact 12345678 --artifact-name publish --workflow build.yml
```

Při `--useartifact latest` zapíše IoTDeploy do logu konkrétní `run_id`, který se předal — auditní stopa pro případ, že potřebuješ vědět, jaký artefakt se nasadil.

Logy jsou ukládány do `%LOCALAPPDATA%\IoTDeploy\logs\` (tlačítko **Open Log** otevře aktuální log v Notepadu).

---

## Řešení problémů

| Problém | Řešení |
|---------|--------|
| `FileNotFoundException: appsettings.json` | Zkontroluj, že `appsettings.json` je ve stejné složce jako `.exe` |
| `FileNotFoundException: *.pem` | Zkontroluj název souboru s privátním klíčem – musí odpovídat vzoru `PemFilePattern` |
| `InvalidOperationException` při startu | Chybné `AppId`, `InstallationId` nebo neplatný privátní klíč |
| Repozitář se nezobrazuje | GitHub App není nainstalována do daného repozitáře (viz krok 4) |
| Workflow se nespustí (timeout) | Prodluž `WorkflowTimeoutMinutes`, zkontroluj, zda workflow existuje v repozitáři |
| Runner se nepřipojí | Zkontroluj, zda má GitHub App oprávnění `Administration: Read & write` |
| Arduino CLI nenajde zařízení | Zkontroluj COM port a zda není blokován jiným programem |
| `No successful run with artifact ... found` | Na zvolené větvi neexistuje úspěšný run s odpovídajícím artefaktem. Ověř, že CI workflow proběhl a `--artifact-name` / `--workflow` filtry odpovídají. |
| `--useartifact <id>` workflow stáhne 0 souborů | `actions/download-artifact@v4` v deploy workflow potřebuje `github-token` s `actions: read` (typicky `${{ secrets.GITHUB_TOKEN }}`). |
