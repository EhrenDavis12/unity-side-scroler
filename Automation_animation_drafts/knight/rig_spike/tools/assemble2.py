"""Hybrid knight: the ORIGINAL art (head, plume, chest, hips, pads, legs, angled boots, sword, shield)
on the full skeleton (neck, shoulders, elbows, wrists, waist, knees, ankles), with clean longer arms
from the parts sheet. Everything is baked to full-canvas rest PNGs in the original image's coordinates."""
import json, math, numpy as np
from PIL import Image
from split_arms import ball, under
P3="parts3/"; SP="sheetparts/"; OUT="parts5/"
ys,xs=np.mgrid[0:1024,0:1024]
def arr(p): return np.array(Image.open(p).convert("RGBA"))
def save(a,n): Image.fromarray(a,"RGBA").save(OUT+n+".png")
def take(a,m): b=a.copy(); b[~m]=0; return b
J={}; PARENT={}
# ---- original pieces ------------------------------------------------------------------
for n in ("head","head_blink","plume","pauldron_sword","pauldron_shield","pelvis"):
    Image.open(P3+n+".png").save(OUT+n+".png")
torso=arr(P3+"torso.png"); op=torso[...,3]>0
save(take(torso,op&(ys<582)),"chest"); save(take(torso,op&(ys>=551)),"hips")      # overlap under the belt
RAW_LEG={}
for side in ("left","right"):
    t=arr(P3+f"thigh_{side}.png"); sh=arr(P3+f"shin_{side}.png")
    # rebuild from the original leg pixels (no baked balls): thigh px + shin px minus the old knee ball
    base=arr("parts/_whole.png"); leg=arr("parts/leg_"+side+".png") if False else None
    RAW_LEG[side]=(t,sh)
from PIL import Image as _I
orig=np.array(_I.open("parts/_whole.png").convert("RGBA"))
legs_px={"left":np.array(_I.open("parts/leg_left.png").convert("RGBA")),"right":np.array(_I.open("parts/leg_right.png").convert("RGBA"))}
pel=np.array(_I.open("parts/pelvis.png").convert("RGBA"))
SPLIT={"left":dict(knee=lambda: ys<775+(xs-380)*0.08, ankle=lambda: ys>=812+(xs-360)*0.15, kj=(422,768), aj=(410,826), hip=(445,668)),
       "right":dict(knee=lambda: ys<781-(xs-560)*0.11, ankle=lambda: ys>=832-(xs-550)*0.12, kj=(594,772), aj=(600,834), hip=(560,668))}
for side,S_ in SPLIT.items():
    L=legs_px[side]; o=L[...,3]>0
    thigh_sel=o&S_["knee"](); boot_sel=o&S_["ankle"](); shin_sel=o&~thigh_sel&~boot_sel
    thigh=take(L,thigh_sel)
    hx=(xs<500) if side=="left" else (xs>=500)
    thigh[(pel[...,3]>0)&hx]=pel[(pel[...,3]>0)&hx]            # black trouser from the old pelvis cut
    save(np.array(under(thigh,ball(*S_["hip"],30))),f"thigh_{side}")
    save(np.array(under(take(L,shin_sel),ball(*S_["kj"],22))),f"shin_{side}"); J[f"shin_{side}"]=S_["kj"]
    save(np.array(under(take(L,boot_sel),ball(*S_["aj"],16))),f"boot_{side}")
    J[f"boot_{side}"]=S_["aj"]
J.update({"hips":(500,570),"chest":(500,570),"head":(495,400),"plume":(470,130),"pelvis":(502,668),
          "pauldron_sword":(395,448),"pauldron_shield":(603,448),
          "thigh_left":(445,668),"thigh_right":(560,668)})
PARENT.update({"hips":None,"chest":"hips","head":"chest","plume":"head","pelvis":"hips",
               "thigh_left":"pelvis","shin_left":"thigh_left","boot_left":"shin_left",
               "thigh_right":"pelvis","shin_right":"thigh_right","boot_right":"shin_right",
               "pauldron_sword":"chest","pauldron_shield":"chest"})
# ---- sheet arms, placed in canvas space ------------------------------------------------
SCALE=1.28
def place(src, pj, at, angle, flip=False):
    """piece `src` with its joint `pj` (piece px) put at canvas point `at`, rotated `angle` (+ = CCW)"""
    im=Image.open(SP+src+".png").convert("RGBA")
    if flip: im=im.transpose(Image.FLIP_LEFT_RIGHT); pj=(im.width-pj[0],pj[1])
    a=math.radians(angle); c,s=math.cos(a),math.sin(a)
    M=np.array([[c*SCALE, s*SCALE],[-s*SCALE, c*SCALE]])            # piece -> canvas (y down, + = CCW on screen)
    T=np.array(at,float)-M@np.array(pj,float)
    Mi=np.linalg.inv(M); ti=-Mi@T
    full=im.transform((1024,1024),Image.AFFINE,(Mi[0,0],Mi[0,1],ti[0],Mi[1,0],Mi[1,1],ti[1]),resample=Image.BICUBIC)
    return full, (lambda p: tuple((M@np.array(p,float)+T).round(1)))
def arm(side, shoulder, a_up, a_fore, a_fist, up, fore, fist):
    img,f=place(up,(30,12),shoulder,a_up);                 elbow=f((30,74))
    img.save(OUT+f"upper_arm_{side}.png")
    img,f=place(fore,(35,14),elbow,a_up+a_fore);          wrist=f((35,100))
    img=under(np.array(img),ball(int(elbow[0]),int(elbow[1]),int(23*SCALE))); img.save(OUT+f"forearm_{side}.png")
    img,f=place(fist,(35,10),wrist,a_up+a_fore+a_fist);   grip=f((35,33))
    img=under(np.array(img),ball(int(wrist[0]),int(wrist[1]),int(11*SCALE))); img.save(OUT+f"fist_{side}.png")
    J.update({f"upper_arm_{side}":shoulder,f"forearm_{side}":elbow,f"fist_{side}":wrist})
    PARENT.update({f"upper_arm_{side}":"chest",f"forearm_{side}":f"upper_arm_{side}",f"fist_{side}":f"forearm_{side}"})
    return grip
grip_s=arm("sword",(395,452),-30,-4,8,"c10","c16","c18")
grip_h=arm("shield",(603,452),-12,100,-14,"c11","c17","c19")
def move(n,src_pt,dst_pt,parent):
    im=Image.open(P3+n+".png"); dx,dy=dst_pt[0]-src_pt[0],dst_pt[1]-src_pt[1]
    im.rotate(0,translate=(dx,dy)).save(OUT+n+".png"); J[n]=tuple(dst_pt); PARENT[n]=parent
wrist=J["fist_sword"]; ow=(334,566)                     # wrist on the original art
fore_dir=math.degrees(math.atan2(-(wrist[1]-J["forearm_sword"][1]),wrist[0]-J["forearm_sword"][0]))
turn=fore_dir-247.0                                        # original forearm pointed ~247 deg
for n,src in (("fist_sword","parts/hand_sword.png"),("sword","parts/sword.png")):
    im=Image.open(src).convert("RGBA").rotate(turn,center=ow,translate=(wrist[0]-ow[0],wrist[1]-ow[1]),resample=Image.BICUBIC)
    if n=="fist_sword": im=under(np.array(im),ball(int(wrist[0]),int(wrist[1]),int(12*SCALE)))
    im.save(OUT+n+".png")
g=np.array([318-ow[0],600-ow[1]],float); r=math.radians(turn)
J["sword"]=(wrist[0]+g[0]*math.cos(r)+g[1]*math.sin(r), wrist[1]-g[0]*math.sin(r)+g[1]*math.cos(r)); PARENT["sword"]="fist_sword"
move("shield",(686,520),grip_h,"fist_shield")
ORDER=["plume","upper_arm_shield","forearm_shield","pelvis","boot_right","shin_right","thigh_right","boot_left","shin_left","thigh_left",
       "chest","hips","head","upper_arm_sword","pauldron_shield","sword","forearm_sword","fist_sword","pauldron_sword","fist_shield","shield"]
json.dump({"J":J,"P":PARENT,"ORDER":ORDER},open(OUT+"skeleton.json","w"),indent=1)
out=Image.new("RGBA",(1024,1024),(25,25,35,255))
for n in ORDER: out.alpha_composite(Image.open(OUT+n+".png"))
out.save("hybrid_rest.png")
