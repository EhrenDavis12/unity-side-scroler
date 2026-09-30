import math, json
from fk import render, ORDER, ORDER_SWING
from PIL import Image
# keyframes: time (s) -> joint angles (deg, + = counter-clockwise on screen, local to parent)
# ease: the curve INTO this key
K=[
 (0.00,"rest",{}),
 (0.26,"windup",{"torso":7,"head":-3,"plume":-6,"upper_arm_sword":-40,"forearm_sword":-25,"hand_sword":-30,"pauldron_sword":-10,
                 "upper_arm_shield":-8,"forearm_shield":8,"thigh_left":5,"shin_left":-6,"thigh_right":-3,"shin_right":4}),
 (0.34,"hold",   {"torso":8,"head":-3,"plume":-9,"upper_arm_sword":-46,"forearm_sword":-28,"hand_sword":-34,"pauldron_sword":-12,
                 "upper_arm_shield":-10,"forearm_shield":10,"thigh_left":5,"shin_left":-6,"thigh_right":-3,"shin_right":4}),
 (0.43,"strike", {"torso":-8,"head":4,"plume":10,"upper_arm_sword":100,"forearm_sword":12,"hand_sword":20,"pauldron_sword":20,
                 "upper_arm_shield":24,"forearm_shield":-30,"thigh_left":-10,"shin_left":14,"thigh_right":8,"shin_right":-12}),
 (0.53,"follow", {"torso":-11,"head":6,"plume":16,"upper_arm_sword":112,"forearm_sword":34,"hand_sword":-14,"pauldron_sword":24,
                 "upper_arm_shield":26,"forearm_shield":-32,"thigh_left":-12,"shin_left":16,"thigh_right":10,"shin_right":-14}),
 (0.95,"recover",{"torso":-2,"head":1,"plume":-4,"upper_arm_sword":12,"forearm_sword":3,"hand_sword":3,"pauldron_sword":3,
                 "upper_arm_shield":3,"forearm_shield":-4,"thigh_left":-2,"shin_left":3,"thigh_right":2,"shin_right":-3}),
 (1.20,"rest",   {}),
]
SWING=(0.34,0.95)   # sword arm drawn over the shield between these times
EASE={"windup":"inout","hold":"linear","strike":"in","follow":"out","recover":"inout","rest":"inout"}
def ease(t,kind):
    if kind=="in": return t*t*t
    if kind=="out": return 1-(1-t)**3
    if kind=="inout": return t*t*(3-2*t)
    return t
def sample(t):
    for (t0,_,a),(t1,n1,b) in zip(K,K[1:]):
        if t0<=t<=t1:
            u=ease((t-t0)/(t1-t0),EASE[n1]); keys=set(a)|set(b)
            return {k:a.get(k,0)+(b.get(k,0)-a.get(k,0))*u for k in keys}
    return dict(K[-1][2])
if __name__=="__main__":
    fps=30; frames=[]
    for i in range(int(1.2*fps)+10):
        t=min(i/fps,1.2); frames.append(render(sample(t),order=ORDER_SWING if SWING[0]<=t<=SWING[1] else ORDER).crop((40,0,1000,960)).convert("RGB").resize((480,480),Image.LANCZOS))
    frames[0].save("attack_preview.gif",save_all=True,append_images=frames[1:],duration=int(1000/fps),loop=0)
    # key-pose strip
    s=Image.new("RGB",(480*6,480))
    for i,(t,n,_) in enumerate(K[:6]): s.paste(render(sample(t),order=ORDER_SWING if SWING[0]<=t<=SWING[1] else ORDER).crop((40,0,1000,960)).convert("RGB").resize((480,480),Image.LANCZOS),(i*480,0))
    s.resize((1440,240),Image.LANCZOS).save("attack_keys.png")
    json.dump({"swing_sort_window":SWING,"keys":[{"t":t,"name":n,"angles":a} for t,n,a in K],"ease_into":EASE},open("attack_keys.json","w"),indent=1)
