# Notas opcionales del ponente

El [README](../README.md) es la guía pública completa. El recorrido principal
usa Copilot real en VS Code; esta hoja no es un prerrequisito ni un runner.

## Preparación

- Compila, ejecuta tests y arranca una sola sesión del launcher.
- Prepara Swagger con `Get-DemoAccess.ps1 -Target Swagger -Open`.
- Abre Aspire con `Get-DemoAccess.ps1 -Target Aspire -Open`; protege la URL
  privada frente a capturas o proyecciones públicas.
- Comprueba la disponibilidad real de `orders` en el harness seleccionado.
- No confundas tener un servidor configurado con haber visto una llamada suya.
- Mantén la aprobación manual de herramientas de escritura.

## Demo 1

Swagger: GET de pendientes de `cus-contoso`. Verifica tres resultados.
No escribas antes de la revisión MCP.

## Demo 2

Pega el prompt REST exacto del README en Copilot. Muestra los comandos ejecutados,
la sonda deliberada incorrecta, su ProblemDetails y la consulta corregida.
No atribuyas al modelo el fallo que el prompt le ha pedido provocar.
La API también tiene capacidades REST: MCP no es necesario para compartir handlers.

## Demo 3

Pega solo la intención de negocio del README en una conversación nueva con
el mismo modelo y `orders` habilitado. No dictes tools, IDs ni el plan; deja
que el agente los deduzca o solicite una aclaración si falta una capacidad.
La tabla de resultados del README es para ti, no para pegarla al agente.
Expande las tools de `orders`,
muestra candidato y evidencia y espera antes de autorizar.
Confirma solo el ID de la revisión actual; revisa los argumentos de la escritura.
Tras el resultado, solicita un retry idéntico y comprueba estado por REST.
Si el agente no llama a la herramienta, no presentes su resumen como una llamada.

## Demo 4

Busca los traceId REST y las operaciones recientes del recurso `mcp`.
Muestra tool, handler, permisos, idempotencia y transacción según los spans
realmente presentes. No prometas una única traza para toda la conversación
ni razonamiento interno del modelo. Contrasta la escritura con el estado SQLite
visible por REST.

## Recuperación

- Sin Internet/Copilot: declara que cambias al **ensayo determinista opcional**
  AgentDemo, no que el modelo sigue ejecutándose.
- Revisión obsoleta o reset: vuelve a revisar y solicita confirmación nueva.
- Swagger 401: renueva acceso con el helper; si caducó la sesión, reinicia.
- MCP no disponible: enseña el error, revisa configuración/salida, no sustituyas
  silenciosamente un resultado MCP por REST.
- AppHost cae con `IOException` sobre `aspire-dcp*\kubeconfig`: compila sin
  `-NoBuild` (Aspire ≥ 13.3.0 lo corrige) o usa `-Mode Standalone`.
- Dashboard sin datos: ejecuta una lectura nueva y revisa recurso/filtros/logs.

## Ensayo opcional AgentDemo

Con `Start-Demos.ps1 -IncludeAgentDemo`, la página local en puerto 5103 conserva los botones REST, revisión MCP y
confirmación con retry. Sus decisiones son programadas, sin LLM. La traza
continua de ese runner no verifica la integración de Copilot.
`Rehearse-Demos.ps1` autoriza explícitamente reset/cancelación de datos sintéticos;
no lo ejecutes simultáneamente con una sesión interactiva.
