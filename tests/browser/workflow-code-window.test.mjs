import {readFileSync} from 'node:fs';
import {runInNewContext} from 'node:vm';
import assert from 'node:assert/strict';
import {test} from 'node:test';

test('code windows support pointer and keyboard movement, viewport bounds, and cleanup', () => {
const listeners={},style={},events={};
let disconnected=false,aborted=false;
const handle={addEventListener:(name,fn)=>listeners[name]=fn,setPointerCapture(){},focus(){}};
const element={style,offsetWidth:400,offsetHeight:300,querySelector:()=>handle,getBoundingClientRect:()=>({left:parseFloat(style.left)||100,top:parseFloat(style.top)||100})};
const context={innerWidth:1000,innerHeight:800,window:{addEventListener:(name,fn)=>events[name]=fn},AbortController:class{signal={};abort(){aborted=true;}},ResizeObserver:class{constructor(cb){this.cb=cb;}observe(){}disconnect(){disconnected=true;}}};
runInNewContext(readFileSync(new URL('../../src/PlaceContext.Host/Components/Shared/WorkflowCodeWindow.razor.js', import.meta.url),'utf8').replaceAll('export function','function')+'\nthis.api={attach,detach};',context);
context.api.attach(element);
listeners.pointerdown({button:0,clientX:120,clientY:130,pointerId:1,preventDefault(){}});
listeners.pointermove({clientX:950,clientY:900});
assert.equal(style.left,'600px');assert.equal(style.top,'500px');
assert.equal(style.transform,'none');
listeners.lostpointercapture();listeners.pointermove({clientX:10,clientY:10});assert.equal(style.left,'600px');
listeners.keydown({key:'ArrowLeft',preventDefault(){}});assert.equal(style.left,'580px');
context.innerWidth=500;events.resize();assert.equal(style.left,'100px');
context.api.detach(element);assert.ok(disconnected&&aborted);
});
