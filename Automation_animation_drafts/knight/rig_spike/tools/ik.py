"""Two-bone IK for the legs: feet stay planted where they are at rest; the hips move and the
thigh/shin angles are solved so each ankle still lands on its target. Knees always bend OUTWARD."""
import math, numpy as np
def ang(v): return math.degrees(math.atan2(-v[1], v[0]))          # screen vector -> math angle (y up)
def rot(v, a):                                                       # + = counter-clockwise on screen
    r=math.radians(a); c,s=math.cos(r),math.sin(r); return np.array([v[0]*c+v[1]*s, -v[0]*s+v[1]*c])
def wrap(a): return (a+180)%360-180
class Legs:
    def __init__(self, J):
        self.J={k:np.array(v,float) for k,v in J.items()}
        self.legs={}
        for side,out in (("left",-1),("right",+1)):                  # out: which side the knee goes
            H,K,A=self.J[f"thigh_{side}"],self.J[f"shin_{side}"],self.J[f"boot_{side}"]
            self.legs[side]=dict(L1=np.linalg.norm(K-H),L2=np.linalg.norm(A-K),
                                 rest_t=ang(K-H),rest_s=ang(A-K),target=A.copy(),out=out)
    def solve(self, hips_angle=0.0, hips_offset=(0,0)):
        """returns local angles for thigh/shin/boot of both legs"""
        out={}; hj=self.J["hips"]
        for side,L in self.legs.items():
            H=hj+rot(self.J[f"thigh_{side}"]-hj, hips_angle)+np.array(hips_offset,float)
            d_vec=L["target"]-H; d=np.linalg.norm(d_vec)
            d=min(max(d,abs(L["L1"]-L["L2"])+1e-3),L["L1"]+L["L2"]-1e-3)
            base=ang(d_vec)
            a1=math.degrees(math.acos((L["L1"]**2+d**2-L["L2"]**2)/(2*L["L1"]*d)))
            # knee to the outside: for the left leg the knee swings toward screen-left
            cands=[base+a1, base-a1]
            def knee_x(t): return (H+np.array([math.cos(math.radians(t)),-math.sin(math.radians(t))])*L["L1"])[0]
            t_dir=min(cands,key=knee_x) if L["out"]<0 else max(cands,key=knee_x)
            Kp=H+np.array([math.cos(math.radians(t_dir)),-math.sin(math.radians(t_dir))])*L["L1"]
            s_dir=ang(L["target"]-Kp)
            thigh_world=wrap(t_dir-L["rest_t"]); shin_world=wrap(s_dir-L["rest_s"])
            out[f"thigh_{side}"]=wrap(thigh_world-hips_angle)
            out[f"shin_{side}"]=wrap(shin_world-thigh_world)
            out[f"boot_{side}"]=wrap(-shin_world)                    # foot stays flat, as at rest
        return out
