using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PlaceContext.Application.Build;

namespace PlaceContext.Host.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("artifact-share")]
public sealed class WorkflowDemoController(IWorkflowDemoShares shares) : ControllerBase
{
    [HttpGet("/share/workflows/{token}")]
    public async Task<IActionResult> Get(string token)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var demo = await shares.ResolveAsync(token, HttpContext.RequestAborted);
        if (demo is null)
        {
            return NotFound();
        }
        var script = "const frames=" + JsonSerializer.Serialize(demo.Frames) + ";" + """
            const slider=document.getElementById('position'), image=document.getElementById('frame'), caption=document.getElementById('caption'), play=document.getElementById('play');
            let index=0,timer;
            function show(){
                const frame=frames[index]; if(!frame)return;
                slider.value=index;
                image.hidden=!frame.Image;
                if(frame.Image)image.src='data:image/png;base64,'+frame.Image;
                image.alt=frame.Description;
                caption.textContent=(index+1)+' / '+frames.length+' · '+(frame.ElapsedMilliseconds/1000).toFixed(1)+'s · '+frame.Description+(frame.Image?'':' · Screenshot unavailable');
                slider.setAttribute('aria-valuetext',caption.textContent);
            }
            function stop(){clearInterval(timer);timer=null;play.textContent='Play replay';}
            document.getElementById('previous').onclick=()=>{stop();index=Math.max(0,index-1);show();};
            document.getElementById('next').onclick=()=>{stop();index=Math.min(frames.length-1,index+1);show();};
            slider.oninput=()=>{stop();index=Number(slider.value);show();};
            play.onclick=()=>{if(timer){stop();return;}if(index===frames.length-1)index=0;show();play.textContent='Pause replay';timer=setInterval(()=>{if(index>=frames.length-1){stop();return;}index++;show();},1000);};
            if(!frames.length)document.getElementById('replay').hidden=true;
            slider.max=Math.max(0,frames.length-1);show();
            """;
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        Response.Headers["Content-Security-Policy"] = $"sandbox allow-scripts; default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'sha256-{hash}'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";
        return Content($$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{WebUtility.HtmlEncode(demo.Title)}} — Workflow demo</title>
            <style>
            body{margin:0;background:#f6f7f9;color:#182235;font:15px/1.6 system-ui,sans-serif}main{max-width:1200px;margin:auto;padding:32px 20px}h1{font-size:28px;margin:6px 0}h2{font-size:18px}small{color:#576176}section{background:white;border:1px solid #dfe4ec;border-radius:14px;padding:24px;margin-top:22px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:14px/1.6 ui-monospace,monospace}img{width:100%;height:65vh;object-fit:contain;background:#101724;border-radius:8px}button{padding:10px 16px;border:1px solid #ced5df;border-radius:8px;background:white;cursor:pointer}nav{display:flex;gap:8px;align-items:center;flex-wrap:wrap}input{flex:1;min-width:150px}p{overflow-wrap:anywhere}[hidden]{display:none!important}
            </style></head><body><main><small>PlaceContext · Read-only workflow demo</small><h1>{{WebUtility.HtmlEncode(demo.Title)}}</h1>
            <p>Status: {{WebUtility.HtmlEncode(demo.Status)}} · Snapshot {{demo.CreatedAt:yyyy-MM-dd HH:mm}} UTC</p>
            <small>This shared snapshot expires {{demo.ExpiresAt:yyyy-MM-dd HH:mm}} UTC. It grants no workspace or live browser access.</small>
            <section><h2>Shared outcome</h2><pre>{{WebUtility.HtmlEncode(demo.Outcome)}}</pre></section>
            <section id="replay"><h2>Visual replay</h2><img id="frame" alt="Recorded browser window"><p id="caption" aria-live="polite"></p>
            <nav aria-label="Replay controls"><button id="previous">Previous</button><button id="play">Play replay</button><button id="next">Next</button><input id="position" type="range" min="0" value="0" aria-label="Replay position"></nav><small>Recorded decision frames · one frame per second</small></section>
            </main><script>{{script}}</script></body></html>
            """, "text/html; charset=utf-8");
    }
}
