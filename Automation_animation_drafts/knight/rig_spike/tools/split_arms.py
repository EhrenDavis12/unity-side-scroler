import numpy as np
from PIL import Image, ImageDraw, ImageFilter
SLEEVE=(16,21,26); OUTLINE=(6,10,15); HI=(55,59,65)
def poly_mask(poly):
    m=Image.new("L",(1024,1024),0); ImageDraw.Draw(m).polygon(poly,fill=255); return np.array(m)>0
def ball(cx,cy,r,ol=5):
    """undersuit ball joint: outlined dark disc with a soft highlight, antialiased (4x supersample)"""
    k=4; im=Image.new("RGBA",(1024*k,1024*k),(0,0,0,0)); d=ImageDraw.Draw(im)
    d.ellipse(((cx-r)*k,(cy-r)*k,(cx+r)*k,(cy+r)*k),fill=OUTLINE+(255,))
    d.ellipse(((cx-r+ol)*k,(cy-r+ol)*k,(cx+r-ol)*k,(cy+r-ol)*k),fill=SLEEVE+(255,))
    hr=r*0.45; d.ellipse(((cx-r*0.55-hr/2)*k,(cy-r*0.55-hr/2)*k,(cx-r*0.55+hr/2+hr*0.6)*k,(cy-r*0.55+hr/2)*k),fill=HI+(255,))
    return im.resize((1024,1024),Image.LANCZOS)
def under(top_rgba, bottom_img):
    out=bottom_img.copy(); out.alpha_composite(Image.fromarray(top_rgba,"RGBA")); return out
def take(src,mask):
    out=src.copy(); out[~mask]=0; return out
arm=np.array(Image.open("parts/arm_sword.png")); torso=np.array(Image.open("parts/torso.png"))
op=arm[...,3]>0
ys,xs=np.mgrid[0:1024,0:1024]
torso_strip=op&(xs>425)&(ys>470)
pad=op&poly_mask([(345,392),(442,392),(442,470),(430,482),(360,488),(338,476)])&~torso_strip
fore=op&~pad&~torso_strip&((ys-480)>0.75*(xs-320))
upper=op&~pad&~torso_strip&~fore
# torso gets its strip back
t=torso.copy(); t[torso_strip]=arm[torso_strip]; Image.fromarray(t,"RGBA").save("parts2/torso.png")
Image.fromarray(take(arm,pad),"RGBA").save("parts2/pauldron_sword.png")
Image.fromarray(take(arm,fore),"RGBA").save("parts2/forearm_sword.png")
up=under(take(arm,upper), Image.alpha_composite(ball(395,448,36),ball(352,506,19)))
up.save("parts2/upper_arm_sword.png")
# shield side
arm2=np.array(Image.open("parts/arm_shield.png")); op2=arm2[...,3]>0
pad2=op2&poly_mask([(560,392),(655,392),(664,470),(640,486),(575,482),(556,440)])
Image.fromarray(take(arm2,pad2),"RGBA").save("parts2/pauldron_shield.png")
rest=under(take(arm2,op2&~pad2), ball(603,448,34)); rest.save("parts2/arm_shield.png")
for n in ["plume","head","head_blink","pelvis","leg_left","leg_right","hand_sword","sword","shield"]:
    Image.open(f"parts/{n}.png").save(f"parts2/{n}.png")
print("px pad",pad.sum(),"upper",upper.sum(),"fore",fore.sum(),"strip",torso_strip.sum(),"pad2",pad2.sum())
