"""Assemble a rest pose from parts-sheet pieces and bake it into full-canvas slot PNGs + rig data.
Each slot: piece file, its own joint (piece px), parent slot, where it attaches (parent piece px),
rest angle (deg, + = counter-clockwise on screen, local to parent)."""
import json, math, numpy as np
from PIL import Image
SP="sheetparts/"
def crop_shin(n):     # the sheet's shin includes a boot; keep knee + greave down to the ankle band
    im=Image.open(SP+n+".png"); return im.crop((0,0,im.width,117))
def pad_dome(n):     # the sheet's pad carries a black sleeve flap; keep only the dome and its rim
    im=Image.open(SP+n+".png").convert("RGBA"); a=np.array(im); a[52:]=0; return Image.fromarray(a)
def ball_under(im, c, r):
    from split_arms import SLEEVE, OUTLINE, HI
    from PIL import ImageDraw
    k=4; w,h=im.size; b=Image.new("RGBA",(w*k,h*k)); d=ImageDraw.Draw(b); cx,cy=c
    d.ellipse(((cx-r)*k,(cy-r)*k,(cx+r)*k,(cy+r)*k),fill=OUTLINE+(255,)); d.ellipse(((cx-r+4)*k,(cy-r+4)*k,(cx+r-4)*k,(cy+r-4)*k),fill=SLEEVE+(255,))
    b=b.resize((w,h),Image.LANCZOS); b.alpha_composite(im); return b
def boot(n, mirror, joint):
    im=Image.open(SP+n+".png").convert("RGBA")
    if mirror: im=im.transpose(Image.FLIP_LEFT_RIGHT)
    pad=Image.new("RGBA",(im.width,im.height+14)); pad.paste(im,(0,14))      # room above the cuff for the ankle ball
    return ball_under(pad,(joint[0],joint[1]),15)
PIECE={}
def piece(slot,src): PIECE[slot]=src if isinstance(src,Image.Image) else Image.open(SP+src+".png").convert("RGBA")
SLOTS={}
def slot(name,src,joint,parent=None,attach=None,angle=0.0):
    piece(name,src); SLOTS[name]=dict(joint=joint,parent=parent,attach=attach,angle=angle)
UPPER=1.0
# ---- skeleton -------------------------------------------------------------------------
slot("hips","c06",(102,14),None,None,0)                     # waist joint at the belt's top centre
slot("chest","c03",(76,140),"hips",(102,20),0)              # chest bottom sits over the belt top
slot("head","c00",(112,188),"chest",(76,22),0)              # neck
slot("plume","c01",(192,132),"head",(118,22),-4)
slot("pad_sword",pad_dome("c04"),(33,24),"chest",(16,42),6)           # screen-left shoulder
slot("upper_arm_sword","c10",(30,12),"chest",(16,46),-38)
slot("forearm_sword","c16",(35,14),"upper_arm_sword",(30,74),16)
slot("fist_sword","c18",(35,10),"forearm_sword",(35,100),0)
slot("sword","c07",(33,268),"fist_sword",(35,33),157)
slot("pad_shield",pad_dome("c05"),(35,24),"chest",(136,42),-6)        # screen-right shoulder
slot("upper_arm_shield","c11",(31,12),"chest",(136,46),10)
slot("forearm_shield","c17",(34,14),"upper_arm_shield",(31,74),75)
slot("fist_shield","c19",(33,10),"forearm_shield",(34,100),-20)
slot("shield","c20",(56,90),"fist_shield",(33,30),-80)
slot("thigh_left","c21",(34,10),"hips",(62,74),-14)
slot("shin_left",crop_shin("c14"),(41,24),"thigh_left",(34,70),6)
slot("boot_left",boot("c23",True,(68,18)),(68,18),"shin_left",(41,112),8)
slot("thigh_right","c22",(33,10),"hips",(142,74),14)
slot("shin_right",crop_shin("c15"),(42,24),"thigh_right",(33,70),-6)
slot("boot_right",boot("c24",False,(40,18)),(40,18),"shin_right",(42,112),-8)
ORDER=["plume","upper_arm_shield","forearm_shield","thigh_right","boot_right","shin_right","thigh_left","boot_left","shin_left",
       "upper_arm_sword","hips","chest","head","pad_shield","forearm_sword","sword","fist_sword","pad_sword","fist_shield","shield"]
CANVAS=1024; ROOT=(512,520)   # waist position on the canvas
# ---- forward kinematics: world transform of each piece --------------------------------
def mat(angle,tx,ty):  # rotation (+ = CCW on screen, y down) then translation
    a=math.radians(angle); c,s=math.cos(a),math.sin(a)
    return np.array([[c, s, tx],[-s, c, ty],[0,0,1.0]])
W={}
def world(n):
    if n in W: return W[n]
    d=SLOTS[n]; jx,jy=d["joint"]
    if d["parent"] is None: pw=np.array([[1,0,ROOT[0]],[0,1,ROOT[1]],[0,0,1.0]]); ax,ay=0,0; pj=(0,0)
    else:
        pw=world(d["parent"]); ax,ay=d["attach"]; pj=SLOTS[d["parent"]]["joint"]
        pw=pw@np.array([[1,0,ax-pj[0]],[0,1,ay-pj[1]],[0,0,1.0]])
    # frame at this joint: parent frame translated to the attach point, rotated by the local angle
    W[n]=pw@mat(d["angle"],0,0)
    return W[n]
def render_piece(n):
    M=world(n); jx,jy=SLOTS[n]["joint"]
    T=M@np.array([[1,0,-jx],[0,1,-jy],[0,0,1.0]])        # piece px -> canvas
    Ti=np.linalg.inv(T)
    im=PIECE[n]
    return im.transform((CANVAS,CANVAS),Image.AFFINE,tuple(Ti[0])+tuple(Ti[1]),resample=Image.BICUBIC)
def joint_canvas(n):
    M=world(n); return tuple((M@np.array([0,0,1.0]))[:2].round(1))
if __name__=="__main__":
    import os; os.makedirs("parts4",exist_ok=True)
    out=Image.new("RGBA",(CANVAS,CANVAS),(25,25,35,255))
    for n in ORDER:
        p=render_piece(n); p.save(f"parts4/{n}.png"); out.alpha_composite(p)
    out.save("assembled.png")
    J={n:joint_canvas(n) for n in SLOTS}; P={n:SLOTS[n]["parent"] for n in SLOTS}
    def rot_world(n):
        a=0.0
        while n: a+=SLOTS[n]["angle"]; n=SLOTS[n]["parent"]
        return a
    ROT={n:rot_world(n) for n in SLOTS}
    json.dump({"J":J,"P":P,"ORDER":ORDER,"rest_world_rotation":ROT},open("parts4/skeleton.json","w"),indent=1)
