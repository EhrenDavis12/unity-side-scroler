"""Video -> knight: turn tracked COCO keypoints into the hybrid rig's joint angles.
Arm bones take the actor's screen directions; the body lean and hip shift carry over scaled;
the legs stay IK-planted so the knight's feet never slide."""
import json, math, numpy as np
from scipy.ndimage import gaussian_filter1d
import attack7 as A            # rig, REST directions, render, leg IK
from ik import ang
from PIL import Image
d=json.load(open("pose_attack.json")); fps=d["fps"]; F=np.array([f for f in d["frames"]],float)   # (n,17,3)
n=len(F)
def unwrap_deg(a): return np.degrees(np.unwrap(np.radians(a)))
def dirs(a,b): v=F[:,b,:2]-F[:,a,:2]; return unwrap_deg(np.degrees(np.arctan2(-v[:,1],v[:,0])))
# the swinging arm is the one whose wrist travels furthest
travel=[np.abs(np.diff(F[:,w,:2],axis=0)).sum() for w in (9,10)]
S_sh,S_el,S_wr=(5,7,9) if travel[0]>travel[1] else (6,8,10)
O_sh,O_el,O_wr=(6,8,10) if S_sh==5 else (5,7,9)
print("swing arm:", "person's left" if S_sh==5 else "person's right", "travel",[int(t) for t in travel])
sig=1.6
U=gaussian_filter1d(dirs(S_sh,S_el),sig); Fo=gaussian_filter1d(dirs(S_el,S_wr),sig)
Ou=gaussian_filter1d(dirs(O_sh,O_el),sig); Of=gaussian_filter1d(dirs(O_el,O_wr),sig)
mid_sh=(F[:,5,:2]+F[:,6,:2])/2; mid_hp=(F[:,11,:2]+F[:,12,:2])/2
lean=gaussian_filter1d(np.degrees(np.arctan2(mid_sh[:,0]-mid_hp[:,0], mid_hp[:,1]-mid_sh[:,1])),sig)   # + = leaning right
shw=np.median(np.linalg.norm(F[:,5,:2]-F[:,6,:2],axis=1)); knight_shw=208.0
hip_dx=gaussian_filter1d((mid_hp[:,0]-mid_hp[0,0])/shw*knight_shw,sig); hip_dy=gaussian_filter1d((mid_hp[:,1]-mid_hp[0,1])/shw*knight_shw,sig)
# the sword continues the forearm with a slight cock of the wrist
Sw=Fo-14
def near(a,ref): return a+360*np.round((ref-a)/360)
RU,RF,RS=A.REST["U"],A.REST["F"],A.REST["S"]
U=near(U,RU+ (U[len(U)//2]-RU)); Fo=near(Fo,U); Sw=near(Sw,Fo)
# blend from the knight's rest pose into the performance and back out
w=np.ones(n); ramp=int(0.25*fps)
w[:ramp]=np.linspace(0,1,ramp)**2*(3-2*np.linspace(0,1,ramp)); w[-ramp:]=w[:ramp][::-1]
def mix(a,rest): return rest+(a-near(a*0+rest,a)*0-rest)*w if False else rest+(a-rest)*w
def unwrap_to(a,rest): return a-360*np.round((a[0]-rest)/360)
U=unwrap_to(U,RU); Fo=unwrap_to(Fo,RF); Sw=unwrap_to(Sw,RS)
Um,Fm,Sm=mix(U,RU),mix(Fo,RF),mix(Sw,RS)
chest=np.clip(-lean*0.8,-18,18)*w                  # screen lean right = clockwise = negative angle
oU=(Ou-Ou[0])*0.5*w; oF=(Of-Of[0])*0.5*w
def pose(i):
    a=A.arm(Um[i],Fm[i],Sm[i],chest[i])
    a.update({"chest":chest[i],"head":-chest[i]*0.4,"plume":chest[i]*0.8,"upper_arm_shield":oU[i],"forearm_shield":oF[i]-oU[i],
              "pauldron_shield":max(-60,min(60,0.6*oU[i])),"hips":chest[i]*0.3})
    off={"hips":(float(np.clip(hip_dx[i]*w[i],-6,6)),float(np.clip(hip_dy[i]*w[i],0,7)))}
    a.update(A.LEGS.solve(a["hips"],off["hips"]))
    return a,off
def frame(i):
    a,off=pose(i)
    return A.fk.render(a,d="parts5",offset=off,order=A.SWING_ORDER if 0.2<w[i]*(1 if Um[i]!=RU else 0) and (Um[i]-RU)>40 else A.ORDER)
if __name__=="__main__":
    crop=(60,0,964,940)
    fr=[frame(i).crop(crop).convert("RGB").resize((452,470),Image.LANCZOS) for i in range(n)]
    fr[0].save("video_attack_preview.gif",save_all=True,append_images=fr[1:]+[fr[-1]]*10,duration=int(1000/fps),loop=0)
    s=Image.new("RGB",(452*8,470))
    for k,i in enumerate(np.linspace(0,n-1,8).astype(int)): s.paste(fr[i],(k*452,0))
    s.resize((s.width//2,s.height//2),Image.LANCZOS).save("video_attack_strip.png")
    print("frames",n)
