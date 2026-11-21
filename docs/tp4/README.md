# TP4 Testing automatizado y CI/CD

Entrega de Ingeniería y Calidad del Software 2026, UTN FRT, comisión 4K1.
Legajos del tablero: 58593, 58605, 50470, 58648 y 57658.

La implementación cubre exclusivamente el backend, de acuerdo con ambos PDF proporcionados.
Se parte de `origin/development` en `b0ac7fb`. Rama de entrega: `test/tp4-testing-documentacion`.

## Contenido

- [Informe técnico completo](informe.md) y `resolucion-tp4.docx`.
- [Matriz de trazabilidad](matriz-requisitos.md).
- [Evidencias verificables](evidencias/README.md): cinco capturas reales de GitHub Actions, TRX, resultados y registros.
- `tests/ECommerce.UnitTests`: 24 ejecuciones unitarias, xUnit, Moq y FluentAssertions.
- `tests/ECommerce.IntegrationTests`: 15 ejecuciones de integración/E2E, API real, JWT y SQLite.
- `.github/workflows/deploy-main.yml`: pruebas obligatorias antes de Docker y Azure.

## Reproducir

Instalar SDK .NET 8.0.400 o posterior de la familia 8.0. `global.json` selecciona el SDK adecuado.
No se necesita SQL Server, Azure, Docker, conexión a una base externa ni credenciales para los tests.
La primera restauración requiere acceso a NuGet.

```powershell
dotnet restore Dsw2025Tpi.sln
dotnet build Dsw2025Tpi.sln --no-restore --configuration Release
dotnet test Dsw2025Tpi.sln --no-build --configuration Release --logger trx --results-directory TestResults --collect:"XPlat Code Coverage"
```

Ejecuciones por nivel:

```powershell
dotnet test tests/ECommerce.UnitTests --configuration Release
dotnet test tests/ECommerce.IntegrationTests --configuration Release
dotnet test tests/ECommerce.IntegrationTests --configuration Release --filter "Category=E2E"
```

En esta PC el SDK de la tarea también está disponible en `D:/ics/.tools/dotnet/dotnet.exe`.
Si el SDK no está en PATH, usar esa ruta en lugar de `dotnet` y configurar
`DOTNET_CLI_HOME=D:/ics/.tools/dotnet-home` y `NUGET_PACKAGES=D:/ics/.tools/nuget`.
No usar `DOTNET_ROLL_FORWARD=Major`: la validación final se hizo con .NET 8.

## Demostrar el bloqueo

El workflow admite una ejecución manual con `demonstrate_failure=true`.
La única mutación cambia, dentro del checkout efímero del runner, la validación de stock de
`<= 0` a `< 0`. El caso unitario de stock cero debe fallar. El repositorio y la rama no se modifican.

```powershell
gh workflow run deploy-main.yml --repo sinergia-ics/ICS2026-backend --ref test/tp4-testing-documentacion -f demonstrate_failure=true
```

Resultado esperado y verificado: `run-tests=failure`; `build-and-deploy=skipped`.
No hay `continue-on-error` en los tests ni `always()` en el job dependiente. El uso de `always()`
se limita a conservar los resultados de la ejecución. Con el parámetro en `false`, la suite pasa.
En un PR o push se usa siempre el código original; la inyección sólo existe para la demostración manual.

## Publicación

Los PR y pushes a ramas `test/**` y `feature/tp4-*` ejecutan tests y construyen Docker sin secretos.
Sólo un push o ejecución manual sobre `main` o `development`, con todos los tests aprobados,
puede publicar y desplegar. Se conserva la configuración existente de Azure y Docker Hub.
Secretos requeridos: `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`, `APP_SERVICE_NAME` y
`AZURE_WEBAPP_PUBLISH_PROFILE`. Sus valores no se incluyen en la entrega.
La imagen se despliega por SHA del commit, para que la versión desplegada sea identificable.

El estado de las tarjetas y su tránsito hasta Done quedan a cargo del equipo, por indicación del usuario.
Esta entrega no inventa una revisión por otro integrante ni un historial de Trello.

## Regenerar el Word

El documento se genera con `scripts/generate_tp4_report.py` a partir de `informe.md` y las evidencias.
Requiere Python 3 y `python-docx`, `Pillow` y `pypdf`. El script acepta `--workspace-copy` para
guardar una copia adicional en la carpeta de trabajo. La revisión visual se realiza después de generarlo.

```powershell
python scripts/generate_tp4_report.py --workspace-copy D:/ics/resolucion-tp4.docx
```
