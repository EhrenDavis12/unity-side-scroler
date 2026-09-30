"""Cut every piece off a parts sheet: flatten green, matte like the rig parts, one PNG per component."""
import sys, io, json, numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
sys.path.insert(0,"/Users/ehrendavis/work/local-RED-dev/src/Asset-Gen-Framework")
from agf import matting
def cut(src, tag, outdir):
    im=Image.open(src).convert("RGB"); a=np.array(im).astype(int)
    g=a[...,1]-np.maximum(a[...,0],a[...,2])
    fg=g<70                                           # anything not strongly green
    fg=ndimage.binary_opening(fg,iterations=1)
    fg=ndimage.binary_fill_holes(fg)                 # keep the sword-grip hole? filled here; re-opened by colour below
    lab,n=ndimage.label(fg)
    sizes=ndimage.sum(fg,lab,range(1,n+1))
    flat=a.copy(); flat[~fg]=(0,255,0)
    hole=fg&(g>=70)                                   # enclosed green (a grip hole) stays transparent
    buf=io.BytesIO(); Image.fromarray(flat.astype(np.uint8)).save(buf,"PNG")
    mb=io.BytesIO(); Image.fromarray(((fg&~hole)*255).astype(np.uint8)).save(mb,"PNG")
    rgba=np.array(Image.open(io.BytesIO(matting.apply(buf.getvalue(),mb.getvalue(),{"erode":2,"grow":4,"threshold":60,"background":"#00ff00"}))))
    c=rgba[...,:3].astype(int); c[...,1]=np.minimum(c[...,1],np.maximum(c[...,0],c[...,2])+8); rgba[...,:3]=c.astype(np.uint8)
    idx=ndimage.distance_transform_edt(lab==0,return_distances=False,return_indices=True); near=lab[idx[0],idx[1]]
    info=[]
    for i,s in enumerate(sizes,1):
        if s<300: continue
        m=(near==i)&(rgba[...,3]>0)
        ys,xs=np.nonzero(m); x0,y0,x1,y1=xs.min()-2,ys.min()-2,xs.max()+3,ys.max()+3
        p=np.zeros_like(rgba); p[m]=rgba[m]
        name=f"{tag}{len(info):02d}"
        Image.fromarray(p).crop((x0,y0,x1,y1)).save(f"{outdir}/{name}.png")
        info.append({"name":name,"bbox":[int(x0),int(y0),int(x1),int(y1)],"px":int(s)})
    return info
if __name__=="__main__":
    allinfo={}
    for tag,src in (("c","partsheet_c.png"),("d","partsheet_d.png"),("a","partsheet_a.png")):
        allinfo[tag]=cut(src,tag,"sheetparts")
    json.dump(allinfo,open("sheetparts/index.json","w"),indent=1)
    # labelled contact sheets
    for tag in allinfo:
        src=Image.open(f"partsheet_{tag}.png").convert("RGB"); d=ImageDraw.Draw(src)
        for p in allinfo[tag]:
            x0,y0,x1,y1=p["bbox"]; d.rectangle((x0,y0,x1,y1),outline=(255,0,255),width=2); d.text((x0+3,y0+2),p["name"],fill=(255,255,0))
        src.save(f"sheet_{tag}_labels.png")
    print({k:len(v) for k,v in allinfo.items()})
