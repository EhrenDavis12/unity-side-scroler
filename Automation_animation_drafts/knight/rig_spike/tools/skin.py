"""Option 1 preview: the ORIGINAL knight in a few layers, bent by bones (linear blend skinning),
the way Unity's 2D Animation sprite skinning does it. Rigid props (sword, shield, head, plume) ride bones."""
import json, math, numpy as np
from PIL import Image
from scipy import ndimage
from scipy.interpolate import griddata
from ik import Legs
def A(p): return np.array(Image.open(p).convert("RGBA"))
# ---------------- layers (full 1024 canvas, original art) ----------------
parts={n:A(f"parts/{n}.png") for n in ["torso","pelvis","leg_left","leg_right","arm_sword","hand_sword","sword","arm_shield","shield","head","plume"]}
blink=A("parts/head_blink.png")
body=np.zeros_like(parts["torso"])
# hidden shoulders: black caps from the AI's arm-less redraw, only outside the original torso, near each shoulder
na=np.array(Image.open("noarms_aligned.png").convert("RGB")).astype(int); g=na[...,1]-np.maximum(na[...,0],na[...,2])
ys,xs=np.mgrid[0:1024,0:1024]
zone=((xs-400)**2+(ys-455)**2<55**2)|((xs-600)**2+(ys-455)**2<55**2)
cap=zone&(g<60)&(na.sum(-1)<200)
body[cap,:3]=na[cap]; body[cap,3]=255
for n in ["pelvis","leg_left","leg_right","torso"]:
    a=parts[n]; m=a[...,3]>0; body[m]=a[m]
arm_s=np.zeros_like(body)
for n in ["arm_sword","hand_sword"]:
    a=parts[n]; m=a[...,3]>0; arm_s[m]=a[m]
LAYERS={"body":body,"arm_sword":arm_s,"arm_shield":parts["arm_shield"]}
# ---------------- skeleton (canvas px, rest) ----------------
J={"hips":(502,668),"chest":(500,640),"neck":(495,400),"plume":(470,130),
   "shoulder_s":(395,448),"elbow_s":(352,506),"wrist_s":(328,575),"grip":(318,600),
   "shoulder_h":(603,448),"elbow_h":(626,493),"wrist_h":(655,520),
   "thigh_left":(445,668),"shin_left":(415,732),"boot_left":(410,826),
   "thigh_right":(560,668),"shin_right":(590,732),"boot_right":(600,834)}
J={k:np.array(v,float) for k,v in J.items()}
# bone: (joint it rotates about, end point, parent bone)
BONES={"hips":("hips",(502,720),None),"chest":("chest","neck","hips"),"head":("neck",(495,300),"chest"),"plume":("plume",(300,100),"head"),
 "upper_arm_sword":("shoulder_s","elbow_s","chest"),"forearm_sword":("elbow_s","wrist_s","upper_arm_sword"),"hand_sword":("wrist_s","grip","forearm_sword"),
 "upper_arm_shield":("shoulder_h","elbow_h","chest"),"forearm_shield":("elbow_h","wrist_h","upper_arm_shield"),
 "thigh_left":("thigh_left","shin_left","hips"),"shin_left":("shin_left","boot_left","thigh_left"),"boot_left":("boot_left",(370,870),"shin_left"),
 "thigh_right":("thigh_right","shin_right","hips"),"shin_right":("shin_right","boot_right","thigh_right"),"boot_right":("boot_right",(645,875),"shin_right")}
def P(x): return J[x] if isinstance(x,str) else np.array(x,float)
# which bones may bend each layer (the pauldron blends with the chest via "chest")
LAYER_BONES={"body":["hips","chest","thigh_left","shin_left","boot_left","thigh_right","shin_right","boot_right"],
             "arm_sword":["chest","upper_arm_sword","forearm_sword","hand_sword"],
             "arm_shield":["chest","upper_arm_shield","forearm_shield"]}
SEG_OVERRIDE={("arm_sword","chest"):((395,430),(470,440)),("arm_shield","chest"):((603,430),(530,440))}   # a short chest stub under each pauldron
# ---------------- mesh + weights ----------------
def seg_dist(p,a,b):
    ab=b-a; t=np.clip(((p-a)@ab)/(ab@ab),0,1); return np.linalg.norm(p-(a+t[:,None]*ab),axis=1)
MESH={}
for L,img in LAYERS.items():
    m=ndimage.binary_dilation(img[...,3]>0,iterations=3)
    yy,xx=np.nonzero(m[::5,::5]); pts=np.stack([xx*5,yy*5],1).astype(float)
    ey,ex=np.nonzero(m&~ndimage.binary_erosion(m)); pts=np.vstack([pts,np.stack([ex,ey],1)[::2].astype(float)])
    ds=[]
    for b in LAYER_BONES[L]:
        a_,e_=SEG_OVERRIDE.get((L,b),(BONES[b][0],BONES[b][1])); ds.append(seg_dist(pts,P(a_),P(e_)))
    D=np.stack(ds,1); W=1/(D+4)**4; W/=W.sum(1,keepdims=True)
    MESH[L]=(pts,W)
# ---------------- pose ----------------
def local(a,c):    # rotation by a deg (+ = counter-clockwise on screen) about c
    r=math.radians(a); C,S_=math.cos(r),math.sin(r); R=np.array([[C,S_],[-S_,C]])
    M=np.eye(3); M[:2,:2]=R; M[:2,2]=c-R@c; return M
def world(angles,offset):
    Wm={}
    def w(b):
        if b in Wm: return Wm[b]
        j,_,par=BONES[b]; M=local(angles.get(b,0.0),P(j))
        if b=="hips": T=np.eye(3); T[:2,2]=offset.get("hips",(0,0)); M=T@M
        Wm[b]=(w(par) if par else np.eye(3))@M; return Wm[b]
    for b in BONES: w(b)
    return Wm
def deform(L,Wm):
    img=LAYERS[L]; pts,Wt=MESH[L]; ph=np.hstack([pts,np.ones((len(pts),1))])
    out_pts=np.zeros_like(pts)
    for i,b in enumerate(LAYER_BONES[L]): out_pts+=Wt[:,i:i+1]*(ph@Wm[b].T)[:,:2]
    x0,y0=np.floor(out_pts.min(0)).astype(int)-2; x1,y1=np.ceil(out_pts.max(0)).astype(int)+2
    x0,y0=max(x0,0),max(y0,0); x1,y1=min(x1,1023),min(y1,1023)
    gy,gx=np.mgrid[y0:y1+1,x0:x1+1]
    src=griddata(out_pts,pts,(gx,gy),method="linear")
    ok=~np.isnan(src[...,0]); res=np.zeros((1024,1024,4),float)
    sx=np.where(ok,src[...,0],0); sy=np.where(ok,src[...,1],0)
    for c in range(4):
        ch=ndimage.map_coordinates(img[...,c].astype(float)*(img[...,3]/255.0 if c<3 else 1),[sy,sx],order=1)
        res[y0:y1+1,x0:x1+1,c]=np.where(ok,ch,0)
    a=res[...,3:4]/255.0; res[...,:3]=np.where(a>0,res[...,:3]/np.maximum(a,1e-3),0)
    return Image.fromarray(np.clip(res,0,255).astype(np.uint8))
def rigid(img,M):
    Mi=np.linalg.inv(M); return Image.fromarray(img).transform((1024,1024),Image.AFFINE,tuple(Mi[0])+tuple(Mi[1]),resample=Image.BICUBIC)
LEGJ={"hips":J["hips"],"thigh_left":J["thigh_left"],"shin_left":J["shin_left"],"boot_left":J["boot_left"],
      "thigh_right":J["thigh_right"],"shin_right":J["shin_right"],"boot_right":J["boot_right"]}
LEGS=Legs(LEGJ)
def render(angles,offset=None,bg=(25,25,35),blinking=False,sword_front=False):
    offset=offset or {}; angles=dict(angles)
    angles.update(LEGS.solve(angles.get("hips",0.0),offset.get("hips",(0,0))))
    Wm=world(angles,offset)
    out=Image.new("RGBA",(1024,1024),bg+(255,))
    order=[("plume",None),("arm_shield",None),("body",None),("head",None),("shield",None),("sword",None),("arm_sword",None)]
    if not sword_front: pass
    for n,_ in order:
        if n in LAYERS: out.alpha_composite(deform(n,Wm))
        elif n=="head": out.alpha_composite(rigid(blink if blinking else parts["head"],Wm["head"]))
        elif n=="plume": out.alpha_composite(rigid(parts["plume"],Wm["plume"]))
        elif n=="sword": out.alpha_composite(rigid(parts["sword"],Wm["hand_sword"]))
        elif n=="shield": out.alpha_composite(rigid(parts["shield"],Wm["forearm_shield"]))
    return out
if __name__=="__main__":
    poses=[{},{"upper_arm_sword":-60,"forearm_sword":-50},{"upper_arm_sword":-150,"forearm_sword":40,"hand_sword":30},
           {"chest":-8,"head":5,"upper_arm_sword":100,"forearm_sword":10},{"upper_arm_shield":-30,"forearm_shield":40,"chest":6}]
    s=Image.new("RGB",(len(poses)*520,600))
    for i,p in enumerate(poses): s.paste(render(p).crop((200,60,720,920)).convert("RGB").resize((520,600)),(i*520,0))
    s.resize((s.width//2,s.height//2),Image.LANCZOS).save("skin_test.png")
