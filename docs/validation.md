# Validación local realizada

## Validación de la copia pública (2026-10-23)

- Copia independiente con código, pruebas, scripts, muestras y documentación de
  la demo; sin credenciales, datos de sesión ni artefactos de compilación.
- `dotnet build TechRiders.slnx --verbosity quiet`: **0 warnings y 0 errores**.
- `dotnet test tests\TechRiders.Tests\TechRiders.Tests.csproj --no-build --verbosity quiet`:
  **78/78 pruebas correctas**, sin omitidas.
- `Rehearse-Demos.ps1 -NoBuild` y `Rehearse-Demos.ps1 -Mode Aspire -NoBuild`:
  ambos correctos sobre esta copia, con REST, MCP y retry idempotente reales.
  Sin archivos de acceso ni registro de procesos al terminar.
- Los siete scripts PowerShell y los cuatro JSON de configuración raíz/editor
  pasan sus parsers; enlaces locales Markdown y bloques de código comprobados.
- Las dos configuraciones MCP publicadas contienen únicamente `orders`.
  Las pruebas ejercitan ambas configuraciones contra el servidor real.
- No se ha validado una conversación desde la UI de Copilot: los ensayos
  automáticos no llaman a modelos.

Las secciones siguientes recogen el historial de validación de la implementación.

## Comandos reproducibles

Desde la raíz del repositorio clonado, PowerShell 7+:

```powershell
dotnet build TechRiders.slnx --verbosity quiet
dotnet test tests\TechRiders.Tests\TechRiders.Tests.csproj --no-build --verbosity quiet
.\scripts\Rehearse-Demos.ps1 -NoBuild
.\scripts\Rehearse-Demos.ps1 -Mode Aspire -NoBuild
```

El ensayo es una autorización explícita para resetear y cancelar **datos sintéticos**. No requiere ninguna confirmación interactiva adicional porque la orden dedicada incluye ese consentimiento; las rutas normales conservan confirmación humana real y segura por defecto.

## Resultados

### Refactor de capas y controllers

- Baseline antes de cambiar código: **53/53**. Tras el refactor:
  **78/78**, sin omitidas; build completo con **0 warnings y 0 errores**.
- API migrada a `OrdersController` y `OrderCapabilitiesController`, con
  autorización de ASP.NET y los mismos handlers que MCP. DTOs, interfaces,
  validadores y casos de uso separados por responsabilidad; registro DI separado
  de autenticación/firma. No se añaden paquetes ni una nueva capa de mediación.
- Comparación estructural del OpenAPI generado antes/después: **0 diferencias**
  (sin contar el orden de las claves JSON). Se conservan rutas, métodos, nombres
  de operación, tags, esquemas, obligatoriedad, formatos y seguridad Bearer.
- 25 pruebas adicionales: contrato OpenAPI; query strings vacíos, espacios y
  valores repetidos; prioridad de errores de negocio; JSON inválido/ausente;
  autenticación antes del body binding; confirmación, idempotencia y prueba
  firmada; HTTP 405 y compatibilidad `/orders/`; rollback atómico y detección
  explícita de documentos SQLite `null`.
- La suite completa vuelve a comprobar el bridge stdio desde ambas
  configuraciones reales, MCP HTTP, permisos, evidencia, concurrencia, retry,
  caducidad y rotación de sesión. Sin llamadas a modelos.
- Ensayos reales tras recompilar: **Standalone 9,3 s** y **Aspire 35,9 s**.
  Ambos pasan por OpenAPI, 400 intencional, revisión MCP, cancelación confirmada
  y retry idempotente. Aspire permanece en **13.3.5**; no se relajan auth ni hooks.
- Se restauraron los seeds con `Reset-Demo.ps1 -Confirm`; sin listeners de la
  demo, registro de procesos ni archivos de acceso al terminar.
- TR se consultó solo como referencia de controllers/DI; no se modificó.
  No se verificó la selección de herramientas desde la UI de Copilot:
  el ensayo automático sigue siendo AgentDemo determinista, no una sesión LLM.

### Corrección del arranque Aspire (kubeconfig de DCP)

- Síntoma: `Start-Demos.ps1` → *apphost terminó antes de estar preparado*;
  `apphost.stderr.log`: `AggregateException` → `IOException` (sharing violation,
  HResult `0x80070020`) al leer `%TEMP%\aspire-dcp*\kubeconfig` en
  `k8s.KubernetesClientConfiguration.LoadKubeConfigAsync` ←
  `Aspire.Hosting.Dcp.KubernetesService.EnsureKubernetesAsync`.
- Causa raíz: en Aspire 13.1.0 la política Polly de lectura del kubeconfig solo
  reintentaba `KubeConfigException`/`YamlException`; si DCP mantenía el archivo
  abierto mientras lo escribía, el `IOException` abortaba el host. Upstream añadió
  `.Handle<IOException>()` en dotnet/aspire#14557, publicado desde 13.3.0.
- Corrección: `Aspire.AppHost.Sdk` y `Aspire.Hosting.AppHost` 13.1.0 → 13.3.5
  (última 13.3.x; build sin warnings). Sin reintentos propios ni `catch` en el AppHost.
- Reproducción determinista con un archivo kubeconfig bloqueado en exclusiva
  1,5 s y la `KubernetesService` real: 13.1.0 falla con ese `IOException`;
  13.3.5 lo reintenta y carga la configuración.
- `AspireDependencyTests` (2 nuevas) fallan con 13.1.0 y pasan con 13.3.5.
  Suite completa: **53/53**.
- `Rehearse-Demos.ps1 -Mode Aspire -NoBuild`: 6 ejecuciones consecutivas
  correctas (45–53 s), con `TRACE_ID`, sin puertos residuales ni registro de
  procesos; después se restauraron los seeds con `Reset-Demo.ps1 -Confirm`.

### Integración Copilot añadida

- `dotnet build TechRiders.slnx --verbosity quiet`: 0 warnings, 0 errores.
- `dotnet test tests\TechRiders.Tests\TechRiders.Tests.csproj --no-build --verbosity quiet`:
  **51/51 pruebas**, sin omitidas (39 existentes y 12 nuevas).
- Las nuevas pruebas arrancan el bridge stdio leyendo cada configuración
  de producción (`.mcp.json` y `.vscode/mcp.json`), contra Kestrel real.
  Comprueban igualdad del catálogo y metadatos, llamadas de lectura/evidencia,
  rechazo sin confirmación, cancelación autorizada de datos sintéticos y retry.
- Se comprueban recarga de credenciales en el bridge ya abierto, permiso
  insuficiente, eliminación de sesión, destinos no locales/caducidad y helper
  REST real con traceId presente en los logs del servidor y sin bearer en salida.
- Se inspeccionan spans reales `tool.cancel_order`, `CancelOrder`, `idempotency`
  y `database.transaction`; los tests no llaman a ningún modelo.
- El helper REST se prueba con OpenAPI 200, colección 200 y ProblemDetails 400.
  Se corrigió la decodificación UTF-8 de `application/problem+json` cuando
  PowerShell entrega el cuerpo como `byte[]`, en vez de mostrar números.
- Arranque Aspire real y login privado verificados: HTTP 200, cookies de sesión,
  bearer API válido y ACL del archivo limitada al usuario actual/SYSTEM.
  El helper Swagger copió exactamente el bearer actual sin imprimirlo; después
  de la prueba se vació el portapapeles.
- En el navegador del dashboard se abrieron trazas recién generadas: REST con
  `ListOrders → authorization/database.transaction` y MCP con
  `tool.get_pending_orders → GetPendingOrders → authorization/database.transaction`.
  Son spans del servidor exportados por OTLP; no una vista del razonamiento de Copilot.
- Rotación real entre lanzamientos: bearer anterior 401, nuevo 200 y lectura MCP
  autenticada correcta; los archivos de acceso desaparecen al detener.
  Los ensayos deterministas Standalone y Aspire siguen pasando. Se dejaron
  los seeds restaurados, los seis puertos libres y ningún servicio en marcha.
- Se verificó que el arranque principal Aspire solo levanta API/MCP, sin listener
  AgentDemo en 5103. `-IncludeAgentDemo` o `-Run` habilitan el recurso de ensayo;
  los ensayos automáticos lo activan en ambos modos. Se corrigió la carrera de
  borrado del registro entre `Stop-Demos.ps1` y el `finally` del launcher.
- Enlaces locales Markdown, fences, tres archivos JSON y sintaxis de cinco scripts
  PowerShell comprobados. El enlace oficial externo respondió HTTP 200. Los
  diagramas se redujeron a flowcharts verticales; no había un parser Mermaid
  local disponible y no se usó el validador externo que requiere inicio de sesión.
- **No se ha validado la selección de herramientas ni una llamada desde Chat
  en la interfaz real de VS Code.** La prueba de protocolo no sustituye esa
  comprobación manual. Los pasos de verificación están en el README.

### Evidencia previa del ensayo AgentDemo (no Copilot)

- Solución completa compilada con SDK 10.0.401, **0 warnings y 0 errores**; NuGet Audit no se desactiva.
- Suite anterior: **39/39 pruebas xUnit**, sin omitidas; reemplazada por el total de la actualización anterior.
- Las pruebas de integración levantan Kestrel real para Api, Mcp y AgentDemo en puertos efímeros con SQLite separado. No usan respuestas HTTP grabadas ni servidores MCP falsos.
- Reglas de ranking/evidencia; contratos 400; identidades anónimas/forjadas/read-only; scopes cliente ignorados; confirmación ausente/falsa/denegada; Shipped; token inválido/caducado/de otro pedido; evidencia/versión propia y relacionada obsoletas; idempotencia normal/conflictiva/concurrente; replay no autorizado; REST y MCP comparten datos/handler/idempotencia; CRUD sin bypass; reset acotado; Host/Origin; propagación W3C.
- Ensayo offline Standalone completo: OpenAPI real → HTTP 400 → lectura corregida → SDK MCP oficial `tools/list`/`tools/call` → revisión ORD-1004 → confirmación explícita → Cancelled → retry `alreadyApplied=true`.
- Ensayo Aspire completo (entonces con AppHost 13.1.0, ahora 13.3.5), dashboard oficial y tres recursos sanos, **sin Docker**. Se ejecuta el runner del recurso AgentDemo con exportación OTLP configurada por Aspire.
- Última validación consecutiva, sin pausas manuales: **Aspire 32,2 s → Standalone 10,2 s**, ambos correctos, sin puertos residuales. Se corrigió y verificó la espera de liberación de sockets al cerrar los hijos DCP. Después se restauraron los seeds a Pending para la siguiente sesión.
- Navegador real: pantalla AgentDemo, tool discovery/revisión, escritura de `CANCELAR ORD-1004`, confirmación y dos respuestas idempotentes verificadas.
- Dashboard protegido abierto con su token **efímero generado para esa sesión**. Se abrió la traza `48ae7dd44d929e42908cf2d1b2a20d82` y se comprobó el árbol AgentDemo → HTTP/MCP → Mcp → GetOrderReviewContext/CancelOrder → authorization/idempotency/database.transaction. Revisión y confirmación compartían el ID. La captura textual de esa validación no es una traza precocinada: los datos viven en memoria del dashboard y desaparecen al cerrar la sesión; se genera otra traza al repetir.
- Bindings observados: `127.0.0.1` para API 5101, MCP 5102, AgentDemo 5103, dashboard 15888 y OTLP 18889.

## Límites explícitos

- **AgentDemo `--live` no implementado/configurado.** El flujo principal ahora usa Copilot externo en VS Code, no ese runner. Las validaciones automáticas no invocan modelos y no atribuyen decisiones deterministas a uno.
- **Identidad Demo, no producción.** Secreto aleatorio por lanzamiento, entorno de procesos de confianza, HMAC y permisos comprobados en servidor. No se accedió a credenciales existentes.
- **Sin despliegues ni servicios de pago.** Publicar el código no despliega los
  servicios locales ni valida una conversación real de Copilot.
