import sys, json, time, base64, urllib.request, urllib.error, os
tok=os.environ["REPLICATE_API_TOKEN"]
V="891411c38a6ed2d44c004b7b9e44217df7a5b07848f29ddefd2e28bc7cbf93bc"
def run(img, prompt, out):
    uri="data:image/png;base64,"+base64.b64encode(open(img,"rb").read()).decode()
    body={"version":V,"input":{"image":uri,"text_prompt":prompt}}
    req=urllib.request.Request("https://api.replicate.com/v1/predictions",data=json.dumps(body).encode(),headers={"Authorization":"Bearer "+tok,"Content-Type":"application/json","Prefer":"wait=60"})
    for w in (0,15,30,60,120):
        time.sleep(w)
        try: p=json.load(urllib.request.urlopen(req)); break
        except urllib.error.HTTPError as e:
            if e.code!=429: raise
    while p["status"] not in ("succeeded","failed","canceled"):
        time.sleep(3); p=json.load(urllib.request.urlopen(urllib.request.Request(p["urls"]["get"],headers={"Authorization":"Bearer "+tok})))
    if p["status"]!="succeeded": print("FAIL",out,p.get("error")); return
    o=p["output"]; o=o if isinstance(o,str) else o[0]
    urllib.request.urlretrieve(o,out); print("ok",out)
run(sys.argv[1],sys.argv[2],sys.argv[3])
