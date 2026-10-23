namespace TechRiders.AgentDemo;

public static class Page
{
    public const string Html = """
        <!doctype html><html lang="es"><meta charset="utf-8">
        <title>Tech Riders — Demo offline</title>
        <style>body{font:18px system-ui;background:#101725;color:#eaf2ff;max-width:1100px;margin:40px auto}button,input{font:inherit;padding:10px;margin:8px}pre{white-space:pre-wrap;background:#1c2940;padding:20px;border-radius:12px}a{color:#8ecbff}.warning{color:#ffcf70}</style>
        <h1>Tu API ya no la van a consumir personas</h1>
        <p class="warning">DEMO determinista local · sin modelo · peticiones HTTP y MCP reales · datos sintéticos.</p>
        <blockquote>Busca los pedidos pendientes de Contoso, identifica cuál debería revisar primero y cancélalo si está duplicado.</blockquote>
        <p><a href="http://127.0.0.1:5101/swagger" target="_blank">1 · Swagger</a> · <a href="http://127.0.0.1:15888" target="_blank">4 · Aspire Dashboard</a></p>
        <button id="rest">2 · REST: fallo docente + lectura corregida</button>
        <button id="mcp">3 · MCP: descubrir y revisar</button>
        <p id="proposal"></p><div id="confirmation" hidden>
        <label>Confirmación humana explícita: <input id="typed" placeholder="CANCELAR ORD-1004" autocomplete="off"></label>
        <button id="confirm">Confirmar cancelación + retry</button><button id="deny">Denegar (sin escritura)</button></div>
        <p><a id="trace-link" hidden target="_blank">4 · Abrir esta traza en Aspire</a></p>
        <pre id="output">Preparado. Empieza con REST o descubre las tres tools MCP.</pre>
        <script>
        let review;
        const output=document.getElementById('output'), gate=document.getElementById('confirmation');
        async function call(url,body={}) {
          const response=await fetch(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
          const data=await response.json(); output.textContent=JSON.stringify(data,null,2);
          if(data.traceId){const link=document.getElementById('trace-link');link.href='http://127.0.0.1:15888/traces/detail/'+data.traceId;link.hidden=false}
          if(!response.ok) throw Error(data.detail||'Request failed'); return data;
        }
        async function safe(action){try{await action()}catch(e){output.textContent+='\n'+e.message}}
        document.getElementById('rest').onclick=()=>safe(()=>call('/demo/rest'));
        document.getElementById('mcp').onclick=()=>safe(async()=>{
          review=await call('/demo/mcp/review'); gate.hidden=!review.context.allowedActions.includes('Cancel');
          document.getElementById('typed').value='';
          document.getElementById('proposal').textContent='Propuesta: cancelar '+review.candidate.id+'. '+review.context.reasons.map(r=>r.description+' Relacionado: '+r.relatedOrderId).join(' ');
        });
        document.getElementById('confirm').onclick=()=>safe(async()=>{
          gate.hidden=true; await call('/demo/mcp/confirm',{runId:review.runId,confirmation:document.getElementById('typed').value});
        });
        document.getElementById('deny').onclick=()=>safe(async()=>{gate.hidden=true;await call('/demo/mcp/deny',{runId:review.runId,confirmation:''})});
        </script></html>
        """;
}
