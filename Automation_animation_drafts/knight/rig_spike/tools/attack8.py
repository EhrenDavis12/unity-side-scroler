"""Overhead chop on the hybrid knight (original art, full skeleton, sheet arms), feet planted by leg IK."""
import json, math, os, numpy as np
RIG=os.environ.get("RIG","parts6")
import fk
from smooth import Clip
from ik import Legs, ang
from PIL import Image
sk=json.load(open(RIG+"/skeleton.json"))
fk.J={k:tuple(v) for k,v in sk["J"].items()}; fk.P=sk["P"]; ORDER=sk["ORDER"]; fk.ORDER=ORDER
CHAIN=["sword","forearm_sword","fist_sword","pauldron_sword"]      # upper arm stays under the pad
SWING_ORDER=[n for n in ORDER if n not in CHAIN+["fist_shield","shield"]]+["fist_shield","shield"]+CHAIN
Jn={k:np.array(v,float) for k,v in fk.J.items()}
_sw=np.array(Image.open(RIG+"/sword.png"))[...,3]>0; _ys,_xs=np.nonzero(_sw)
_d=(_xs-Jn["sword"][0])**2+(_ys-Jn["sword"][1])**2; tip=np.array([_xs[_d.argmax()],_ys[_d.argmax()]],float)   # blade tip
REST={"U":ang(Jn["forearm_sword"]-Jn["upper_arm_sword"])%360,"F":ang(Jn["fist_sword"]-Jn["forearm_sword"])%360,"S":ang(tip-Jn["sword"])%360}
def unwrap(v,ref): return ref+((v-ref+180)%360-180)
def arm(U,F,Sw,chest):
    dU=U-REST["U"]; dF=F-REST["F"]; dS=Sw-REST["S"]
    return {"upper_arm_sword":dU-chest,"forearm_sword":dF-dU,"fist_sword":0.0,"sword":dS-dF,
            "pauldron_sword":max(-60,min(60,0.6*dU))}
def key(t,name,U,F,Sw,chest,head,plume,sh_up,sh_fore,hips_ang,hips_off,chest_sx=1.0):
    a=arm(U,F,Sw,chest)
    a.update({"chest":chest,"head":head,"plume":plume,"upper_arm_shield":sh_up,"forearm_shield":sh_fore,
              "pauldron_shield":max(-60,min(60,0.6*sh_up)),"hips":hips_ang})
    return {"t":t,"name":name,"angles":{k:round(v,2) for k,v in a.items()},"offset":{"hips":list(hips_off)},
            "scale":{"chest":[chest_sx,1.0]} if chest_sx!=1 else {}}
R=(REST["U"],REST["F"],REST["S"])
# world directions (math deg, y up) continue smoothly: the arm lifts up IN FRONT and chops down the front
U0,F0,S0=R; U0=U0%360; F0=F0%360; S0=S0%360
K=[
 key(0.00,"rest",   U0,F0,S0,                 0, 0,  0,   0,  0,  0,(0,0)),
 key(0.16,"lift",   290,420,455,              3,-1, -4,  -6,  6,  1,(-2,1),0.98),
 key(0.30,"windup", 440,500,525,              7,-3, -9, -12, 12,  3,(-3,1),0.95),
 key(0.38,"hold",   445,510,540,              8,-3,-11, -13, 13,  3,(-3,1),0.94),
 key(0.51,"strike", 350,352,335,            -10, 5, 12,  18,-20, -5,(5,6),0.94),
 key(0.62,"follow", 300,298,275,            -13, 7, 17,  20,-22, -6,(6,7),0.95),
 key(1.02,"recover",U0,F0,S0,                -2, 1, -4,   2, -3, -1,(1,1)),
 key(1.32,"rest",   U0,F0,S0,                 0, 0,  0,   0,  0,  0,(0,0)),
]
js={"ik":{"legs":{"feet":"planted","knees":"out"}},"length":1.32,"interp":"monotone_cubic","swing_sort_window":[0.16,1.10],
    "lag":{"hips":-0.03,"chest":-0.02,"upper_arm_sword":0.0,"pauldron_sword":0.01,"forearm_sword":0.025,"fist_sword":0.04,"sword":0.05,
           "upper_arm_shield":0.02,"forearm_shield":0.04,"pauldron_shield":0.03,"head":0.035,"plume":0.08},"keys":K}
json.dump(js,open("attack8_keys.json","w"),indent=1)
clip=Clip(js); LEGS=Legs(fk.J)
def frame(x):
    p=clip.sample(x); p["angles"].update(LEGS.solve(p["angles"].get("hips",0.0),p["offset"].get("hips",(0,0))))
    w=js["swing_sort_window"]
    return fk.render(p["angles"],d=RIG,offset=p["offset"],scale=p["scale"],order=SWING_ORDER if w[0]<=x<=w[1] else ORDER)
if __name__=="__main__":
    print("rest dirs",{k:round(v,1) for k,v in REST.items()})
    crop=(100,0,924,940); fps=50; T=js["length"]
    fr=[frame(min(i/fps,T)).crop(crop).convert("RGB").resize((412,470),Image.LANCZOS) for i in range(int(T*fps)+15)]
    fr[0].save("attack8_preview.gif",save_all=True,append_images=fr[1:],duration=20,loop=0)
    s=Image.new("RGB",(412*7,470))
    for i,k in enumerate(K[:7]): s.paste(frame(k["t"]).crop(crop).convert("RGB").resize((412,470),Image.LANCZOS),(i*412,0))
    s.resize((s.width//2,s.height//2),Image.LANCZOS).save("attack8_keys.png")
