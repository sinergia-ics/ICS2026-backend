# Matriz de requisitos y pruebas

Los nombres de los métodos mantienen los escenarios del enunciado. Una teoría xUnit produce una
ejecución por conjunto de datos; por eso hay más ejecuciones que escenarios nominales.

| ID | Escenario obligatorio | Proyecto y método |
| --- | --- | --- |
| UT01 | Alta válida y una llamada a Add | UnitTests / AddProduct_WithValidData_ReturnsProductAndCallsRepositoryAdd |
| UT02 | SKU duplicado sin guardar | UnitTests / AddProduct_WithDuplicateSku_ThrowsDuplicatedEntityException |
| UT03 | Stock cero/negativo o precio negativo, antes del repositorio | UnitTests / AddProduct_WithInvalidStockOrPrice_ThrowsInvalidDataException |
| UT04 | Orden válida, total y descuento | UnitTests / CreateOrder_WithValidItemsAndStock_CalculatesTotalAndDecrementsProductStock |
| UT05 | Stock 2, compra 5, sin escrituras | UnitTests / CreateOrder_WhenProductStockIsInsufficient_ThrowsInsufficientStockException |
| UT06 | Cliente inexistente sin modificar productos | UnitTests / CreateOrder_WhenCustomerDoesNotExist_ThrowsCustomerNotFoundException |
| UT07 | Catálogo público sólo activo | UnitTests / GetProducts_WhenIncludeInactiveIsFalse_ReturnsOnlyActiveProducts |
| UT08 | Página 2, tamaño 5, total 15 | UnitTests / GetProducts_WithPaginationFilter_AppliesSkipAndTakeCorrectly |
| UT09 | Búsqueda por nombre, SKU o código sin distinguir mayúsculas | UnitTests / GetProducts_WithSearchTerm_FiltersByNameSkuOrInternalCode |
| IT01 | POST productos anónimo 401 y User 403 | IntegrationTests / AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden |
| IT02 | POST Admin 201 y fila SQLite | IntegrationTests / AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated |
| IT03 | SKU ya persistido, 400 admitido por consigna | IntegrationTests / AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode |
| IT04 | POST órdenes sin JWT, 401 | IntegrationTests / CreateOrder_WhenUnauthenticated_ReturnsUnauthorized |
| IT05 | Orden 201, Location, ítems y stock 10 a 7 | IntegrationTests / CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite |
| IT06 | Falta stock, 400 y JSON descriptivo | IntegrationTests / CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage |
| IT07 | GET público y contrato productItems/totalItems | IntegrationTests / GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure |
| IT08 | Filtros de query string sobre SQLite | IntegrationTests / GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite |
| IT09 | Admin incluye inactivos; público los excluye | IntegrationTests / GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts |
| E2E01 | Login, alta, catálogo, compra y lectura | IntegrationTests / E2E_LoginCreateProductPurchaseAndReadOrder_PreservesInventoryAndTotals |
| CI01 | Tests antes de Docker y deploy | deploy-main.yml / build-and-deploy.needs = run-tests |
| CI02 | Fallo real de test bloquea el job dependiente | Ejecución manual demonstrate_failure=true; JSON y logs en evidencias |
| DOC01 | Informe, resultados y evidencias | informe.md, resolucion-tp4.docx y evidencias/ |
| AG01 | Gestión Sprint 3 | Tarjetas existentes; movimientos y cierre reservados al equipo por pedido del usuario |

## Pruebas adicionales

Unitarias: precio cero admitido, SKU/nombre vacío, paginación inválida, cantidad cero/negativa,
orden vacía, fallo en un ítem posterior y producto repetido con demanda acumulada mayor al stock.

Integración: catálogo vacío 204, JWT adulterado 401, rechazo de un ítem posterior sin cambiar stock,
y rollback real en SQLite ante una violación de clave foránea al insertar una orden.

## Relación con Trello

| Tarjeta | Evidencia técnica |
| --- | --- |
| [CARD-TP4-01](https://trello.com/c/6XPQuppJ) | Dos proyectos en tests/, referencias, paquetes, solución y fábrica SQLite |
| [CARD-TP4-02](https://trello.com/c/RzmRynX7) | UT01 a UT09, patrón AAA, Moq con Verify y escenarios negativos |
| [CARD-TP4-03](https://trello.com/c/yQOXqp2u) | IT01 a IT09, JWT real, SQLite, aislamiento y E2E01 |
| [CARD-TP4-04](https://trello.com/c/R3YprlAA) | CI01 y CI02, ejecución positiva y negativa con logs preservados |

La tarjeta 03 menciona `totalItems`; el código previo devolvía `total`. Se agrega `totalItems`
como alias de lectura y se conserva `total` para no romper el frontend existente.
El enunciado acepta 409 o 400 ante SKU duplicado: se verifica el 400 del middleware actual.
La guía ofrece xUnit; se prioriza esa consigna frente a la recomendación genérica de MSTest.
