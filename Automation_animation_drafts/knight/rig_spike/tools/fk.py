from PIL import Image
ORDER=["plume","pelvis","upper_arm_shield","forearm_shield","pauldron_shield","shin_right","thigh_right","shin_left","thigh_left","torso","head","upper_arm_sword","sword","forearm_sword","hand_sword","pauldron_sword","shield"]
J={"pelvis":(502,668),"torso":(500,640),"head":(495,400),"plume":(470,130),
 "pauldron_sword":(395,448),"upper_arm_sword":(395,448),"forearm_sword":(352,506),"hand_sword":(328,575),"sword":(318,600),
 "pauldron_shield":(603,448),"upper_arm_shield":(603,448),"forearm_shield":(626,493),"shield":(655,520),
 "thigh_left":(445,668),"shin_left":(415,732),"thigh_right":(560,668),"shin_right":(590,732)}
P={"pelvis":None,"torso":"pelvis","head":"torso","plume":"head",
 "pauldron_sword":"torso","upper_arm_sword":"torso","forearm_sword":"upper_arm_sword","hand_sword":"forearm_sword","sword":"hand_sword",
 "pauldron_shield":"torso","upper_arm_shield":"torso","forearm_shield":"upper_arm_shield","shield":"forearm_shield",
 "thigh_left":"pelvis","shin_left":"thigh_left","thigh_right":"pelvis","shin_right":"thigh_right"}
_cache={}
def img(n,d):
    k=(d,n)
    if k not in _cache: _cache[k]=Image.open(f"{d}/{n}.png").convert("RGBA")
    return _cache[k]
BONE={"upper_arm_sword":"forearm_sword","forearm_sword":"hand_sword","upper_arm_shield":"forearm_shield"}
def render(angles,d="parts3",bg=(25,25,35),swap=None,order=None,offset=None,scale=None,stretch=None,reach=None):
    """angles: slot -> deg (+ = counter-clockwise on screen), local to parent.
    offset: slot -> (dx,dy) canvas px added to the slot's rest position (children follow).
    scale: slot -> (sx,sy) on that slot's own sprite only, about its joint (children do not inherit).
    stretch: slot -> k, lengthens a limb bone along its own axis; its child joint moves out with it."""
    import numpy as np
    offset=dict(offset or {}); scale=scale or {}; stretch=stretch or {}; reach=reach or {}
    for s,k in stretch.items():            # the child slot slides out along the bone
        c=BONE[s]; v=np.subtract(J[c],J[s]); o=offset.get(c,(0,0))
        offset[c]=(o[0]+(k-1)*v[0], o[1]+(k-1)*v[1])
    out=Image.new("RGBA",(1024,1024),bg+(255,))
    for n in (order or ORDER):
        src=(swap or {}).get(n,n); im=img(src,d)
        if n in scale:
            sx,sy=scale[n]; cx,cy=J[n]
            im=im.transform(im.size,Image.AFFINE,(1/sx,0,cx-cx/sx,0,1/sy,cy-cy/sy),resample=Image.BICUBIC)
        if stretch.get(n,1)!=1:
            k=stretch[n]; v=np.subtract(J[BONE[n]],J[n]); u=v/np.hypot(*v); cx,cy=J[n]
            Mi=np.eye(2)+(1/k-1)*np.outer(u,u)        # inverse map: output -> input
            a,b=Mi[0]; c_,d_=Mi[1]
            im=im.transform(im.size,Image.AFFINE,(a,b,cx-a*cx-b*cy,c_,d_,cy-c_*cx-d_*cy),resample=Image.BICUBIC)
        s=n
        while s:
            a_=angles.get(s,0); dx,dy=offset.get(s,(0,0)); r=reach.get(s,1)
            if r!=1:   # uniform scale of this slot's whole subtree about its joint (children inherit)
                cx,cy=J[s]; im=im.transform(im.size,Image.AFFINE,(1/r,0,cx-cx/r,0,1/r,cy-cy/r),resample=Image.BICUBIC)
            if a_ or dx or dy: im=im.rotate(a_,center=J[s],translate=(dx,dy),resample=Image.BICUBIC)
            s=P[s]
        out.alpha_composite(im)
    return out
SWORD_CHAIN=["upper_arm_sword","sword","forearm_sword","hand_sword","pauldron_sword"]
ORDER_SWING=[n for n in ORDER if n not in SWORD_CHAIN]+SWORD_CHAIN   # sword arm drawn over the shield
