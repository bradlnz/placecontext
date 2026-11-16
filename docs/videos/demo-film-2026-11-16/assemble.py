"""Compose actual recorded UI with Higgsfield motion/music and the approved SVG mark."""
import json, subprocess
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from PIL import Image, ImageDraw, ImageFont
ROOT=Path('/home/brad/code/devcontext');WORK=ROOT/'dist/demos/demo-film-2026-11-16';OUT=ROOT/'docs/videos/demo-film-2026-11-16';CLIPS=ROOT/'docs/videos/platform-recordings-2026-11-16/clips'
FONT='/usr/share/fonts/liberation/LiberationSans-Regular.ttf';BOLD='/usr/share/fonts/liberation/LiberationSans-Bold.ttf'
# Preserve the selected mark; remove transparent canvas margins only for placement.
logo=Image.open(WORK/'logo.png').convert('RGBA');logo=logo.crop(logo.getbbox())
def font(size,bold=False):return ImageFont.truetype(BOLD if bold else FONT,size)
def text(draw,xy,value,size=28,bold=False,color='#FFFFFF'):draw.text(xy,value,font=font(size,bold),fill=color)
def mark(im,xy,size):im.paste(logo.resize((size,size),Image.Resampling.LANCZOS),xy,logo.resize((size,size),Image.Resampling.LANCZOS))
def frame(id,title,sub='',chapter=0):
 im=Image.new('RGB',(1920,1080),'black');d=ImageDraw.Draw(im)
 if id in ['intro','outro','poster']:
  mark(im,(160,150),100);text(d,(290,173),'PlaceContext',43,True)
  if id=='outro':
   text(d,(160,380),'BUILD YOUR NEXT',84,True);text(d,(160,475),'CRITICAL WORKFLOW.',84,True)
   text(d,(160,645),'Open source. Self-hosted. Paid support.',30)
   text(d,(160,840),'Explore the platform and documentation',25,color='#B8B8B8')
  else:
   text(d,(160,380),'CRITICAL WORKFLOWS.',84,True);text(d,(160,475),'PEOPLE IN CONTROL.',84,True)
   text(d,(160,640),'The open-source agentic workflow platform',30)
   text(d,(160,840),'PLATFORM DEMO  /  WEBSITE ENQUIRY PROJECT',20,color='#B8B8B8')
  d.line((160,780,1760,780),fill='#444444',width=1)
 else:
  d.rectangle((159,55,1760,956),outline='#363636',width=1)
  text(d,(160,975),title,31,True);text(d,(160,1022),sub,19,color='#B8B8B8')
  mark(im,(1662,980),55);text(d,(1737,993),f'{chapter:02}',22,color='#B8B8B8')
 path=WORK/'frames'/f'{id}.png';im.save(path);return path
plan=[
 ('dashboard','01-dashboard',.45,6,'Your project. In view.','Live dashboard and workflow access.'),
 ('chat-question','02-chat',7.7,5,'Ask in plain language.','A real question about a duplicate invoice payment.'),
 ('chat-answer','02-chat',31.55,7,'Inspect the answer and its evidence.','This chat response cites team data from a saved 15 November run.'),
 ('data','03-data',4.15,7,'Work from your data.','Query the team roster, then filter for Billing.'),
 ('agents','06-agents',.6,5,'Give each agent a clear role.','Triage, review, handoff and follow-up specialists.'),
 ('canvas','04-workflow-editor',.6,5,'Design the workflow visually.','Connect jobs, agents, decisions and browser actions.'),
 ('options','04-workflow-editor',15.1,4,'Choose the step the work needs.','Human approvals, agents, queries, browser workspaces and more.'),
 ('review','04-workflow-editor',23.4,4,'Review before action.','Inspect the AI review question and branching conditions.'),
 ('limits','04-workflow-editor',30.55,3,'Set explicit limits.','Configure retries, delay and timeout for a step.'),
 ('markup','04-workflow-editor',42.2,4,'Edit the definition directly.','A YAML draft edit, shown without deploying it.'),
 ('triage-input','07-triage-execution',8.15,4,'Run the Triage workflow.','A new standalone run using the duplicate-payment question.'),
 ('triage-running','07-triage-execution',15.7,8,'Watch execution as it happens.','Query data → agent authorization → classification.'),
 ('triage-result','07-triage-execution',35.2,8,'A result you can inspect.','Triage succeeded: Maya Chen, Billing. Recorded 16 November 2026.'),
 ('safety','05-fresh-workflow-run',14.55,7,'When checks fail, execution stops.','Separate full-workflow attempt: 67% safety confidence; 90% required.'),
]
frame('intro','');frame('outro','');frame('poster','')
for i,(id,clip,start,duration,title,sub) in enumerate(plan,1):frame(id,title,sub,i)

def run(args):subprocess.run([str(a) for a in args],check=True)
def probe(path):return json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-show_format','-of','json',str(path)]))
def encode(inputs,filters,duration,path):
 run(['ffmpeg','-hide_banner','-v','error','-y','-filter_complex_threads','2',*inputs,'-filter_complex',filters,'-map','[v]','-t',duration,'-an','-c:v','libx264','-threads','2','-preset','fast','-crf','18','-pix_fmt','yuv420p','-r','30','-video_track_timescale','15360','-movflags','+faststart',path])
def render(item):
 id,clip,start,duration,title,sub=item;source=CLIPS/(clip+'.mp4');assert start+duration<=float(probe(source)['format']['duration'])
 encode(['-loop','1','-framerate','30','-i',WORK/'frames'/f'{id}.png','-ss',start,'-i',source],f'[1:v]setpts=PTS-STARTPTS,fps=30,setsar=1[screen];[0:v][screen]overlay=160:56:shortest=1,setsar=1,fade=t=in:st=0:d=0.10,fade=t=out:st={duration-.1}:d=0.10[v]',duration,WORK/'parts'/f'{id}.mp4');print('Rendered',id,flush=True)
if __name__=='__main__':
 import sys
 if '--frames-only' in sys.argv:raise SystemExit(0)
 if '--finish' not in sys.argv:
  with ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(render,plan))
 if '--render-ui' in sys.argv:raise SystemExit(0)
 for id,duration in ([] if '--finish' in sys.argv else [('intro',3),('outro',7)]):
  encode(['-loop','1','-framerate','30','-i',WORK/'frames'/f'{id}.png'],f'[0:v]fps=30,setsar=1,fade=t=in:st=0:d=0.25,fade=t=out:st={duration-.4}:d=0.4[v]',duration,WORK/'parts'/f'{id}.mp4')
 # Threshold the generated abstract plate to enforce the requested no-gradient style.
 encode(['-ss','0.7','-i',WORK/'motion-original.mp4'],'[0:v]setpts=PTS-STARTPTS,scale=1920:1080,fps=30,format=gray,lutyuv=y=if(gt(val\\,160)\\,235\\,16),format=yuv420p,setsar=1,fade=t=in:st=0:d=0.1,fade=t=out:st=2.8:d=0.2[v]',3,WORK/'parts/motion.mp4')
 timeline=[];cursor=0
 def add(id,duration,source,start,title,sub):
  global cursor
  timeline.append({'id':id,'start':cursor,'end':cursor+duration,'source':str(source.relative_to(ROOT)),'sourceStart':start,'title':title,'description':sub});cursor+=duration
 add('motion',3,WORK/'motion-original.mp4',.7,'PlaceContext','[Instrumental music] Abstract workflow paths on black.')
 add('intro',3,WORK/'frames/intro.png',0,'Critical workflows. People in control.','The open-source agentic workflow platform.')
 for id,clip,start,duration,title,sub in plan:add(id,duration,CLIPS/(clip+'.mp4'),start,title,sub)
 add('outro',7,WORK/'frames/outro.png',0,'Build your next critical workflow.','Open source. Self-hosted. Paid support.')
 assert cursor==90,cursor
 playlist=WORK/'parts/playlist.txt';playlist.write_text(''.join(f"file '{t['id']}.mp4'\n" for t in timeline))
 run(['ffmpeg','-v','error','-y','-f','concat','-safe','0','-i',playlist,'-c','copy','-movflags','+faststart',WORK/'picture.mp4'])
 run(['ffmpeg','-v','error','-y','-i',WORK/'picture.mp4','-i',WORK/'music-original.m4a','-map','0:v','-map','1:a','-c:v','copy','-af','loudnorm=I=-18:TP=-1.5:LRA=9,afade=t=in:st=0:d=0.6,afade=t=out:st=86:d=4,apad','-t',90,'-c:a','aac','-b:a','192k','-ar','48000','-movflags','+faststart',OUT/'placecontext-demo.mp4'])
 (OUT/'edit.json').write_text(json.dumps(timeline,indent=2))
 def stamp(t):return f'{int(t//3600):02}:{int(t//60)%60:02}:{int(t)%60:02}.000'
 (OUT/'placecontext-demo.vtt').write_text('WEBVTT\n\n'+''.join(f'{i+1}\n{stamp(t["start"])} --> {stamp(t["end"])}\n{t["title"]}\n{t["description"]}\n\n' for i,t in enumerate(timeline)))
 Image.open(WORK/'frames/poster.png').save(OUT/'poster.jpg',quality=95)
 info=probe(OUT/'placecontext-demo.mp4');v=next(s for s in info['streams'] if s['codec_type']=='video');a=next(s for s in info['streams'] if s['codec_type']=='audio')
 assert (v['width'],v['height'],v['codec_name'],v['pix_fmt'])==(1920,1080,'h264','yuv420p');assert v['avg_frame_rate']=='30/1';assert abs(float(info['format']['duration'])-90)<.1;assert a['codec_name']=='aac'
 run(['ffmpeg','-v','error','-i',OUT/'placecontext-demo.mp4','-f','null','-'])
 (OUT/'export-checks.json').write_text(json.dumps({'duration':90,'size':[1920,1080],'fps':30,'video':'h264','audio':'aac','decode':'passed','bytes':(OUT/'placecontext-demo.mp4').stat().st_size},indent=2));print('PASS: 90-second film exported and decoded',flush=True)
