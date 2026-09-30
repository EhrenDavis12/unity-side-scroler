"""Knight from individually generated parts (solo/*.png), placed on the hybrid skeleton (parts5).
Limbs: two-point fit (top joint -> this joint, bottom joint -> child joint). Other parts: fit to the
original part's box. Output: parts6/ full-canvas rest sprites + skeleton.json."""
import json, math, numpy as np
from PIL import Image
from scipy import ndimage
sk=json.load(open("parts5/skeleton.json")); J={k:np.array(v,float) for k,v in sk["J"].items()}; P=sk["P"]; ORDER=sk["ORDER"]
J["shin_left"]=np.array([420.,726.]); J["shin_right"]=np.array([590.,730.])     # knee at the knee guard's centre
J["boot_left"]=np.array([412.,828.]); J["boot_right"]=np.array([598.,834.])
W={"upper_arm":50,"forearm":58,"thigh":60,"shin":74}
OUT="parts6/"
def load(n, mirror=False):
    im=Image.open(f"solo/{n}.png").convert("RGBA")
    a=np.array(im); m=a[...,3]>40
    lab,k=ndimage.label(m); 
    if k>1:                                    # keep the main piece only
        sz=ndimage.sum(m,lab,range(1,k+1)); keep=np.isin(lab,[i+1 for i,s in enumerate(sz) if s>0.02*sz.max()]); a[~keep]=0
    im=Image.fromarray(a)
    if mirror: im=im.transpose(Image.FLIP_LEFT_RIGHT)
    return im
def mask(im): return np.array(im)[...,3]>40
def bbox(m): ys,xs=np.nonzero(m); return xs.min(),ys.min(),xs.max(),ys.max()
def ends(im, inset_top=0.5, inset_bot=0.5):
    """joint points of an upright limb: centre of its top and bottom, inset by a fraction of its width"""
    m=mask(im); x0,y0,x1,y1=bbox(m); h=y1-y0
    def row_center(y): xs=np.nonzero(m[int(y)])[0]; return (xs.min()+xs.max())/2, xs.max()-xs.min()
    ct,wt=row_center(y0+0.15*h); cb,wb=row_center(y1-0.15*h)
    return np.array([ct,y0+inset_top*wt]), np.array([cb,y1-inset_bot*wb])
def place_two(im, a, b, A, B):
    """similarity transform taking piece points a,b to canvas points A,B"""
    v=b-a; V=B-A; s=np.linalg.norm(V)/np.linalg.norm(v)
    ang=math.atan2(V[1],V[0])-math.atan2(v[1],v[0]); c,sn=math.cos(ang)*s,math.sin(ang)*s
    M=np.array([[c,-sn],[sn,c]]); T=A-M@a
    return warp(im,M,T)
def place_limb(im, a, b, A, B, width):
    """fit a->A, b->B along the bone, and scale across the bone to `width` px"""
    m=mask(im); x0,y0,x1,y1=bbox(m); ym=int((y0+y1)/2); xs=np.nonzero(m[ym])[0]; wmid=xs.max()-xs.min()
    v=b-a; V=B-A; u=v/np.linalg.norm(v); n=np.array([-u[1],u[0]]); U=V/np.linalg.norm(V); N=np.array([-U[1],U[0]])
    sl=np.linalg.norm(V)/np.linalg.norm(v); sw=width/wmid
    M=np.outer(U,u)*sl+np.outer(N,n)*sw
    return warp(im,M,A-M@a)
def place_box(im, box, anchor="center", scale_by="w", mirror=False):
    m=mask(im); x0,y0,x1,y1=bbox(m); tx0,ty0,tx1,ty1=box
    s=(tx1-tx0)/(x1-x0) if scale_by=="w" else (ty1-ty0)/(y1-y0)
    M=np.array([[s,0],[0,s]]); src=np.array([(x0+x1)/2,(y0+y1)/2]); dst=np.array([(tx0+tx1)/2,(ty0+ty1)/2])
    if anchor=="top": src=np.array([(x0+x1)/2,y0]); dst=np.array([(tx0+tx1)/2,ty0])
    return warp(im,M,dst-M@src)
def place_at(im, src_pt, dst_pt, s, rot_deg=0.0):
    r=math.radians(-rot_deg); c,sn=math.cos(r)*s,math.sin(r)*s     # + = counter-clockwise on screen
    M=np.array([[c,-sn],[sn,c]]); return warp(im,M,np.array(dst_pt)-M@np.array(src_pt))
def warp(im,M,T):
    Mi=np.linalg.inv(M); ti=-Mi@T
    return im.transform((1024,1024),Image.AFFINE,(Mi[0,0],Mi[0,1],ti[0],Mi[1,0],Mi[1,1],ti[1]),resample=Image.BICUBIC)
def obox(n):  # box of the original part
    return bbox(mask(Image.open(f"parts5/{n}.png").convert("RGBA")))
out={}
# body
out["head"]=place_box(load("head"),obox("head"))
out["plume"]=place_box(load("plume"),obox("plume"))
out["chest"]=place_box(load("chest"),obox("chest"))
out["hips"]=place_box(load("hips"),obox("hips"),anchor="top",scale_by="h")
out["pauldron_sword"]=place_box(load("pad"),obox("pauldron_sword"))
out["pauldron_shield"]=place_box(load("pad",True),obox("pauldron_shield"))
out["shield"]=place_box(load("shield"),obox("shield"))
out["pelvis"]=Image.new("RGBA",(1024,1024))
# limbs
for side,mir in (("sword",False),("shield",True)):
    ua=load("upper_arm",mir); a,b=ends(ua); out[f"upper_arm_{side}"]=place_limb(ua,a,b,J[f"upper_arm_{side}"],J[f"forearm_{side}"],W["upper_arm"])
    fa=load("forearm",mir); a,b=ends(fa,0.5,0.15); out[f"forearm_{side}"]=place_limb(fa,a,b,J[f"forearm_{side}"],J[f"fist_{side}"],W["forearm"])
for side,mir in (("left",True),("right",False)):
    th=load("thigh",mir); a,b=ends(th); out[f"thigh_{side}"]=place_limb(th,a,b,J[f"thigh_{side}"],J[f"shin_{side}"],W["thigh"])
    sh=load("shin",mir); a,b=ends(sh,0.95,0.05); out[f"shin_{side}"]=place_limb(sh,a,b,J[f"shin_{side}"],J[f"boot_{side}"],W["shin"])
    bt=load("boot",side=="left"); m=mask(bt); x0,y0,x1,y1=bbox(m)
    xs=np.nonzero(m[int(y0+0.08*(y1-y0))])[0]; top=np.array([(xs.min()+xs.max())/2, y0+0.35*(xs.max()-xs.min())])
    ob=obox(f"boot_{side}"); s=1.3*(ob[2]-ob[0])/(x1-x0)
    out[f"boot_{side}"]=place_at(bt,top,J[f"boot_{side}"],s)
# hands
fist=load("fist",True); m=mask(fist); x0,y0,x1,y1=bbox(m)
out["fist_shield"]=place_at(fist,((x0+x1)/2,(y0+y1)/2),J["fist_shield"],62/(x1-x0))
sf=load("sword_fist"); a=np.array(sf); m=a[...,3]>40
dark=m&(a[...,:3].astype(int).sum(-1)<180); ys,xs=np.nonzero(dark); grip=np.array([xs.mean(),ys.mean()])
ys,xs=np.nonzero(m); tip=np.array([xs[ys.argmin()],ys.min()])
L_orig=None
o=np.array(Image.open("parts5/sword.png").convert("RGBA"))[...,3]>40; oy,ox=np.nonzero(o)
d=(ox-J["sword"][0])**2+(oy-J["sword"][1])**2; otip=np.array([ox[d.argmax()],oy[d.argmax()]],float)
out["sword"]=place_two(sf,grip,tip,J["fist_sword"],J["fist_sword"]+(otip-J["sword"]))
out["fist_sword"]=Image.new("RGBA",(1024,1024))
# the skull sits on the neck: head joint unchanged; blink made later by an edit
import os; os.makedirs(OUT,exist_ok=True)
for n,im in out.items(): im.save(OUT+n+".png")
J2={k:list(map(float,v)) for k,v in J.items()}; J2["sword"]=list(map(float,J["fist_sword"]))
json.dump({"J":J2,"P":P,"ORDER":ORDER},open(OUT+"skeleton.json","w"),indent=1)
canvas=Image.new("RGBA",(1024,1024),(25,25,35,255))
for n in ORDER: canvas.alpha_composite(out[n])
canvas.save("solo_rest.png")
print("placed",len(out))
