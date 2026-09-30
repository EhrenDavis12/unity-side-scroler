"""Smooth playback of attack keys: monotone cubic (Fritsch-Carlson) per channel,
non-uniform key times, plus per-slot lag (overlapping action)."""
import json, numpy as np
from PIL import Image
from fk import render, ORDER, ORDER_SWING

def monotone_tangents(t, y):
    n=len(t); d=np.diff(y)/np.diff(t); m=np.zeros(n)
    for i in range(1,n-1):
        if d[i-1]*d[i]<=0: m[i]=0          # extreme pose: come to rest, never overshoot
        else:
            w1=2*(t[i+1]-t[i])+(t[i]-t[i-1]); w2=(t[i+1]-t[i])+2*(t[i]-t[i-1])
            m[i]=(w1+w2)/(w1/d[i-1]+w2/d[i])   # weighted harmonic mean
    return m                                # ends: 0 (start and finish at rest)

def hermite(t, y, m, x):
    x=min(max(x,t[0]),t[-1]); i=min(np.searchsorted(t,x,side="right")-1,len(t)-2)
    h=t[i+1]-t[i]; s=(x-t[i])/h
    h00=2*s**3-3*s**2+1; h10=s**3-2*s**2+s; h01=-2*s**3+3*s**2; h11=s**3-s**2
    return h00*y[i]+h10*h*m[i]+h01*y[i+1]+h11*h*m[i+1]

class Clip:
    def __init__(self, js):
        self.js=js; keys=sorted(js["keys"],key=lambda k:k["t"]); self.t=np.array([k["t"] for k in keys])
        self.length=js.get("length",self.t[-1]); self.lag=js.get("lag",{})
        self.ch={}
        for chan,rest in (("angles",0.0),("offset",(0.0,0.0)),("scale",(1.0,1.0)),("stretch",1.0),("reach",1.0)):
            slots=set().union(*[k.get(chan,{}).keys() for k in keys])
            for s in slots:
                vals=np.array([k.get(chan,{}).get(s,rest) for k in keys],float)
                vals=vals.reshape(len(keys),-1)
                comps=[(vals[:,c],monotone_tangents(self.t,vals[:,c])) for c in range(vals.shape[1])]
                self.ch[(chan,s)]=comps
    def sample(self, x):
        out={"angles":{},"offset":{},"scale":{},"stretch":{},"reach":{}}
        for (chan,s),comps in self.ch.items():
            xs=x-self.lag.get(s,0.0)
            v=[hermite(self.t,y,m,xs) for y,m in comps]
            out[chan][s]=v[0] if chan in ("angles","stretch","reach") else tuple(v)
        return out
    def frame(self, x, **kw):
        p=self.sample(x); w=self.js.get("swing_sort_window")
        order=ORDER_SWING if (w and w[0]<=x<=w[1]) else ORDER
        return render(p["angles"],offset=p["offset"],scale=p["scale"],stretch=p["stretch"],reach=p["reach"],order=order,**kw)
