import json
from fk import render, ORDER, ORDER_SWING
from PIL import Image
# Overhead chop with a faked body twist. Knees only ever bend OUTWARD:
# thigh_left <= 0, shin_left >= 0, thigh_right >= 0, shin_right <= 0.
K=[
 (0.00,"rest",{},{},{}),
 (0.30,"windup",
  {"torso":6,"head":-2,"plume":-8,"upper_arm_sword":-150,"forearm_sword":20,"hand_sword":40,"pauldron_sword":-25,
   "upper_arm_shield":-18,"forearm_shield":20,"thigh_left":-4,"shin_left":5,"thigh_right":3,"shin_right":-4},
  {"pauldron_sword":(-8,-4),"upper_arm_sword":(-8,-4),"pauldron_shield":(8,2),"upper_arm_shield":(10,2),"head":(-3,0),"torso":(-4,2)},
  {"torso":(0.95,1.0)}),
 (0.38,"hold",
  {"torso":7,"head":-2,"plume":-10,"upper_arm_sword":-156,"forearm_sword":24,"hand_sword":46,"pauldron_sword":-27,
   "upper_arm_shield":-20,"forearm_shield":22,"thigh_left":-5,"shin_left":6,"thigh_right":4,"shin_right":-5},
  {"pauldron_sword":(-9,-4),"upper_arm_sword":(-9,-4),"pauldron_shield":(9,2),"upper_arm_shield":(11,2),"head":(-3,0),"torso":(-5,2)},
  {"torso":(0.94,1.0)}),
 (0.47,"strike",
  {"torso":-9,"head":4,"plume":12,"upper_arm_sword":-250,"forearm_sword":-4,"hand_sword":-10,"pauldron_sword":18,
   "upper_arm_shield":22,"forearm_shield":-30,"thigh_left":-12,"shin_left":15,"thigh_right":10,"shin_right":-13},
  {"pauldron_sword":(10,2),"upper_arm_sword":(10,2),"pauldron_shield":(-8,4),"upper_arm_shield":(-12,6),"head":(5,2),"torso":(8,4)},
  {"torso":(0.94,1.0)}),
 (0.57,"follow",
  {"torso":-12,"head":6,"plume":16,"upper_arm_sword":-298,"forearm_sword":10,"hand_sword":-20,"pauldron_sword":22,
   "upper_arm_shield":24,"forearm_shield":-32,"thigh_left":-14,"shin_left":17,"thigh_right":12,"shin_right":-15},
  {"pauldron_sword":(12,3),"upper_arm_sword":(12,3),"pauldron_shield":(-9,5),"upper_arm_shield":(-13,7),"head":(6,3),"torso":(10,6)},
  {"torso":(0.95,1.0)}),
 (0.98,"recover",
  {"torso":-2,"head":1,"plume":-4,"upper_arm_sword":-352,"forearm_sword":2,"hand_sword":-3,"pauldron_sword":2,
   "upper_arm_shield":3,"forearm_shield":-4,"thigh_left":-2,"shin_left":2,"thigh_right":2,"shin_right":-2},
  {"torso":(1,1)},{}),
 (1.25,"rest",{"upper_arm_sword":-360},{},{}),
]
EASE={"windup":"inout","hold":"linear","strike":"in","follow":"out","recover":"inout","rest":"inout"}
SWING=(0.38,0.98)
def ease(t,k):
    return t**3 if k=="in" else 1-(1-t)**3 if k=="out" else t*t*(3-2*t) if k=="inout" else t
def lerp_ch(a,b,u,rest):
    out={}
    for k in set(a)|set(b):
        x=a.get(k,rest); y=b.get(k,rest)
        out[k]=tuple(p+(q-p)*u for p,q in zip(x,y)) if isinstance(x,tuple) else x+(y-x)*u
    return out
def sample(t):
    for (t0,_,a,oa,sa),(t1,n1,b,ob,sb) in zip(K,K[1:]):
        if t0<=t<=t1:
            u=ease((t-t0)/(t1-t0),EASE[n1])
            return lerp_ch(a,b,u,0),lerp_ch(oa,ob,u,(0,0)),lerp_ch(sa,sb,u,(1,1))
    return {},{},{}
def frame(t):
    a,o,s=sample(t)
    return render(a,offset=o,scale=s,order=ORDER_SWING if SWING[0]<=t<=SWING[1] else ORDER)
if __name__=="__main__":
    for n,a,*_ in [(k[1],k[2]) for k in K]:
        assert a.get("thigh_left",0)<=0 and a.get("shin_left",0)>=0 and a.get("thigh_right",0)>=0 and a.get("shin_right",0)<=0, n
    fps=30; T=K[-1][0]; frames=[frame(min(i/fps,T)).crop((40,0,1000,960)).convert("RGB").resize((480,480),Image.LANCZOS) for i in range(int(T*fps)+10)]
    frames[0].save("attack2_preview.gif",save_all=True,append_images=frames[1:],duration=33,loop=0)
    s=Image.new("RGB",(480*6,480))
    for i,k in enumerate(K[:6]): s.paste(frame(k[0]).crop((40,0,1000,960)).convert("RGB").resize((480,480),Image.LANCZOS),(i*480,0))
    s.resize((1440,240),Image.LANCZOS).save("attack2_keys.png")
    js={"length":T,"swing_sort_window":list(SWING),"ease_into":EASE,"keys":[{"t":t,"name":n,"angles":a,"offset":{k:list(v) for k,v in o.items()},"scale":{k:list(v) for k,v in sc.items()}} for t,n,a,o,sc in K]}
    json.dump(js,open("attack2_keys.json","w"),indent=1)
