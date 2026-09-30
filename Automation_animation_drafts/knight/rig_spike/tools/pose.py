import numpy as np, sys
from PIL import Image
ORDER=["plume","arm_shield","pauldron_shield","leg_right","leg_left","pelvis","torso","head","sword","hand_sword","forearm_sword","upper_arm_sword","pauldron_sword","shield"]
# joints (canvas px) and parents
J={"shoulder_s":(395,448),"elbow_s":(352,506),"wrist_s":(328,575),"shoulder_h":(603,448)}
def rot_about(img,deg,c):  # deg: + = counter-clockwise on screen
    return img.rotate(deg,center=c,resample=Image.BICUBIC)
def pose(shoulder=0,elbow=0,wrist=0,pad_share=0.35,pad_max=35,bg=(25,25,35)):
    P={n:Image.open(f"parts2/{n}.png").convert("RGBA") for n in ORDER}
    # forward kinematics for the sword arm chain, applied child-first
    def chain(img,levels):
        for deg,c in levels: img=rot_about(img,deg,c)
        return img
    import math
    def rp(p,deg,c):
        a=math.radians(-deg); x,y=p[0]-c[0],p[1]-c[1]
        return (c[0]+x*math.cos(a)-y*math.sin(a), c[1]+x*math.sin(a)+y*math.cos(a))
    e=rp(J["elbow_s"],shoulder,J["shoulder_s"]); w=rp(rp(J["wrist_s"],shoulder,J["shoulder_s"]),elbow,e)
    # hand, sword: wrist -> elbow -> shoulder (apply innermost first in rest space)
    for n in ("hand_sword","sword"):
        P[n]=rot_about(rot_about(rot_about(P[n],wrist,J["wrist_s"]),elbow,J["elbow_s"]),shoulder,J["shoulder_s"])
    P["forearm_sword"]=rot_about(rot_about(P["forearm_sword"],elbow,J["elbow_s"]),shoulder,J["shoulder_s"])
    P["upper_arm_sword"]=rot_about(P["upper_arm_sword"],shoulder,J["shoulder_s"])
    P["pauldron_sword"]=rot_about(P["pauldron_sword"],max(-pad_max,min(pad_max,shoulder*pad_share)),J["shoulder_s"])
    out=Image.new("RGBA",(1024,1024),bg+(255,))
    for n in ORDER: out.alpha_composite(P[n])
    return out
if __name__=="__main__":
    shots=[(0,0,0),(-40,-30,0),(-110,-20,0),(-150,10,20),(40,60,-20)]
    s=Image.new("RGB",(520*len(shots),560))
    for i,(a,b,c) in enumerate(shots): s.paste(pose(a,b,c).crop((130,180,650,740)).convert("RGB"),(i*520,0))
    s.resize((s.width//2,s.height//2),Image.LANCZOS).save("joint_test.png")
