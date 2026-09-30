import numpy as np
from PIL import Image, ImageDraw
from split_arms import ball, poly_mask, take, under, SLEEVE, OUTLINE
ys,xs=np.mgrid[0:1024,0:1024]
L=lambda n: np.array(Image.open(f"parts/{n}.png"))
S=lambda a,n: Image.fromarray(a,"RGBA").save(f"parts3/{n}.png") if isinstance(a,np.ndarray) else a.save(f"parts3/{n}.png")
torso=L("torso"); arm=L("arm_sword"); op=arm[...,3]>0
# --- sword arm: tight pad, everything right of the pad's outline back to the torso
padpoly=[(348,472),(337,456),(348,426),(370,405),(398,395),(416,396),(424,408),(428,440),(425,468),(414,482),(360,490)]
pad=op&poly_mask(padpoly)
to_torso=op&~pad&(((xs>=423)&(ys<535))|((xs>=415)&(ys>=470)&(ys<535))|(ys<400))
fore=op&~pad&~to_torso&((ys-480)>0.75*(xs-320))
upper=op&~pad&~to_torso&~fore
t=torso.copy(); t[to_torso]=arm[to_torso]
S(take(arm,pad),"pauldron_sword"); S(take(arm,fore),"forearm_sword")
S(under(take(arm,upper),Image.alpha_composite(ball(395,448,36),ball(352,506,15))),"upper_arm_sword")
# --- shield arm: pad, upper (sleeve), forearm (cuffs + hand), split at the elbow
arm2=L("arm_shield"); op2=arm2[...,3]>0
pad2=op2&poly_mask([(560,392),(655,392),(664,470),(640,486),(575,482),(556,440)])
sh=np.array([603,448]); d=np.array([47,92])/np.hypot(47,92); el=sh+d*50   # elbow ~50px down the arm
proj=(xs-sh[0])*d[0]+(ys-sh[1])*d[1]
fore2=op2&~pad2&(proj>50); up2=op2&~pad2&~fore2
S(take(arm2,pad2),"pauldron_shield")
S(under(take(arm2,up2),Image.alpha_composite(ball(603,448,34),ball(int(el[0]),int(el[1]),14))),"upper_arm_shield")
S(take(arm2,fore2),"forearm_shield")
print("shield elbow",el.round())
# --- legs: pelvis halves become thighs; leg parts split at the knee guard's lower edge
pel=L("pelvis"); pop=pel[...,3]>0
for side,xsplit in (("left",lambda: xs<500),("right",lambda: xs>=500)):
    leg=L(f"leg_{side}"); lop=leg[...,3]>0
    if side=="left": shin=lop&(ys>=752+(xs-380)*0.1); hip=(445,668); knee=(415,732)
    else: shin=lop&(ys>=760-(xs-560)*0.16); hip=(560,668); knee=(590,732)
    thigh_px=np.where((pop&xsplit())[...,None],pel,0).astype(np.uint8)
    thigh_px[lop&~shin]=leg[lop&~shin]
    th=under(thigh_px,ball(*hip,30)); S(th,f"thigh_{side}")
    S(under(take(leg,shin),ball(*knee,27)),f"shin_{side}")
    print(side,"hip",hip,"knee",knee)
# hips filler: an undersuit ellipse behind both thighs
k=4; im=Image.new("RGBA",(4096,4096)); dd=ImageDraw.Draw(im)
dd.ellipse((440*k,648*k,565*k,700*k),fill=OUTLINE+(255,)); dd.ellipse((445*k,653*k,560*k,695*k),fill=SLEEVE+(255,))
S(im.resize((1024,1024),Image.LANCZOS),"pelvis")
S(t,"torso")
for n in ["plume","head","head_blink","hand_sword","sword","shield"]: Image.open(f"parts/{n}.png").save(f"parts3/{n}.png")
# gold torso gets the same chest-edge pixels? it was cut by polygon from its own image; keep as is
Image.open("variants/torso_gold.png").save("parts3/torso_gold.png")
