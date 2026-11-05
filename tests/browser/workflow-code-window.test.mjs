import {readFileSync} from 'node:fs';
import {runInNewContext} from 'node:vm';
import assert from 'node:assert/strict';
import {test} from 'node:test';

for (const kind of ['code window', 'workflow dialog']) {
test(`${kind} supports pointer and keyboard movement, viewport bounds, and cleanup`, () => {
const listeners={},headerListeners={},style={},events={};
const header={addEventListener:(name,fn)=>headerListeners[name]=fn};
let disconnected=false,aborted=false;
const handle={addEventListener:(name,fn)=>listeners[name]=fn,setPointerCapture(){},focus(){}};
const element={style,offsetWidth:400,offsetHeight:300,querySelector:()=>handle,getBoundingClientRect:()=>({left:parseFloat(style.left)||100,top:parseFloat(style.top)||100})};
const context={innerWidth:1000,innerHeight:800,window:{addEventListener:(name,fn)=>events[name]=fn},AbortController:class{signal={};abort(){aborted=true;}},ResizeObserver:class{constructor(cb){this.cb=cb;}observe(){}disconnect(){disconnected=true;}}};
runInNewContext(readFileSync(new URL('../../src/PlaceContext.Host/Components/Shared/WorkflowCodeWindow.razor.js', import.meta.url),'utf8').replaceAll('export function','function')+'\nthis.api={attach,detach,attachDialog,detachDialog};',context);
handle.closest=selector=>selector === '.dcmodal' ? element : header;
if (kind === 'workflow dialog') {
  context.api.attachDialog(handle);
  context.api.attachDialog(handle);
} else {
  context.api.attach(element);
}
// The title and empty header space drag; the close button remains a normal button.
headerListeners.pointerdown({button:0,target:{closest:()=>({})},preventDefault(){throw new Error('close button must not start dragging');}});
headerListeners.pointermove({clientX:950,clientY:900});
assert.equal(style.left,undefined);
headerListeners.pointerdown({button:0,target:{closest:()=>null},clientX:120,clientY:130,pointerId:1,preventDefault(){}});
headerListeners.pointermove({clientX:950,clientY:900});
assert.equal(style.left,'600px');assert.equal(style.top,'500px');
assert.equal(style.transform,'none');
headerListeners.lostpointercapture();headerListeners.pointermove({clientX:10,clientY:10});assert.equal(style.left,'600px');
listeners.keydown({key:'ArrowLeft',preventDefault(){}});assert.equal(style.left,'580px');
context.innerWidth=500;events.resize();assert.equal(style.left,'100px');
handle.closest=()=>null;
if (kind === 'workflow dialog') context.api.detachDialog(handle);
else context.api.detach(element);
assert.ok(disconnected&&aborted);
});
}
