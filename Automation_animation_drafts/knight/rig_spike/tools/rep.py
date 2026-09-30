"""Generic Replicate runner: python rep.py <owner/name[:version]> <inputs.json> <out_path>.
File inputs: any string value starting with 'file:' is inlined as a data URI."""
import sys, json, time, base64, mimetypes, urllib.request, urllib.error, os
tok=os.environ["REPLICATE_API_TOKEN"]; H={"Authorization":"Bearer "+tok,"Content-Type":"application/json"}
model,inp,out=sys.argv[1],json.load(open(sys.argv[2])),sys.argv[3]
def fix(v):
    if isinstance(v,str) and v.startswith("file:"):
        p=v[5:]; mt=mimetypes.guess_type(p)[0] or "application/octet-stream"
        return f"data:{mt};base64,"+base64.b64encode(open(p,"rb").read()).decode()
    if isinstance(v,list): return [fix(x) for x in v]
    return v
inp={k:fix(v) for k,v in inp.items()}
if ":" in model: url="https://api.replicate.com/v1/predictions"; body={"version":model.split(":")[1],"input":inp}
else: url=f"https://api.replicate.com/v1/models/{model}/predictions"; body={"input":inp}
for w in (0,20,40,80,160):
    time.sleep(w)
    try: p=json.load(urllib.request.urlopen(urllib.request.Request(url,data=json.dumps(body).encode(),headers=H))); break
    except urllib.error.HTTPError as e:
        if e.code!=429: print("HTTP",e.code,e.read()[:400]); sys.exit(1)
t0=time.time()
while p["status"] not in ("succeeded","failed","canceled"):
    time.sleep(5); p=json.load(urllib.request.urlopen(urllib.request.Request(p["urls"]["get"],headers=H)))
print(model,p["status"],"%.0fs"%(time.time()-t0))
if p["status"]!="succeeded": print(p.get("error")); sys.exit(1)
o=p["output"]; json.dump(o,open(out+".output.json","w"),indent=1)
urls=[]
def walk(x):
    if isinstance(x,str) and x.startswith("http"): urls.append(x)
    elif isinstance(x,list): [walk(y) for y in x]
    elif isinstance(x,dict): [walk(y) for y in x.values()]
walk(o)
for i,u in enumerate(urls):
    ext=os.path.splitext(u.split("?")[0])[1]; path=out if i==0 else f"{os.path.splitext(out)[0]}_{i}{ext}"
    urllib.request.urlretrieve(u,path); print("saved",path)
