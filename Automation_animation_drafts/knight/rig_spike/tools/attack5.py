"""Overhead chop for the sheet-built rig (joints: neck, shoulders, elbows, wrists, waist, knees, ankles).
The sword arm is authored as WORLD directions (math degrees, y up, + = counter-clockwise) and converted
to local angles, so each pose reads as 'where the bone points' rather than stacked offsets."""
import json
import fk
from smooth import Clip
from ik import Legs
from PIL import Image
sk=json.load(open("parts4/skeleton.json"))
fk.J={k:tuple(v) for k,v in sk["J"].items()}; fk.P=sk["P"]; ORDER=sk["ORDER"]; fk.ORDER=ORDER
CHAIN=["forearm_sword","sword","fist_sword","pad_sword"]   # upper arm stays behind the chest
SWING_ORDER=[n for n in ORDER if n not in CHAIN+["fist_shield","shield"]]+["fist_shield","shield"]+CHAIN
RW=sk["rest_world_rotation"]   # pieces are drawn pointing down (arms) or up (sword)
REST={"upper_arm_sword":270+RW["upper_arm_sword"],"forearm_sword":270+RW["forearm_sword"],"sword":90+RW["sword"]}
R_U,R_F,R_S=REST["upper_arm_sword"],REST["forearm_sword"],REST["sword"]
def arm(U,F,Sw,chest=0.0,fist=None):
    """world dirs -> local angles. chest: the chest's own local angle (the arm hangs off it)."""
    dU=U-REST["upper_arm_sword"]; dF=F-REST["forearm_sword"]; dS=Sw-REST["sword"]
    fist_d = dF if fist is None else fist            # the fist follows the forearm unless told otherwise
    return {"upper_arm_sword":dU-chest,"forearm_sword":dF-dU,"fist_sword":fist_d-dF,"sword":dS-fist_d,
            "pad_sword":max(-30,min(30,0.3*(dU)))-chest*0}
def legs(bend):
    """knees OUT only (horse-stance direction); feet stay flat on the ground via the ankles."""
    tl,sl=-bend,bend*1.4; tr,sr=bend,-bend*1.4
    return {"thigh_left":tl,"shin_left":sl,"boot_left":-(tl+sl),"thigh_right":tr,"shin_right":sr,"boot_right":-(tr+sr)}
def key(t,name,U,F,Sw,chest,head,plume,shield_up,shield_fore,bend,chest_sx=1.0,off=None):
    a=arm(U,F,Sw,chest)
    a.update({"chest":chest,"head":head,"plume":plume,"upper_arm_shield":shield_up,"forearm_shield":shield_fore,"pad_shield":0.3*shield_up})
    return {"t":t,"name":name,"angles":{k:round(v,2) for k,v in a.items()},"offset":off or {},"scale":{"chest":[chest_sx,1.0]} if chest_sx!=1 else {}}
K=[
 key(0.00,"rest",   R_U,R_F,R_S,  0, 0,  0,   0,  0, 0),
 key(0.16,"lift",   290,420,455,  3,-1, -4,  -6,  8, 2, 0.98),
 key(0.30,"windup", 440,500,520,  7,-3, -9, -12, 14, 4, 0.95, {"hips":[-6,2]}),
 key(0.38,"hold",   445,510,535,  8,-3,-11, -13, 15, 5, 0.94, {"hips":[-7,2]}),
 key(0.51,"strike", 350,350,340,-10, 5, 12,  20,-25,10, 0.94, {"hips":[12,10]}),
 key(0.62,"follow", 305,300,280,-13, 7, 17,  22,-28,12, 0.95, {"hips":[14,12]}),
 key(1.02,"recover",R_U,R_F,R_S, -2, 1, -4,   2, -3, 2, 1.0,  {"hips":[1,1]}),
 key(1.32,"rest",   R_U,R_F,R_S,  0, 0,  0,   0,  0, 0),
]
# whole-body tilt about the waist (feet stay planted; the leg IK absorbs it)
for k,h in zip(K,[0,1,3,3,-5,-6,-1,0]): k["angles"]["hips"]=h
js={"ik":{"legs":{"feet":"planted","knees":"out","slots":["thigh_left","shin_left","boot_left","thigh_right","shin_right","boot_right"]}},"length":1.32,"interp":"monotone_cubic","swing_sort_window":[0.38,1.02],
    "lag":{"hips":-0.03,"chest":-0.02,"thigh_left":-0.03,"shin_left":-0.03,"boot_left":-0.03,"thigh_right":-0.03,"shin_right":-0.03,"boot_right":-0.03,
           "upper_arm_sword":0.0,"pad_sword":0.015,"forearm_sword":0.025,"fist_sword":0.04,"sword":0.05,
           "upper_arm_shield":0.02,"forearm_shield":0.04,"pad_shield":0.03,"head":0.035,"plume":0.08},
    "keys":K}
json.dump(js,open("attack5_keys.json","w"),indent=1)
clip=Clip(js)
LEGS=Legs(fk.J)
def frame(x):
    p=clip.sample(x); w=js["swing_sort_window"]
    p["angles"].update(LEGS.solve(p["angles"].get("hips",0.0),p["offset"].get("hips",(0,0))))
    return fk.render(p["angles"],d="parts4",offset=p["offset"],scale=p["scale"],order=SWING_ORDER if w[0]<=x<=w[1] else ORDER)
if __name__=="__main__":
    crop=(150,100,874,960); fps=50; T=js["length"]
    fr=[frame(min(i/fps,T)).crop(crop).convert("RGB").resize((362,430),Image.LANCZOS) for i in range(int(T*fps)+15)]
    fr[0].save("attack5_preview.gif",save_all=True,append_images=fr[1:],duration=20,loop=0)
    s=Image.new("RGB",(362*7,430))
    for i,k in enumerate(K[:7]): s.paste(frame(k["t"]).crop(crop).convert("RGB").resize((362,430),Image.LANCZOS),(i*362,0))
    s.resize((s.width//2,s.height//2),Image.LANCZOS).save("attack5_keys.png")
