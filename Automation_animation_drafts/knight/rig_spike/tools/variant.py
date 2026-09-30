"""Cut one part out of a variant image (same pose, already on the base canvas)."""
import sys, io, numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
sys.path.insert(0,"/Users/ehrendavis/work/local-RED-dev/src/Asset-Gen-Framework")
from agf import matting
import importlib.util
spec=importlib.util.spec_from_file_location("cut",__file__.replace("variant.py","cut.py")); cut=importlib.util.module_from_spec(spec); spec.loader.exec_module(cut)
def flatten(im):
    a=np.array(im.convert("RGB")).astype(int); g=a[...,1]-np.maximum(a[...,0],a[...,2])
    fg=g<60; fg=ndimage.binary_opening(fg,iterations=1)
    a[~fg]=(0,255,0); return Image.fromarray(a.astype(np.uint8)), fg
def cut_part(src, poly, out, extra=None):
    flat,fg=flatten(Image.open(src))
    m=Image.new("L",flat.size,0); ImageDraw.Draw(m).polygon(poly,fill=255)
    region=np.array(m)>0
    if extra is not None: region&=extra
    core=fg&region
    buf=io.BytesIO(); flat.save(buf,"PNG"); mb=io.BytesIO(); Image.fromarray((core*255).astype(np.uint8)).save(mb,"PNG")
    rgba=np.array(Image.open(io.BytesIO(matting.apply(buf.getvalue(),mb.getvalue(),{"erode":2,"grow":4,"threshold":60,"background":"#00ff00"}))))
    # the band may spill past the polygon into neighbours: keep only near the region
    near=ndimage.binary_dilation(region,iterations=4); rgba[~near]=0
    c=rgba[...,:3].astype(int); c[...,1]=np.minimum(c[...,1],np.maximum(c[...,0],c[...,2])); rgba[...,:3]=c.astype(np.uint8)
    Image.fromarray(rgba,"RGBA").save(out)
if __name__=="__main__":
    cut_part(sys.argv[1], cut.POLY[sys.argv[2]], sys.argv[3])
