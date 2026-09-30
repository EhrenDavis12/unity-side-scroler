import numpy as np
from PIL import Image
def place(img, s, dx, dy, size):
    n=int(round(img.size[0]*s)); im=img.resize((n,n),Image.LANCZOS)
    c=Image.new("RGB",size,(0,255,0)); c.paste(im,(int(round(dx)),int(round(dy)))); return c
def score(ref, cand, w):
    return (np.abs(ref-cand).sum(-1)*w).sum()/w.sum()
def search(img, ref_img, w, scales, shifts, factor):
    """minimise colour difference over weighted region, at 1/factor resolution"""
    size=ref_img.size; small=(size[0]//factor,size[1]//factor)
    R=np.array(ref_img.resize(small,Image.BILINEAR)).astype(float)
    ws=np.array(Image.fromarray((w*255).astype(np.uint8)).resize(small))/255.0
    best=None
    for s in scales:
        for dx in shifts[0]:
            for dy in shifts[1]:
                C=np.array(place(img.resize((img.size[0]//factor,img.size[1]//factor)),s,dx/factor,dy/factor,small)).astype(float)
                v=score(R,C,ws)
                if best is None or v<best[0]: best=(v,s,dx,dy)
    return best
