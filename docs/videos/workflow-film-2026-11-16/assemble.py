"""Workflow-first film: real modal interactions, exact approved logo, existing score."""
import json,subprocess,sys
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from PIL import Image,ImageDraw,ImageFont
ROOT=Path('/home/brad/code/devcontext');WORK=ROOT/'dist/demos/workflow-film-2026-11-16';OUT=ROOT/'docs/videos/workflow-film-2026-11-16'
SOURCE=WORK/'raw/workflow-modals.webm';MUSIC=ROOT/'dist/demos/brand-film/music-original.mp3'
F='/usr/share/fonts/liberation/LiberationSans-Regular.ttf';B='/usr/share/fonts/liberation/LiberationSans-Bold.ttf'
logo=Image.open(WORK/'logo.png').convert('RGBA');logo=logo.crop(logo.getbbox())
def font(size,bold=False):return ImageFont.truetype(B if bold else F,size)
def text(d,xy,value,size=28,bold=False,color='white',anchor=None):d.text(xy,value,font=font(size,bold),fill=color,anchor=anchor)
def mark(im,xy,size):m=logo.resize((size,size),Image.Resampling.LANCZOS);im.paste(m,xy,m)
plan=[
 ('overview',5,'Build critical workflows.','Website enquiry → right person · real workflow editor'),
 ('code',10,'Open the code behind each step.','Inspect the validation job directly from the canvas.'),
 ('conditions',9,'Make every condition explicit.','Inspect AND conditions, payload fields and decision thresholds.'),
 ('ai-if',8,'Review the evidence with AI.','Open the AI Review question that selects the True or False path.'),
 ('subworkflow',4,'Compose reusable workflows.','Open the Routing Review sub-workflow and inspect its stages.'),
 ('ai-review',8,'Configure an independent review.','Inspect the AI evidence check and its review instructions.'),
 ('parallel',6,'Coordinate parallel branches.','Inspect each branch’s condition and the workflows it starts.'),
 ('browser',5,'Define browser actions.','Review the workspace, instructions and execution settings.'),
 ('limits',4,'Put bounds on execution.','Inspect retries, delay and timeout for a workflow step.'),
 ('yaml',4,'Keep the definition visible.','Open the workflow YAML alongside the visual editor.'),
]
for id in ['intro','outro','poster']:
 im=Image.new('RGBA',(1920,1080),(0,0,0,0));d=ImageDraw.Draw(im)
 # An opaque central field protects the exact logo and typography from generated motion.
 d.rectangle((410,165,1510,915),fill='black');mark(im,(882,280),156)
 text(d,(960,495),'PlaceContext',69,True,anchor='mm')
 text(d,(960,602),'CRITICAL WORKFLOWS.',43,True,anchor='mm')
 text(d,(960,658),'PEOPLE IN CONTROL.',43,True,anchor='mm')
 text(d,(960,760),'Open source. Self-hosted. Paid support.' if id=='outro' else 'Inside the Website Enquiry workflow',26,anchor='mm',color='#BDBDBD')
 im.save(WORK/'frames'/f'{id}.png')
 if id=='poster':bg=Image.new('RGB',(1920,1080),'black');bg.paste(im,mask=im.getchannel('A'));bg.save(OUT/'poster.jpg',quality=96)
for i,(id,duration,title,sub) in enumerate(plan,1):
 im=Image.new('RGB',(1920,1080),'black');d=ImageDraw.Draw(im);d.rectangle((159,43,1760,944),outline='#363636',width=1)
 text(d,(160,968),title,32,True);text(d,(160,1018),sub,20,color='#BDBDBD');mark(im,(1665,976),55);text(d,(1740,990),str(i).zfill(2),22,color='#BDBDBD');im.save(WORK/'frames'/f'{id}.png')
def run(args):subprocess.run([str(x) for x in args],check=True)
def probe(path):return json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-show_format','-of','json',str(path)]))
def encode(inputs,filters,duration,path):run(['ffmpeg','-v','error','-y','-filter_complex_threads','2',*inputs,'-filter_complex',filters,'-map','[v]','-t',duration,'-an','-c:v','libx264','-threads','2','-preset','slow','-crf','17','-pix_fmt','yuv420p','-r','30','-video_track_timescale','15360','-movflags','+faststart',path])
if '--frames-only' in sys.argv:raise SystemExit(0)
capture=json.loads((WORK/'capture.json').read_text());scenes={s['id']:s for s in capture['scenes']};assert all(s['error'] is None for s in capture['scenes'])
assert all(id in scenes for id,*_ in plan)
def render(item):
 id,duration,title,sub=item;s=scenes[id];source=WORK/s.get('source','raw/workflow-modals.webm');start=s['start']+.05;assert start+duration<=s['end'],(id,start,duration,s['end'])
 encode(['-loop','1','-framerate','30','-i',WORK/'frames'/f'{id}.png','-ss',start,'-i',source],f'[1:v]setpts=PTS-STARTPTS,fps=30,setsar=1[screen];[0:v][screen]overlay=160:44:shortest=1,setsar=1,fade=t=in:st=0:d=0.1,fade=t=out:st={duration-.1}:d=0.1[v]',duration,WORK/'parts'/f'{id}.mp4');print('Rendered',id,flush=True)
if '--code-pickup' in sys.argv:
 render(next(item for item in plan if item[0]=='code'));raise SystemExit(0)
if '--finish' not in sys.argv:
 with ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(render,plan))
if '--render-ui' in sys.argv:raise SystemExit(0)
for id,start,duration in ([] if '--finish' in sys.argv else [('intro',0,4),('outro',5,5)]):
 encode(['-ss',start,'-i',WORK/'motion-original.mp4','-loop','1','-framerate','30','-i',WORK/'frames'/f'{id}.png'],f'[0:v]setpts=PTS-STARTPTS,scale=1920:1080,fps=30,format=gray,lutyuv=y=if(gt(val\\,160)\\,235\\,16),format=yuv420p,setsar=1[plate];[plate][1:v]overlay=0:0:shortest=1,fade=t=in:st=0:d=0.25,fade=t=out:st={duration-.25}:d=0.25[v]',duration,WORK/'parts'/f'{id}.mp4')
timeline=[];cursor=0
for id,duration,title,sub in [('intro',4,'PlaceContext','Critical workflows. People in control.'),*plan,('outro',5,'PlaceContext','Open source. Self-hosted. Paid support.')]:
 source=WORK/scenes[id].get('source','raw/workflow-modals.webm') if id in scenes else WORK/'motion-original.mp4';source_start=scenes[id]['start']+.05 if id in scenes else (0 if id=='intro' else 5)
 timeline.append({'id':id,'start':cursor,'end':cursor+duration,'title':title,'description':sub,'source':str(source.relative_to(ROOT)),'sourceStart':source_start});cursor+=duration
assert cursor==72
playlist=WORK/'parts/playlist.txt';playlist.write_text(''.join(f"file '{s['id']}.mp4'\n" for s in timeline))
run(['ffmpeg','-v','error','-y','-f','concat','-safe','0','-i',playlist,'-c','copy','-movflags','+faststart',WORK/'picture.mp4'])
run(['ffmpeg','-v','error','-y','-i',WORK/'picture.mp4','-i',MUSIC,'-map','0:v','-map','1:a','-c:v','copy','-af','loudnorm=I=-18:TP=-1.5:LRA=9,afade=t=in:st=0:d=0.4,afade=t=out:st=70:d=2,apad','-t',72,'-c:a','aac','-b:a','256k','-ar','48000','-movflags','+faststart',OUT/'placecontext-workflows.mp4'])
(OUT/'edit.json').write_text(json.dumps(timeline,indent=2));(OUT/'capture.json').write_text(json.dumps(capture,indent=2))
def stamp(t):return f'{int(t//3600):02}:{int(t//60)%60:02}:{int(t)%60:02}.000'
(OUT/'placecontext-workflows.vtt').write_text('WEBVTT\n\n'+''.join(f'{i+1}\n{stamp(s["start"])} --> {stamp(s["end"])}\n{s["title"]}\n{s["description"]}\n\n' for i,s in enumerate(timeline)))
info=probe(OUT/'placecontext-workflows.mp4');v=next(s for s in info['streams'] if s['codec_type']=='video');a=next(s for s in info['streams'] if s['codec_type']=='audio')
assert (v['width'],v['height'],v['codec_name'],v['pix_fmt'])==(1920,1080,'h264','yuv420p');assert v['avg_frame_rate']=='30/1';assert a['codec_name']=='aac';assert abs(float(info['format']['duration'])-72)<.1
run(['ffmpeg','-v','error','-i',OUT/'placecontext-workflows.mp4','-f','null','-'])
(OUT/'export-checks.json').write_text(json.dumps({'duration':72,'dimensions':[1920,1080],'fps':30,'video':'h264','audio':'aac','decode':'passed','existingSoundtrack':str(MUSIC.relative_to(ROOT)),'bytes':(OUT/'placecontext-workflows.mp4').stat().st_size},indent=2));print('PASS: 72-second workflow film',flush=True)
