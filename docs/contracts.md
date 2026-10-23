# Contratos de la demo

## Consulta tradicional

`GET /orders?customerId=cus-contoso&status=Pending`

Estados exactos: `Pending`, `Processing`, `Shipped`, `Cancelled`. Cliente estable: `cus-contoso`; `Contoso` no es un ID. El DTO CRUD solo expone ID, cliente, estado, importe y prioridad: **no permite inferir duplicación**.

`GET /orders?customerId=Contoso&status=pending` responde `400 application/problem+json`, `code=INVALID_ORDER_STATUS`, valores permitidos y `correlationId`. Se valida estado primero para que el fallo docente sea estable. La corrección es una lectura adicional, no un bucle de reintentos imprevisible.

CRUD: GET colección/detalle, POST con ID generado, PUT y DELETE. POST/PUT exigen `OrderInput` completo, prioridad exacta Normal/High e importe positivo; solo crean/editan Pending. Seeds protegidos frente a DELETE. Las muestras están en `samples\orders.http`.

## Capacidades reutilizadas

| Application | REST | MCP |
|---|---|---|
| GetPendingOrders | GET `/capabilities/pending-orders?customerId=cus-contoso` | `get_pending_orders(customerId)` |
| GetOrderReviewContext | GET `/capabilities/orders/{id}/review-context` | `get_order_review_context(orderId)` |
| CancelOrder | POST `/orders/{id}/cancellation` + `Idempotency-Key` | `cancel_order(orderId, reviewToken, confirmed=false, idempotencyKey="")` |

MCP usa **Streamable HTTP real** en `/mcp` y el cliente oficial 2.0.0, incluyendo inicialización, `tools/list` y `tools/call`. Se publican exactamente tres tools con descripción de intención, argumentos y efectos. Los errores de negocio se devuelven como contenido JSON `error: {code,detail,status,correlationId}`; el cliente los convierte en excepciones explícitas y no interpreta el error como resultado de negocio.

Copilot conecta por stdio a `TechRiders.CopilotBridge`, que reenvía el catálogo
y los resultados originales al HTTP protegido. No llama a REST ni implementa
reglas de duplicidad. Lee el bearer actual de `.local/orders-session.json` en cada
petición; no se incluyen secretos en los archivos MCP ni inputs incompatibles
con el harness portable. El emisor mantiene su clave solo en el entorno de
procesos; el bearer de cliente está en un archivo local con ACL restringida,
excluido de Git y borrado al detener. Son credenciales de demo, no un IdP.

## Revisión y cancelación

1. Leer candidatos ordenados con `reviewRank`.
2. Obtener contexto para **el ID seleccionado**. Devuelve razones, relacionado, acciones, versión y token firmado.
3. Mostrar ID y evidencia. Confirmar explícitamente ese pedido, o denegar.
4. Enviar el mismo ID/token con `confirmed=true` y clave `demo:` única (máximo 128 caracteres).
5. El servidor valida identidad/permiso, confirmación, clave, estado Pending, firma, expiración, versiones y duplicidad actual. El cliente no decide la duplicidad final.
6. Guardar transición y resultado idempotente en la misma transacción.

Primera respuesta: `{orderId:"ORD-1004",previousStatus:"Pending",currentStatus:"Cancelled",alreadyApplied:false}`.
Retry idéntico: mismo resultado de negocio y `alreadyApplied:true`, sin otra mutación. La marca de replay es la única diferencia intencional.
Misma clave y distinto payload o identidad: conflicto. Reutilizar un token antiguo con una clave nueva no autoriza cancelar un estado ya modificado.

## Identidad y permisos

- Lecturas: `orders.read`.
- CRUD permitido: `orders.write`.
- Cancelación: `orders.cancel`.
- Bearer HMAC temporal emitido solo por el launcher/cliente local de confianza; claves solo en variables de entorno.
- No existe parámetro `actor`, `scopes` ni cabecera alternativa que eleve permisos.
- No se debe usar el emisor Demo ni `confirmed` como solución de identidad/consentimiento para producción.
- UI local confiable con protección Host/Origin; un proceso local con acceso al entorno del launcher está dentro del límite de confianza de esta demostración.

## Errores relevantes

| HTTP/status de negocio MCP | Código | Consecuencia |
|---|---|---|
| 400 | INVALID_ORDER_STATUS / INVALID_CUSTOMER_ID / INVALID_ORDER | Corregir contrato; no escribe |
| 401 | UNAUTHENTICATED o challenge del adaptador | Identidad ausente/inválida; no escribe |
| 403 | ORDER_CANCELLATION_NOT_ALLOWED | Permiso insuficiente; no escribe |
| 404 | ORDER_NOT_FOUND | ID desconocido |
| 409 | CONFIRMATION_REQUIRED | Falta confirmación; no escribe |
| 409 | INVALID_ORDER_STATE | No Pending; no escribe |
| 409 | INVALID_SIGNED_CONTEXT / STALE_REVIEW_CONTEXT | Token inválido, caducado o evidencia/versiones cambiadas; revisar otra vez |
| 409 | IDEMPOTENCY_CONFLICT | No reutilizar la clave para otro payload |
| 409 | PROTECTED_DEMO_ORDER | DELETE no es un atajo para cancelar seeds |

Sin permiso o sin confirmación se rechaza **antes** de modificar la base de datos. Las pruebas inspeccionan versiones además del estado para demostrar ausencia de escrituras.
