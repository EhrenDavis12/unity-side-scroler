import json, math, os, numpy as np
from PIL import Image
from scipy.spatial import Delaunay
from scipy import ndimage
import skin as K
from smooth import Clip
from ik import ang
OUT="export_skin/"; os.makedirs(OUT,exist_ok=True)
def crop_save(arr,name):
    m=arr[...,3]>0; ys,xs=np.nonzero(m); x0,y0,x1,y1=xs.min()-2,ys.min()-2,xs.max()+3,ys.max()+3
    Image.fromarray(arr).crop((x0,y0,x1,y1)).save(OUT+name+".png"); return [int(x0),int(y0),int(x1-x0),int(y1-y0)]
data={"canvas":[1024,1024],"note":"canvas px, origin top-left, y DOWN. Bones: head=joint it rotates about, tail=end. Vertices in canvas px. weights: up to 4 [boneName,weight] per vertex, summing to 1.",
      "bones":{b:{"head":list(map(float,K.P(v[0]))),"tail":list(map(float,K.P(v[1]))),"parent":v[2]} for b,v in K.BONES.items()},
      "skinned":{},"props":{}}
for L,img in K.LAYERS.items():
    rect=crop_save(img,L); pts,W=K.MESH[L]
    tri=Delaunay(pts); m=ndimage.binary_dilation(img[...,3]>0,iterations=4)
    c=pts[tri.simplices].mean(1).astype(int); keep=m[c[:,1].clip(0,1023),c[:,0].clip(0,1023)]
    simp=tri.simplices[keep]
    used=np.unique(simp); remap=-np.ones(len(pts),int); remap[used]=np.arange(len(used))
    bones=K.LAYER_BONES[L]; ws=[]
    for i in used:
        order=np.argsort(-W[i])[:4]; w=W[i][order]; w=w/w.sum()
        ws.append([[bones[j],round(float(x),4)] for j,x in zip(order,w) if x>1e-3])
    data["skinned"][L]={"file":L+".png","rect":rect,"vertices":pts[used].round(2).tolist(),"triangles":remap[simp].tolist(),"weights":ws}
    print(L,len(used),"verts",len(simp),"tris")
PROP={"head":("head","head"),"head_blink":("head","head"),"plume":("plume","plume"),"sword":("sword","hand_sword"),"shield":("shield","forearm_shield")}
for n,(src,bone) in PROP.items():
    arr=K.blink if n=="head_blink" else K.parts[src]; rect=crop_save(arr,n)
    data["props"][n]={"file":n+".png","rect":rect,"bone":bone,"slot":"head" if n.startswith("head") else n}
data["draw_order"]=["plume","arm_shield","body","head","shield","sword","arm_sword"]
# ---- chop keys (bone local angles, + = counter-clockwise on screen) ----
J=K.J; tipm=K.parts["sword"][...,3]>0; ys,xs=np.nonzero(tipm); d=(xs-J["grip"][0])**2+(ys-J["grip"][1])**2; tip=np.array([xs[d.argmax()],ys[d.argmax()]],float)
REST={"U":ang(J["elbow_s"]-J["shoulder_s"])%360,"F":ang(J["wrist_s"]-J["elbow_s"])%360,"S":ang(tip-J["grip"])%360}
def arm(U,F,Sw,chest):
    dU=U-REST["U"]; dF=F-REST["F"]; dS=Sw-REST["S"]
    return {"upper_arm_sword":dU-chest,"forearm_sword":dF-dU,"hand_sword":dS-dF}
def key(t,name,U,F,Sw,chest,head,plume,su,sf,ha,ho):
    a=arm(U,F,Sw,chest); a.update({"chest":chest,"head":head,"plume":plume,"upper_arm_shield":su,"forearm_shield":sf,"hips":ha})
    return {"t":t,"name":name,"angles":{k:round(v,2) for k,v in a.items()},"offset":{"hips":list(ho)}}
U0,F0,S0=REST["U"],REST["F"],REST["S"]
KEYS=[key(0.00,"rest",U0,F0,S0,0,0,0,0,0,0,(0,0)),
      key(0.16,"lift",290,420,455,3,-1,-4,-6,6,1,(-2,1)),
      key(0.30,"windup",440,500,525,7,-3,-9,-12,12,3,(-3,1)),
      key(0.38,"hold",445,510,540,8,-3,-11,-13,13,3,(-3,1)),
      key(0.51,"strike",350,352,335,-10,5,12,18,-20,-5,(5,6)),
      key(0.62,"follow",300,298,275,-13,7,17,20,-22,-6,(6,7)),
      key(1.02,"recover",U0,F0,S0,-2,1,-4,2,-3,-1,(1,1)),
      key(1.32,"rest",U0,F0,S0,0,0,0,0,0,0,(0,0))]
attack={"length":1.32,"interp":"monotone_cubic","ik":{"legs":"feet planted, knees out; solve thigh/shin with a 2-bone limb solver targeting the rest ankle positions, boot keeps its rest world angle"},
        "lag":{"hips":-0.03,"chest":-0.02,"upper_arm_sword":0.0,"forearm_sword":0.025,"hand_sword":0.05,"upper_arm_shield":0.02,"forearm_shield":0.04,"head":0.035,"plume":0.08},"keys":KEYS}
json.dump(data,open(OUT+"skin.json","w")); json.dump(attack,open(OUT+"attack_keys.json","w"),indent=1)
clip=Clip(attack)
def frame(x):
    p=clip.sample(x); return K.render(p["angles"],p["offset"])
if __name__=="__main__":
    crop=(100,0,924,940); fps=25
    fr=[frame(min(i/fps,1.32)).crop(crop).convert("RGB").resize((412,470),Image.LANCZOS) for i in range(int(1.32*fps)+8)]
    fr[0].save("skin_attack.gif",save_all=True,append_images=fr[1:],duration=40,loop=0)
    s=Image.new("RGB",(412*7,470))
    for i,k in enumerate(KEYS[:7]): s.paste(frame(k["t"]).crop(crop).convert("RGB").resize((412,470),Image.LANCZOS),(i*412,0))
    s.resize((s.width//2,s.height//2),Image.LANCZOS).save("skin_keys.png")
