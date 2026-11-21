# Evidencias de validación

Validación realizada el 21 de noviembre de 2026 sobre la implementación del commit `126f28d`.
Los cambios posteriores de la entrega agregan documentación y artefactos de revisión.
La implementación inicial se verificó en `feature/tp4-testing-documentacion`;
la rama final de entrega es `test/tp4-testing-documentacion`, de acuerdo con el formato exigido.

## Resultados locales

- Los dos archivos `.trx` de esta carpeta registran 24 y 15 pruebas aprobadas, respectivamente.
- `ejecucion-local.txt` conserva la salida de la ejecución con SDK .NET 8.0.428.
- `casos-ejecutados.csv` permite revisar las 39 ejecuciones individualmente.
- `resultados-locales.png` visualiza los totales leídos de los TRX.

## GitHub Actions

| Ejecución | Evento | Quality gate | Docker y despliegue |
| --- | --- | --- | --- |
| [44935540151](https://github.com/sinergia-ics/ICS2026-backend/actions/runs/44935540151) | push de la rama TP4 | success | Docker success; publicación y Azure omitidos por rama feature |
| [44935709155](https://github.com/sinergia-ics/ICS2026-backend/actions/runs/44935709155) | workflow_dispatch con demonstrate_failure=true | failure en test de stock cero | Job completo skipped por needs; sin pasos ejecutados |

`ci-exitosa.json` y `ci-bloqueada.json` son las respuestas de `gh run view --json status,conclusion,jobs,url`.
Los archivos TXT con los mismos prefijos son las salidas originales de `gh run view --log`.
Las carpetas `github-exitosa` y `github-bloqueada` preservan los artefactos descargados de ambas ejecuciones,
incluidos resultados TRX y cobertura. Las figuras PNG se generan desde esos registros y están identificadas
como visualizaciones, sin imitar capturas de la interfaz de GitHub.

La prueba negativa altera únicamente la copia efímera del runner. Se cambia la condición de stock inválido
de `<= 0` a `< 0`, por lo que la prueba para stock cero falla. Las fuentes de la rama mantienen `<= 0`.

## Comprobar la evidencia

```powershell
gh run view 44935540151 --repo sinergia-ics/ICS2026-backend
gh run view 44935709155 --repo sinergia-ics/ICS2026-backend
gh run view 44935709155 --repo sinergia-ics/ICS2026-backend --log-failed
```

Los logs, JSON y TRX son la evidencia primaria. Las figuras facilitan su lectura en el informe Word.
Las acciones de revisión del equipo y el historial de Trello no se reemplazan por estos archivos.
