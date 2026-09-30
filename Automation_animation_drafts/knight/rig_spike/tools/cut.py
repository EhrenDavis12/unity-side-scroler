import sys, io, json, numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
sys.path.insert(0,"/Users/ehrendavis/work/local-RED-dev/src/Asset-Gen-Framework")
from agf import matting
# back-to-front draw order; polygons are claimed in PRIORITY order (first wins)
POLY={
 "sword":[(65,820),(90,790),(240,640),(245,600),(270,590),(335,660),(315,695),(285,675),(115,830)],
 "hand_sword":[(282,555),(368,545),(380,600),(350,648),(318,660),(300,630),(282,600)],
 "shield":[(645,365),(785,350),(880,395),(880,565),(840,660),(775,712),(712,645),(672,560),(648,480)],
 "arm_shield":[(565,392),(652,400),(660,470),(668,560),(630,565),(600,540),(585,470),(560,440)],
 "arm_sword":[(345,395),(438,395),(440,470),(410,540),(370,590),(300,560),(318,495),(340,470)],
 "head":[(338,200),(420,118),(520,110),(642,205),(648,300),(625,392),(560,398),(480,404),(425,402),(345,372)],
 "plume":[(180,90),(300,30),(440,10),(515,40),(505,125),(440,140),(380,190),(345,240),(270,235),(180,200)],
 "torso":[(398,402),(598,402),(605,540),(638,660),(560,668),(500,695),(440,684),(382,662),(400,560)],
 "leg_left":[(325,690),(482,690),(482,890),(320,890)],
 "leg_right":[(528,690),(700,690),(700,900),(528,900)],
 "pelvis":[(395,615),(625,615),(612,705),(395,705)],
}
ORDER=["plume","arm_shield","leg_right","leg_left","pelvis","torso","head","sword","hand_sword","arm_sword","shield"]
def build(src, outdir):
    flat=Image.open(src).convert("RGB"); W,H=flat.size
    fg=np.array(Image.open(f"{outdir}/../fg_mask.png"))>=128
    buf=io.BytesIO(); flat.save(buf,"PNG"); mb=io.BytesIO(); Image.fromarray((fg*255).astype(np.uint8)).save(mb,"PNG")
    rgba=np.array(Image.open(io.BytesIO(matting.apply(buf.getvalue(),mb.getvalue(),{"erode":2,"grow":4,"threshold":60,"background":"#00ff00"}))))
    # despill: the knight has no green, so near the silhouette edge no pixel
    # may be greener than its own red/blue -- that excess is the backdrop
    op=rgba[...,3]>0
    near=op
    c=rgba[...,:3].astype(int); cap=np.maximum(c[...,0],c[...,2])
    c[...,1]=np.where(near,np.minimum(c[...,1],cap),c[...,1]); rgba[...,:3]=c.astype(np.uint8)
    label=np.full((H,W),-1)
    names=list(POLY)
    for i,n in enumerate(POLY):
        m=Image.new("L",(W,H),0); ImageDraw.Draw(m).polygon(POLY[n],fill=255)
        label[(np.array(m)>0)&fg&(label<0)]=i
    xs=np.arange(W)[None,:].repeat(H,0); ys=np.arange(H)[:,None].repeat(W,1)
    # leftovers and the matte band take the nearest labelled pixel
    idx=ndimage.distance_transform_edt(label<0,return_distances=False,return_indices=True)
    full=label[idx[0],idx[1]]
    print("unlabelled fg px:",int((fg&(label<0)).sum()))
    info={}
    for n in ORDER:
        k=names.index(n); part=rgba.copy(); part[full!=k]=0
        Image.fromarray(part,"RGBA").save(f"{outdir}/{n}.png")
        a=part[...,3]>0; ys_,xs_=np.nonzero(a); info[n]=[int(xs_.min()),int(ys_.min()),int(xs_.max()),int(ys_.max())]
    Image.fromarray(rgba,"RGBA").save(f"{outdir}/_whole.png")
    json.dump({"order":ORDER,"bbox":info,"size":[W,H]},open(f"{outdir}/parts.json","w"),indent=1)
    # preview: each part tinted
    pal=[(230,60,60),(60,160,230),(240,200,40),(120,220,90),(160,90,220),(240,120,200),(60,220,200),(250,150,60),(150,150,150),(90,90,240),(200,240,120)]
    prev=np.array(flat).astype(float)
    for n in ORDER:
        k=names.index(n); sel=(full==k)&fg; prev[sel]=prev[sel]*0.45+np.array(pal[ORDER.index(n)])*0.55
    Image.fromarray(prev.astype(np.uint8)).save(f"{outdir}/../cut_preview.png")
if __name__=="__main__": build(sys.argv[1],sys.argv[2])
